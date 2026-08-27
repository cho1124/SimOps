using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using SimOps.Application;
using SimOps.Experiments;
using SimOps.Infrastructure;

internal static class ExperimentSpecs
{
    private static ExperimentDefinition Registered => ExperimentJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "registered-plan.json")));
    private static ExperimentDefinition Fixture() => Registered with { ExperimentId = "spec-" + Guid.NewGuid().ToString("N"), RunsPerCell = 20, FirstSeed = "0", BootstrapReplicates = 100 };
    private static readonly string Connection = Environment.GetEnvironmentVariable("SIMOPS_CONNECTION_STRING") ?? "Host=127.0.0.1;Port=54329;Database=simops;Username=simops;Password=simops-local-only";
    private static string? _isolated;

    public static async Task<int> RunAsync(bool database)
    {
        var tests = database ? new (string, Func<Task>)[] {
            ("EXP-001 Ready plan, variants, configs and audit rows are immutable", Immutability),
            ("BATCH-001 duplicate completion and expired lease fencing", LeaseAndDuplicates),
            ("BATCH-002 recovery skips committed cells and cancellation preserves evidence", RecoveryAndCancel),
            ("BATCH-004 exhausted leases become terminal failures", Exhaustion),
            ("BATCH-005 two-batch admission is race-safe", Capacity),
            ("EXP-002 aggregation after all cells matches local CLI digest", Aggregate),
        } : new (string, Func<Task>)[] {
            ("EXP-HTTP-001 operator authorization and bounded strict input", HttpSecurity),
            ("EXP-HTTP-002 draft revision, Ready lock and idempotent start", HttpLifecycle),
            ("EXP-HTTP-003 registered 18000-run experiment matches frozen digest", HttpRegistered),
        };
        NpgsqlConnection? admin = null;
        string? databaseName = null;
        if (database)
        {
            var settings = new NpgsqlConnectionStringBuilder(Connection);
            if (settings.Host != "127.0.0.1" || settings.Port != 54329) throw new InvalidOperationException("Isolated specs require local SimOps PostgreSQL.");
            databaseName = "simops_experiment_spec_" + Guid.NewGuid().ToString("N");
            admin = new NpgsqlConnection(settings.ConnectionString); await admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin)) await create.ExecuteNonQueryAsync();
            settings.Database = databaseName; settings.Pooling = false; _isolated = settings.ConnectionString;
            await using var store = Store(); await store.InitializeAsync(); await store.InitializeAsync();
        }
        var failed = 0;
        try
        {
            foreach (var (name, run) in tests)
            {
                try { await run(); Console.WriteLine("PASS " + name); }
                catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
            }
        }
        finally
        {
            if (admin is not null)
            {
                // The only removed database is the UUID-named fixture created above on the fixed local instance.
                await using (var drop = new NpgsqlCommand($"DROP DATABASE {databaseName}", admin)) await drop.ExecuteNonQueryAsync();
                await admin.DisposeAsync(); _isolated = null;
            }
        }
        Console.WriteLine($"Experiment backend specs: {tests.Length - failed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
    private static PostgresRunStore Store() => new(_isolated ?? Connection);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> action, string? code = null)
    {
        try { await action(); } catch (ExperimentCommandException ex) { Check(code is null || ex.Code == code, "Wrong error: " + ex.Code); return; }
        throw new InvalidOperationException("Expected command rejection.");
    }
    private static async Task<Guid> Start(PostgresRunStore store, ExperimentDefinition plan)
    {
        var draft = await store.SaveExperimentAsync(new(plan));
        await store.MarkExperimentReadyAsync(plan.ExperimentId, draft.PlanHash);
        return await store.StartBatchAsync(plan.ExperimentId, new(draft.PlanHash, Guid.NewGuid().ToString("N")));
    }
    private static async Task Sql(string sql, Guid? id = null, string? name = null)
    {
        await using var connection = new NpgsqlConnection(_isolated ?? Connection); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is not null) command.Parameters.AddWithValue("id", id.Value);
        if (name is not null) command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task Immutability()
    {
        await using var store = Store(); var plan = Fixture();
        var draft = await store.SaveExperimentAsync(new(plan));
        await store.MarkExperimentReadyAsync(plan.ExperimentId, draft.PlanHash);
        await Reject(() => store.SaveExperimentAsync(new(plan with { Hypothesis = "changed" }, 1)), "EXPERIMENT_LOCKED");
        foreach (var sql in new[] {
            "UPDATE simops.experiments SET definition='{}'::jsonb WHERE id=@name",
            "UPDATE simops.experiments SET status='draft' WHERE id=@name",
            "UPDATE simops.experiment_variants SET role='treatment' WHERE experiment_id=@name",
            "INSERT INTO simops.experiment_variants SELECT experiment_id,'extra',role,config_checksum FROM simops.experiment_variants WHERE experiment_id=@name LIMIT 1",
            "UPDATE simops.game_configs SET content='{}' WHERE checksum IN (SELECT config_checksum FROM simops.experiment_variants WHERE experiment_id=@name)",
            "UPDATE simops.experiment_audit SET action='changed' WHERE experiment_id=@name" })
        {
            try { await Sql(sql, name: plan.ExperimentId); throw new InvalidOperationException("Immutable row changed."); }
            catch (PostgresException ex) when (ex.SqlState == "23514") { }
        }
    }
    private static async Task LeaseAndDuplicates()
    {
        await using var store = Store(); var plan = Fixture(); var batch = await Start(store, plan);
        try
        {
            var old = (await store.ClaimSimulationJobAsync())!;
            Check(await store.HeartbeatSimulationAsync(old), "Heartbeat failed.");
            var cell = ExperimentRunner.ExecuteCell(plan, old.VariantId!, old.AgentId!);
            await Sql("UPDATE simops.simulation_jobs SET locked_until=now()-interval '1 second' WHERE id=@id", old.Id);
            Check(!await store.CompleteSimulationCellAsync(old, cell), "Expired owner completed before reclaim.");
            var current = (await store.ClaimSimulationJobAsync())!;
            Check(current.Id == old.Id && current.LockToken != old.LockToken, "Expired job was not reclaimed.");
            Check(!await store.HeartbeatSimulationAsync(old), "Stale token renewed.");
            Check(!await store.CompleteSimulationCellAsync(old, cell), "Stale token completed.");
            var outcomes = await Task.WhenAll(store.CompleteSimulationCellAsync(current, cell), store.CompleteSimulationCellAsync(current, cell));
            Check(outcomes.Count(x => x) == 1, "Completion was not exactly once.");
            Check((await store.GetBatchAsync(batch))!.CompletedRuns == 20, "Progress counted duplicate output.");
        }
        finally { await store.CancelSimulationBatchAsync(batch); }
    }
    private static async Task RecoveryAndCancel()
    {
        await using var store = Store(); var plan = Fixture(); var batch = await Start(store, plan);
        var first = (await store.ClaimSimulationJobAsync())!;
        await store.CompleteSimulationCellAsync(first, ExperimentRunner.ExecuteCell(plan, first.VariantId!, first.AgentId!));
        var interrupted = (await store.ClaimSimulationJobAsync())!;
        await Sql("UPDATE simops.simulation_jobs SET locked_until=now()-interval '1 second' WHERE id=@id", interrupted.Id);
        await using var restarted = Store();
        var recovered = (await restarted.ClaimSimulationJobAsync())!;
        Check(recovered.Id == interrupted.Id && recovered.Id != first.Id, "Recovery repeated committed work.");
        await restarted.CancelSimulationBatchAsync(batch);
        Check(!await store.HeartbeatSimulationAsync(recovered), "Cancelled job retained its lease.");
        Check(!await store.CompleteSimulationCellAsync(recovered, ExperimentRunner.ExecuteCell(plan, recovered.VariantId!, recovered.AgentId!)), "Cancelled job wrote output.");
        var progress = (await store.GetBatchAsync(batch))!;
        Check(progress.Status == "cancelled" && progress.CompletedCells == 1, "Cancellation discarded completed evidence.");
        Check(await store.GetExperimentResultJsonAsync(plan.ExperimentId, false) is null, "Cancelled partial batch produced final metrics.");
    }
    private static async Task Exhaustion()
    {
        await using var store = Store(); var plan = Fixture(); var batch = await Start(store, plan);
        var job = (await store.ClaimSimulationJobAsync())!;
        await Sql("UPDATE simops.simulation_jobs SET locked_until=now()-interval '1 second',attempts=max_attempts WHERE id=@id", job.Id);
        _ = await store.ClaimSimulationJobAsync();
        Check((await store.GetBatchAsync(batch))!.Status == "failed", "Exhausted batch remained running.");
        Check((await store.GetExperimentAsync(plan.ExperimentId))!.Status == "failed", "Experiment failure not propagated.");
    }
    private static async Task Capacity()
    {
        await using var store = Store(); var plans = new[] { Fixture(), Fixture(), Fixture() };
        foreach (var plan in plans) { var saved = await store.SaveExperimentAsync(new(plan)); await store.MarkExperimentReadyAsync(plan.ExperimentId, saved.PlanHash); }
        var batches = await Task.WhenAll(plans.Select(async plan => {
            try { return (Guid?)await store.StartBatchAsync(plan.ExperimentId, new(ExperimentRunner.PlanHash(plan), Guid.NewGuid().ToString("N"))); }
            catch (ExperimentCommandException ex) when (ex.Code == "SIMULATION_CAPACITY") { return null; }
        }));
        try { Check(batches.Count(b => b.HasValue) == 2, "Capacity gate raced."); }
        finally { foreach (var batch in batches.OfType<Guid>()) await store.CancelSimulationBatchAsync(batch); }
    }
    private static async Task Aggregate()
    {
        await using var store = Store(); var plan = Fixture(); var batch = await Start(store, plan);
        for (var index = 0; index < 18; index++)
        {
            var job = (await store.ClaimSimulationJobAsync())!;
            Check(job.Kind == "cell", "Aggregation started before all cells finished.");
            await store.CompleteSimulationCellAsync(job, ExperimentRunner.ExecuteCell(plan, job.VariantId!, job.AgentId!));
        }
        var aggregate = (await store.ClaimSimulationJobAsync())!;
        Check(aggregate.Kind == "aggregate", "Aggregation job missing.");
        var report = ExperimentRunner.AssembleReport(plan, await store.LoadSimulationCellsAsync(batch));
        Check(report.ResultDigest == ExperimentRunner.Execute(plan).ResultDigest, "jsonb roundtrip changed canonical digest.");
        Check(await store.CompleteSimulationReportAsync(aggregate, report), "Report was not committed.");
        Check(!await store.CompleteSimulationReportAsync(aggregate, report), "Report completed twice.");
        try { await Sql("UPDATE simops.simulation_batches SET result_digest='tampered' WHERE id=@id", batch); throw new InvalidOperationException("Terminal snapshot changed."); }
        catch (PostgresException ex) when (ex.SqlState == "23514") { }
        await using var reloaded = Store();
        Check((await reloaded.GetExperimentAsync(plan.ExperimentId))!.Status == "analyzing", "Persisted status missing.");
        var summary = JsonSerializer.Deserialize<ExperimentReport>((await reloaded.GetExperimentResultJsonAsync(plan.ExperimentId, false))!, ExperimentJson.Options)!;
        Check(summary.Cells.All(c => c.Runs.Count == 0) && summary.ResultDigest == report.ResultDigest, "Summary leaks raw runs or loses provenance.");
        var decision = new ExperimentDecisionRequest(report.PlanHash, report.ResultDigest, "rejected", null, "Automated fixture decision; not a production approval.");
        await Reject(() => store.DecideExperimentAsync(plan.ExperimentId, decision with { Conclusion = "approved_candidate", SelectedVariantId = "not-a-candidate" }), "CANDIDATE_INVALID");
        await store.DecideExperimentAsync(plan.ExperimentId, decision); await store.DecideExperimentAsync(plan.ExperimentId, decision);
        await Reject(() => store.DecideExperimentAsync(plan.ExperimentId, decision with { Reason = "overwrite" }), "EXPERIMENT_STATE");
    }
    private static HttpClient Client(bool authenticated = true)
    {
        var client = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("SIMOPS_API_URL") ?? "http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(30) };
        if (authenticated) client.DefaultRequestHeaders.Add("X-SimOps-Admin-Key", Environment.GetEnvironmentVariable("SIMOPS_ADMIN_KEY") ?? "simops-local-dev-key");
        return client;
    }
    private static async Task<T> Post<T>(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(path, body, ExperimentJson.Options);
        Check(response.StatusCode == expected, $"{path}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(ExperimentJson.Options))!;
    }
    private static async Task HttpSecurity()
    {
        using var unauthorized = Client(false);
        foreach (var path in new[] { "/api/v1/experiments", "/api/v1/experiments/x/results", "/api/v1/catalog/experiment-template" })
            Check((await unauthorized.GetAsync(path)).StatusCode == HttpStatusCode.Unauthorized, "Operator data leaked.");
        using var client = Client();
        using (var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/experiments"))
        {
            preflight.Headers.Add("Origin", "http://127.0.0.1:5173");
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-simops-admin-key");
            using var response = await unauthorized.SendAsync(preflight);
            Check(response.StatusCode == HttpStatusCode.NoContent && response.Headers.GetValues("Access-Control-Allow-Origin").Single() == "http://127.0.0.1:5173", "Dashboard preflight was blocked.");
        }
        var invalid = Fixture() with { RunsPerCell = 1001 };
        Check((await client.PostAsJsonAsync("/api/v1/experiments", new SaveExperimentRequest(invalid), ExperimentJson.Options)).StatusCode == HttpStatusCode.BadRequest, "Oversized job accepted.");
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new SaveExperimentRequest(Fixture()), ExperimentJson.Options))!;
        json["definition"]!["decisionRules"]!.AsObject().Remove("minimumMaeImprovement");
        Check((await client.PostAsync("/api/v1/experiments", new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json"))).StatusCode == HttpStatusCode.BadRequest, "Missing threshold was silently defaulted.");
    }
    private static async Task HttpLifecycle()
    {
        using var client = Client(); var plan = Fixture();
        var saved = await Post<ExperimentDetail>(client, "/api/v1/experiments", new SaveExperimentRequest(plan));
        var changed = plan with { Hypothesis = "Updated before Ready." };
        var edited = await Post<ExperimentDetail>(client, "/api/v1/experiments", new SaveExperimentRequest(changed, saved.Revision));
        Check(edited.Revision == 2 && edited.PlanHash != saved.PlanHash, "Draft revision was not updated.");
        await Post<ExperimentDetail>(client, $"/api/v1/experiments/{plan.ExperimentId}/ready", new ExperimentCommandRequest(edited.PlanHash));
        Check((await client.PostAsJsonAsync("/api/v1/experiments", new SaveExperimentRequest(plan, 2), ExperimentJson.Options)).StatusCode == HttpStatusCode.Conflict, "Ready plan changed.");
        var command = new StartBatchRequest(edited.PlanHash, Guid.NewGuid().ToString("N"));
        var timer = Stopwatch.StartNew();
        var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Post<JsonElement>(client, $"/api/v1/experiments/{plan.ExperimentId}/batches", command, HttpStatusCode.Accepted)));
        Console.WriteLine($"  concurrentStartMs={timer.ElapsedMilliseconds}");
        Check(starts.Select(x => x.GetProperty("batchId").GetGuid()).Distinct().Count() == 1, "Duplicate starts created batches.");
        var detail = await Wait(client, plan.ExperimentId);
        Check(detail.Batch!.CompletedRuns == 360, "Worker did not finish fixture cells.");
        var readers = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => client.GetFromJsonAsync<ExperimentDetail>($"/api/v1/experiments/{plan.ExperimentId}", ExperimentJson.Options)));
        Check(readers.All(x => x?.Batch?.CompletedRuns == 360), "Concurrent polling exhausted the connection pool or lost progress.");
    }
    private static async Task HttpRegistered()
    {
        using var client = Client(); var plan = Registered;
        var saved = await Post<ExperimentDetail>(client, "/api/v1/experiments", new SaveExperimentRequest(plan));
        if (saved.Status == "draft") await Post<ExperimentDetail>(client, $"/api/v1/experiments/{plan.ExperimentId}/ready", new ExperimentCommandRequest(saved.PlanHash));
        if (saved.Status is "draft" or "ready")
            await Post<JsonElement>(client, $"/api/v1/experiments/{plan.ExperimentId}/batches", new StartBatchRequest(saved.PlanHash, "registered-difficulty-curve-001"), HttpStatusCode.Accepted);
        var detail = await Wait(client, plan.ExperimentId);
        Check(detail.Batch!.CompletedRuns == 18000, "Full experiment count mismatch.");
        var report = await client.GetFromJsonAsync<ExperimentReport>($"/api/v1/experiments/{plan.ExperimentId}/results", ExperimentJson.Options);
        Check(report!.ResultDigest == "3bf0513a6d9eb46554b81a17ea8860cb9fbeb1a5be36bccf30d9c7707e9dbb08", "Registered digest changed.");
        Check(report.ReviewCandidateIds.Count == 0 && detail.Decision is null, "Experiment was automatically approved or decided.");
        var checksum = report.Cells.Single(c => c.VariantId == "uniform" && c.AgentId == "novice").ConfigChecksum;
        var config = await client.GetFromJsonAsync<JsonElement>($"/api/v1/catalog/configs/{checksum}");
        Check(config.GetProperty("checksum").GetString() == checksum && config.GetProperty("encounters")[5].GetProperty("attackPower").GetInt32() == 15, "Registered config lookup differs.");
        Console.WriteLine($"  persistedDigest={report.ResultDigest}, runs={detail.Batch.CompletedRuns}");
    }
    private static async Task<ExperimentDetail> Wait(HttpClient client, string id)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(60))
        {
            var detail = (await client.GetFromJsonAsync<ExperimentDetail>($"/api/v1/experiments/{id}", ExperimentJson.Options))!;
            if (detail.Status is "analyzing" or "decided") return detail;
            if (detail.Status == "failed") throw new InvalidOperationException(JsonSerializer.Serialize(detail.Batch));
            await Task.Delay(300);
        }
        throw new TimeoutException("Experiment did not complete in 60 seconds.");
    }
}
