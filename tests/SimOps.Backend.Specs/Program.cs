using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Npgsql;
using SimOps.Agent.Core;
using SimOps.Application;
using SimOps.Game.Core;
using SimOps.Infrastructure;

var mode = args.Length > 0 ? args[0] : "--http";
if (mode is "--ranking" or "--ranking-db") return await RankingSpecs.RunAsync(mode == "--ranking-db");
var tests = mode == "--lease"
    ? new (string Name, Func<Task> Execute)[]
    {
        ("VERIFY-002 expired lease is reclaimed and stale completion is fenced", LeaseRecoveryAsync),
        ("JOB-001 exhausted crash attempts become terminal failure", ExhaustedLeaseAsync),
        ("DB-001 fresh database applies all migrations and catalog seeds", FreshDatabaseAsync),
    }
    : new (string Name, Func<Task> Execute)[]
    {
        ("API-001 readiness and OpenAPI are available", ReadinessAndOpenApiAsync),
        ("API-002 operator routes reject missing credentials", UnauthorizedAsync),
        ("EVENT-001 replay emits matching encounter start and end events", EventBoundariesAsync),
        ("VERIFY-001 concurrent duplicate submissions create one run", IdempotencyAsync),
        ("VERIFY-003 tampered result hashes are rejected by the worker", TamperedHashAsync),
        ("VERIFY-004 invalid action sequence is rejected at the API", InvalidSequenceAsync),
        ("VERIFY-005 unoffered reward is rejected by replay", UnofferedRewardAsync),
        ("API-003 submission latency remains below the local target", SubmissionLatencyAsync),
    };

var failures = 0;
foreach (var test in tests)
{
    var timer = Stopwatch.StartNew();
    try
    {
        await test.Execute();
        Console.WriteLine($"PASS {test.Name} ({timer.ElapsedMilliseconds} ms)");
    }
    catch (Exception exception)
    {
        failures += 1;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"Backend Specs: {tests.Length - failures} passed, {failures} failed");
return failures == 0 ? 0 : 1;

static HttpClient CreateClient(bool authenticated = true)
{
    var client = new HttpClient
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("SIMOPS_API_URL") ?? "http://127.0.0.1:5080"),
        Timeout = TimeSpan.FromSeconds(15),
    };
    if (authenticated)
    {
        client.DefaultRequestHeaders.Add("X-SimOps-Admin-Key", Environment.GetEnvironmentVariable("SIMOPS_ADMIN_KEY") ?? "simops-local-dev-key");
    }

    return client;
}

static RunSubmission CreateSubmission(ulong seed = 42UL)
{
    var config = GameConfig.CreateBaseline();
    var scoreRule = ScoreRule.CreateBaseline();
    var agent = AgentFactory.CreateDefinitions().Single(definition => definition.Persona == AgentPersona.Greedy);
    var run = SyntheticSimulation.Execute(config, scoreRule, agent, seed);
    return new RunSubmission(
        Guid.NewGuid().ToString("N"), agent.Id, agent.Version, config.GameVersion, config.Checksum,
        scoreRule.Version, scoreRule.Checksum, seed.ToString(CultureInfo.InvariantCulture), run.Result.ResultHash,
        run.Actions.Select(action => new SubmittedAction(action.Sequence, action.ActionType, action.RewardId)).ToArray());
}

static async Task<SubmissionReceipt> SubmitAsync(HttpClient client, RunSubmission submission)
{
    using var response = await client.PostAsJsonAsync("/api/v1/synthetic-runs", submission, ContractJson.Options);
    Equal(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
    return await response.Content.ReadFromJsonAsync<SubmissionReceipt>(ContractJson.Options)
        ?? throw new InvalidOperationException("Submission receipt was empty.");
}

static async Task<RunStatusResponse> WaitForTerminalAsync(HttpClient client, Guid runId)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < TimeSpan.FromSeconds(10))
    {
        var status = await client.GetFromJsonAsync<RunStatusResponse>($"/api/v1/runs/{runId}", ContractJson.Options)
            ?? throw new InvalidOperationException("Run status was empty.");
        if (status.Status is "verified" or "rejected" or "failed")
        {
            return status;
        }

        await Task.Delay(100);
    }

    throw new TimeoutException("Worker did not complete the run within 10 seconds.");
}

static async Task ReadinessAndOpenApiAsync()
{
    using var client = CreateClient();
    using var ready = await client.GetAsync("/health/ready");
    Equal(HttpStatusCode.OK, ready.StatusCode, "Readiness failed.");
    using var openApi = await client.GetAsync("/openapi/v1.json");
    Equal(HttpStatusCode.OK, openApi.StatusCode, "OpenAPI failed.");
    True((await openApi.Content.ReadAsStringAsync()).Contains("synthetic-runs", StringComparison.Ordinal), "OpenAPI did not describe submission.");
}

