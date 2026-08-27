using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimOps.Application;
using SimOps.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("SIMOPS_CONNECTION_STRING")
    ?? (builder.Environment.IsDevelopment()
        ? "Host=127.0.0.1;Port=54329;Database=simops;Username=simops;Password=simops-local-only;Maximum Pool Size=10"
        : throw new InvalidOperationException("SIMOPS_CONNECTION_STRING is required outside Development."));
builder.Services.AddSingleton(new PostgresRunStore(connectionString));
builder.Services.AddSingleton<ReplayVerifier>();
builder.Services.AddHostedService<VerificationWorker>();
builder.Services.AddHostedService<SimulationWorker>();
await builder.Build().RunAsync();

internal sealed class VerificationWorker : BackgroundService
{
    private readonly PostgresRunStore _store;
    private readonly ReplayVerifier _verifier;
    private readonly ILogger<VerificationWorker> _logger;

    public VerificationWorker(PostgresRunStore store, ReplayVerifier verifier, ILogger<VerificationWorker> logger)
    {
        _store = store;
        _verifier = verifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _store.InitializeAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            ClaimedJob? job;
            try
            {
                job = await _store.ClaimJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Job polling failed; retrying after backoff");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            if (job is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                continue;
            }

            try
            {
                var submission = await _store.LoadSubmissionAsync(job.RunId, stoppingToken);
                var output = _verifier.Verify(submission);
                await _store.CompleteJobAsync(job, output, stoppingToken);
                _logger.LogInformation("Run {RunId} completed: verified={Verified} code={Code}", job.RunId, output.Verified, output.RejectionCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Verification job {JobId} failed", job.JobId);
                try
                {
                    await _store.FailJobAsync(job, exception.GetType().Name, stoppingToken);
                }
                catch (Exception persistenceException) when (persistenceException is not OperationCanceledException)
                {
                    _logger.LogWarning(persistenceException, "Could not persist failure for {JobId}; its lease will expire", job.JobId);
                }
            }
        }
    }
}
