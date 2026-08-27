using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimOps.Application;
using SimOps.Infrastructure;

internal sealed class AnalysisWorker(PostgresRunStore store, IAnalysisProvider provider, ILogger<AnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await store.InitializeAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await store.ClaimAnalysisAsync(stoppingToken);
                if (job is null) { await Task.Delay(500, stoppingToken); continue; }
                await Process(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Analysis polling/persistence failed; recover by lease");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task Process(ClaimedAnalysisJob job, CancellationToken stoppingToken)
    {
        using var work = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = KeepLease(job, work, heartbeatStop.Token);
        try
        {
            work.CancelAfter(TimeSpan.FromSeconds(120));
            // No DB connection/transaction is held while the provider works.
            var response = await provider.AnalyzeAsync(job.Snapshot, work.Token).WaitAsync(work.Token);
            if (await store.CompleteAnalysisAsync(job, response, work.Token))
                logger.LogInformation("Analysis {JobId} completed with {Provider}/{Model}", job.Id, response.Provider, response.Model);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            await store.FailAnalysisAsync(job, "ANALYSIS_TIMEOUT_OR_LEASE_LOST", true, stoppingToken);
        }
        catch (AnalysisValidationException ex) { await store.FailAnalysisAsync(job, ex.Code, false, stoppingToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never persist raw provider text/headers/errors (may contain sensitive data).
            await store.FailAnalysisAsync(job, "ANALYSIS_PROVIDER_FAILED", true, stoppingToken);
            logger.LogWarning("Analysis {JobId} failed ({Type})", job.Id, ex.GetType().Name);
        }
        finally
        {
            await heartbeatStop.CancelAsync();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }

    private async Task KeepLease(ClaimedAnalysisJob job, CancellationTokenSource work, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                if (!await store.HeartbeatAnalysisAsync(job, token)) { await work.CancelAsync(); return; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { await work.CancelAsync(); }
    }
}
