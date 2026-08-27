using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimOps.Application;
using SimOps.Experiments;
using SimOps.Infrastructure;

internal sealed class SimulationWorker(PostgresRunStore store, ILogger<SimulationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await store.InitializeAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            ClaimedSimulationJob? job = null;
            try
            {
                job = await store.ClaimSimulationJobAsync(stoppingToken);
                if (job is null) { await Task.Delay(250, stoppingToken); continue; }
                using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var heartbeat = KeepLeaseAsync(job, execution);
                try
                {
                    if (job.ExecutionFingerprint != ExperimentRunner.ExecutionFingerprint)
                        throw new InvalidOperationException("EXECUTION_ARTIFACT_CHANGED");
                    if (job.Kind == "cell")
                    {
                        var cell = await Task.Run(() => ExperimentRunner.ExecuteCell(job.Definition, job.VariantId!, job.AgentId!, execution.Token), execution.Token);
                        await store.CompleteSimulationCellAsync(job, cell, execution.Token);
                    }
                    else
                    {
                        var cells = await store.LoadSimulationCellsAsync(job.BatchId, execution.Token);
                        var report = await Task.Run(() => ExperimentRunner.AssembleReport(job.Definition, cells, execution.Token), execution.Token);
                        await store.CompleteSimulationReportAsync(job, report, execution.Token);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested && execution.IsCancellationRequested)
                {
                    logger.LogInformation("Simulation {JobId} lost its lease or was cancelled; output discarded", job.Id);
                }
                finally
                {
                    await execution.CancelAsync();
                    await heartbeat;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Simulation job {JobId} failed", job?.Id);
                if (job is not null)
                    try { await store.FailSimulationJobAsync(job, exception.Message == "EXECUTION_ARTIFACT_CHANGED" ? exception.Message : exception.GetType().Name, stoppingToken); }
                    catch (Exception persistenceException) when (persistenceException is not OperationCanceledException)
                    { logger.LogWarning(persistenceException, "Failure persistence failed; lease expiry will recover the job"); }
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task KeepLeaseAsync(ClaimedSimulationJob job, CancellationTokenSource execution)
    {
        try
        {
            while (!execution.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), execution.Token);
                if (!await store.HeartbeatSimulationAsync(job, execution.Token))
                { await execution.CancelAsync(); return; }
            }
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Heartbeat failed; stopping simulation {JobId}", job.Id);
            await execution.CancelAsync();
        }
    }
}
