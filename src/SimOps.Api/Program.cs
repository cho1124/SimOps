using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using SimOps.Agent.Core;
using SimOps.Application;
using SimOps.Game.Core;
using SimOps.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("SIMOPS_CONNECTION_STRING")
    ?? (builder.Environment.IsDevelopment()
        ? "Host=127.0.0.1;Port=54329;Database=simops;Username=simops;Password=simops-local-only;Maximum Pool Size=20"
        : throw new InvalidOperationException("SIMOPS_CONNECTION_STRING is required outside Development."));
var adminKey = Environment.GetEnvironmentVariable("SIMOPS_ADMIN_KEY")
    ?? (builder.Environment.IsDevelopment()
        ? "simops-local-dev-key"
        : throw new InvalidOperationException("SIMOPS_ADMIN_KEY is required outside Development."));

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Services.AddSingleton(new PostgresRunStore(connectionString));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiError("RATE_LIMITED", "Submission capacity is temporarily full.", true,
                context.HttpContext.Response.Headers["X-Correlation-Id"].ToString()), token);
    };
    options.AddPolicy("submission", _ => RateLimitPartition.GetFixedWindowLimiter("submission", _ =>
        new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
});

var app = builder.Build();
await app.Services.GetRequiredService<PostgresRunStore>().InitializeAsync();

app.Use(async (context, next) =>
{
    var correlationId = Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/openapi"))
    {
        var supplied = context.Request.Headers["X-SimOps-Admin-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(adminKey)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ApiError("UNAUTHORIZED", "A valid operator key is required.", false, correlationId));
            return;
        }
    }

    try
    {
        await next();
    }
    catch (SubmissionValidationException exception)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new ApiError(exception.Code, exception.Message, false, correlationId));
    }
    catch (SubmissionConflictException exception)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new ApiError("IDEMPOTENCY_CONFLICT", exception.Message, false, correlationId));
    }
    catch (BadHttpRequestException)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new ApiError("REQUEST_INVALID", "The request body is invalid.", false, correlationId));
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Request {CorrelationId} failed", correlationId);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ApiError("INTERNAL_ERROR", "The request could not be completed.", true, correlationId));
    }
});

app.UseRateLimiter();
app.MapOpenApi();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (PostgresRunStore store, CancellationToken token) =>
    await store.PingAsync(token) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/api/v1/catalog/baseline", () => Results.Ok(new
{
    gameConfig = GameConfig.CreateBaseline(),
    scoreRule = ScoreRule.CreateBaseline(),
    agents = AgentFactory.CreateDefinitions(),
}));
app.MapPost("/api/v1/synthetic-runs", async (RunSubmission submission, PostgresRunStore store, CancellationToken token) =>
{
    var receipt = await store.SubmitAsync(submission, token);
    return Results.Accepted($"/api/v1/runs/{receipt.RunId}", receipt);
}).RequireRateLimiting("submission");
app.MapGet("/api/v1/runs/{runId:guid}", async (Guid runId, PostgresRunStore store, CancellationToken token) =>
{
    var run = await store.GetRunAsync(runId, token);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

await app.RunAsync();