static async Task UnauthorizedAsync()
{
    using var client = CreateClient(false);
    using var response = await client.GetAsync("/api/v1/catalog/baseline");
    Equal(HttpStatusCode.Unauthorized, response.StatusCode, "Missing operator credentials were accepted.");
}

static Task EventBoundariesAsync()
{
    for (ulong seed = 0; seed < 100; seed++)
    {
        var output = new ReplayVerifier().Verify(CreateSubmission(seed));
        True(output.Verified, "Telemetry fixture did not verify.");
        Equal(output.Stages.Count, output.Events.Count(entry => entry.EventType == "encounter_started"), "Encounter start missing.");
        Equal(output.Stages.Count, output.Events.Count(entry => entry.EventType == "encounter_ended"), "Encounter end missing.");
        Equal(1, output.Events.Count(entry => entry.EventType == "run_ended"), "Run end missing.");
    }

    return Task.CompletedTask;
}

static async Task IdempotencyAsync()
{
    using var client = CreateClient();
    var submission = CreateSubmission();
    var receipts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SubmitAsync(client, submission)));
    Equal(1, receipts.Select(receipt => receipt.RunId).Distinct().Count(), "Duplicate submissions created multiple runs.");
    var status = await WaitForTerminalAsync(client, receipts[0].RunId);
    Equal("verified", status.Status, "Valid replay was not verified.");
    Equal(submission.ClientResultHash, status.Result?.ResultHash, "Authoritative result hash changed.");
    True(status.EventCount > status.ActionCount, "Authoritative telemetry was not stored.");

    using var conflict = await client.PostAsJsonAsync(
        "/api/v1/synthetic-runs", submission with { ClientResultHash = new string('0', 64) }, ContractJson.Options);
    Equal(HttpStatusCode.Conflict, conflict.StatusCode, "Changed payload reused an idempotency key.");
}

static async Task TamperedHashAsync()
{
    using var client = CreateClient();
    var receipt = await SubmitAsync(client, CreateSubmission(43UL) with { ClientResultHash = new string('0', 64) });
    var status = await WaitForTerminalAsync(client, receipt.RunId);
    Equal("rejected", status.Status, "Tampered hash was accepted.");
    Equal("RESULT_MISMATCH", status.RejectionCode, "Wrong tamper rejection code.");
}

static async Task InvalidSequenceAsync()
{
    using var client = CreateClient();
    var submission = CreateSubmission(44UL);
    var actions = submission.Actions.ToArray();
    actions[0] = actions[0] with { Sequence = 2 };
    using var response = await client.PostAsJsonAsync(
        "/api/v1/synthetic-runs", submission with { Actions = actions }, ContractJson.Options);
    Equal(HttpStatusCode.BadRequest, response.StatusCode, "Invalid sequence was accepted.");
    var error = await response.Content.ReadFromJsonAsync<ApiError>(ContractJson.Options);
    Equal("ACTION_SEQUENCE_INVALID", error?.Code, "Wrong sequence rejection code.");
}

static async Task UnofferedRewardAsync()
{
    using var client = CreateClient();
    var submission = CreateSubmission(45UL);
    var actions = submission.Actions.ToArray();
    var rewardIndex = Array.FindIndex(actions, action => action.ActionType == GameActionType.ChooseReward);
    True(rewardIndex >= 0, "Fixture did not reach reward selection.");
    actions[rewardIndex] = actions[rewardIndex] with { RewardId = "not-offered" };
    var receipt = await SubmitAsync(client, submission with { Actions = actions });
    var status = await WaitForTerminalAsync(client, receipt.RunId);
    Equal("rejected", status.Status, "Unoffered reward was accepted.");
    Equal("REWARD_NOT_OFFERED", status.RejectionCode, "Wrong reward rejection code.");
}

static async Task SubmissionLatencyAsync()
{
    await Task.Delay(1_100);
    using var client = CreateClient();
    var latencies = new List<double>();
    var runIds = new List<Guid>();
    for (ulong seed = 100; seed < 110; seed++)
    {
        var submission = CreateSubmission(seed);
        var timer = Stopwatch.StartNew();
        var receipt = await SubmitAsync(client, submission);
        timer.Stop();
        latencies.Add(timer.Elapsed.TotalMilliseconds);
        runIds.Add(receipt.RunId);
    }

    latencies.Sort();
    var p95 = latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1];
    Console.WriteLine($"  submissionP95Ms={p95.ToString("F2", CultureInfo.InvariantCulture)} sampleSize={latencies.Count}");
    True(p95 < 500d, "Local submission p95 exceeded 500 ms.");
    foreach (var runId in runIds)
    {
        Equal("verified", (await WaitForTerminalAsync(client, runId)).Status, "Latency fixture was not verified.");
    }
}

static string ConnectionString() => Environment.GetEnvironmentVariable("SIMOPS_CONNECTION_STRING")
    ?? "Host=127.0.0.1;Port=54329;Database=simops;Username=simops;Password=simops-local-only";

static async Task LeaseRecoveryAsync()
{
    await using var store = new PostgresRunStore(ConnectionString());
    await store.InitializeAsync();
    var submission = CreateSubmission(300UL);
    var receipt = await store.SubmitAsync(submission);
    var first = await store.ClaimJobAsync() ?? throw new InvalidOperationException("No job was claimed. Stop the external Worker for lease tests.");
    Equal(receipt.RunId, first.RunId, "A different queued job interfered with lease tests.");
    await ExpireLeaseAsync(first, false);
    var second = await store.ClaimJobAsync() ?? throw new InvalidOperationException("Expired job was not reclaimed.");
    Equal(first.JobId, second.JobId, "Reclaim returned a different job.");
    True(first.LockToken != second.LockToken, "Reclaim did not rotate the fencing token.");
    var output = new ReplayVerifier().Verify(submission);
    await store.CompleteJobAsync(first, output);
    Equal("verifying", (await store.GetRunAsync(receipt.RunId))?.Status, "A stale worker committed its result.");
    await store.CompleteJobAsync(second, output);
    var completed = await store.GetRunAsync(receipt.RunId);
    Equal("verified", completed?.Status, "Reclaimed worker did not commit.");
    await store.CompleteJobAsync(second, output);
    Equal(completed?.EventCount, (await store.GetRunAsync(receipt.RunId))?.EventCount, "Repeated completion duplicated events.");
}

static async Task ExhaustedLeaseAsync()
{
    await using var store = new PostgresRunStore(ConnectionString());
    var receipt = await store.SubmitAsync(CreateSubmission(301UL));
    var job = await store.ClaimJobAsync() ?? throw new InvalidOperationException("No exhausted-attempt fixture job was claimed.");
    Equal(receipt.RunId, job.RunId, "A different queued job interfered with exhausted-attempt tests.");
    await ExpireLeaseAsync(job, true);
    _ = await store.ClaimJobAsync();
    Equal("failed", (await store.GetRunAsync(receipt.RunId))?.Status, "An exhausted expired lease remained running forever.");
}

static async Task ExpireLeaseAsync(ClaimedJob job, bool exhaustAttempts)
{
    await using var connection = new NpgsqlConnection(ConnectionString());
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        "UPDATE simops.jobs SET locked_until = now() - interval '1 second', attempts = CASE WHEN @exhaust THEN max_attempts ELSE attempts END WHERE id = @id AND lock_token = @token", connection);
    command.Parameters.AddWithValue("exhaust", exhaustAttempts);
    command.Parameters.AddWithValue("id", job.JobId);
    command.Parameters.AddWithValue("token", job.LockToken);
    Equal(1, await command.ExecuteNonQueryAsync(), "Lease fixture was not updated.");
}

static async Task FreshDatabaseAsync()
{
    var settings = new NpgsqlConnectionStringBuilder(ConnectionString());
    if (settings.Host != "127.0.0.1" || settings.Port != 54329)
        throw new InvalidOperationException("Fresh database specs are limited to the project's local PostgreSQL instance.");
    var databaseName = "simops_spec_" + Guid.NewGuid().ToString("N");
    await using var admin = new NpgsqlConnection(settings.ConnectionString);
    await admin.OpenAsync();
    await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin))
        await create.ExecuteNonQueryAsync();
    try
    {
        settings.Database = databaseName;
        await using var store = new PostgresRunStore(settings.ConnectionString);
        await store.InitializeAsync();
        await store.InitializeAsync();
        True(await store.PingAsync(), "Fresh database is unavailable.");
        Equal(PostgresRunStore.BaselineSeasonId, (await store.GetActiveSeasonAsync())?.SeasonId, "Fresh baseline season was not seeded.");
        var player = await store.RegisterPlayerAsync(new RegisterPlayerRequest("Fresh schema"));
        Equal(player.PlayerId, await store.AuthenticatePlayerAsync(player.Credential), "Fresh schema identity failed.");
    }
    finally
    {
        // Only this test's just-created UUID database on the fixed local instance is removed.
        await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName}", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message} expected={expected} actual={actual}");
    }
}
