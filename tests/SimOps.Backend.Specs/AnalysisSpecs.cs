using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using SimOps.Application;
using SimOps.Experiments;
using SimOps.Infrastructure;

internal static class AnalysisSpecs
{
    private static ExperimentDefinition Fixture() => ExperimentJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "registered-plan.json"))) with {
        ExperimentId = "analysis-spec-" + Guid.NewGuid().ToString("N"), RunsPerCell = 20, FirstSeed = "0", BootstrapReplicates = 100 };
    private static readonly Lazy<ExperimentReport> LocalReport = new(() => ExperimentRunner.Execute(Fixture()));
    private static MetricSnapshot Snapshot => AnalysisEvidence.CreateSnapshot(LocalReport.Value);
    private static readonly string Connection = Environment.GetEnvironmentVariable("SIMOPS_CONNECTION_STRING") ?? "Host=127.0.0.1;Port=54329;Database=simops;Username=simops;Password=simops-local-only";
    private static string? _isolated;
    private static void Check(bool test, string message) { if (!test) throw new InvalidOperationException(message); }
    private static async Task Invalid(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is AnalysisValidationException or ExperimentCommandException) { return; }
        throw new InvalidOperationException("Expected rejection.");
    }
    private static ProviderAnalysis WithOutput(ProviderAnalysis provider, AnalysisOutput output) => provider with { Json = JsonSerializer.Serialize(output, ExperimentJson.Options) };
    private static AnalysisOutput Output(ProviderAnalysis provider) => JsonSerializer.Deserialize<AnalysisOutput>(provider.Json, ExperimentJson.Options)!;

    public static async Task<int> RunAsync(string mode)
    {
        if (mode == "--analysis-probe")
        {
            using var api = Client();
            var result = JsonSerializer.Deserialize<ExperimentReport>(await api.GetStringAsync("/api/v1/experiments/difficulty-curve-001/results"), ExperimentJson.Options)!;
            var snapshot = AnalysisEvidence.CreateSnapshot(result);
            using var local = new OllamaAnalysisProvider(OllamaAnalysisProvider.CreateLocalClient(), "qwen2.5:3b");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var response = await local.AnalyzeAsync(snapshot, deadline.Token);
            Console.WriteLine(response.Json);
            AnalysisEvidence.Validate(snapshot, response);
            return 0;
        }
        var tests = mode switch {
            "--analysis-db" => new (string, Func<Task>)[] {
                ("AI-DB-001 idempotency and admission are race-safe", Admission),
                ("AI-DB-002 stale leases and duplicate completion are fenced; input/report immutable", Fencing),
                ("AI-DB-003 invalid output preserves metrics and human decision", FailureIsolation),
                ("AI-DB-004 exhausted crash attempts fail without fallback", Exhaustion) },
            "--analysis-http" => new (string, Func<Task>)[] {
                ("AI-HTTP-001 authentication and strict command schema", HttpSecurity),
                ("AI-HTTP-002 asynchronous offline analysis and unchanged experiment", HttpOffline) },
            "--analysis-ollama" => new (string, Func<Task>)[] { ("AI-LOCAL-001 three real local-model reports and conclusion stability", HttpLocal) },
            _ => new (string, Func<Task>)[] {
                ("AI-001 canonical snapshot survives JSON ordering and excludes raw hypothesis", Canonical),
                ("AI-002 fabricated numbers, keys and unobserved metrics are rejected", Fabrication),
                ("AI-003 extra fields, null schema and invented conclusions are rejected", Schema),
                ("AI-004 interpretation codes require compatible evidence", Interpretation),
                ("AI-005 deterministic offline provider is explicitly labeled", Repetition),
                ("AI-006 local adapter sends bounded evidence and validates provider identity", Adapter),
                ("AI-007 provider cancellation bounds execution", Timeout),
                ("AI-008 cloud/missing/remote model is never called", LocalOnly),
                ("AI-009 model numeric prose and invented keys cannot cross adapter", ModelFabrication) }
        };
        NpgsqlConnection? admin = null; string? database = null;
        if (mode == "--analysis-db")
        {
            var settings = new NpgsqlConnectionStringBuilder(Connection);
            if (settings.Host != "127.0.0.1" || settings.Port != 54329) throw new InvalidOperationException("Only isolated local fixture databases are allowed.");
            database = "simops_analysis_spec_" + Guid.NewGuid().ToString("N");
            admin = new(settings.ConnectionString); await admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
            settings.Database = database; settings.Pooling = false; _isolated = settings.ConnectionString;
        }
        var failures = 0;
        try
        {
            foreach (var (name, run) in tests)
                try { await run(); Console.WriteLine("PASS " + name); }
                catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
        }
        finally
        {
            if (admin is not null)
            {
                // Remove only the UUID-named database created above on the fixed project-local server.
                await using (var drop = new NpgsqlCommand($"DROP DATABASE {database}", admin)) await drop.ExecuteNonQueryAsync();
                await admin.DisposeAsync(); _isolated = null;
            }
        }
        Console.WriteLine($"Analysis specs: {tests.Length - failures} passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }
    private static Task Canonical()
    {
        var snapshot = Snapshot;
        var reordered = snapshot with { Metrics = snapshot.Metrics.Reverse().ToArray(), Guards = snapshot.Guards.Reverse().ToArray() };
        Check(AnalysisEvidence.SnapshotHash(snapshot) == AnalysisEvidence.SnapshotHash(reordered), "Ordering changed snapshot.");
        var text = JsonSerializer.Serialize(snapshot, ExperimentJson.Options);
        Check(!text.Contains(LocalReport.Value.Definition.Hypothesis, StringComparison.Ordinal) && !text.Contains("actions\""), "Raw instructions/actions leaked.");
        Check(snapshot.Metrics.Select(m => m.Key).Distinct().Count() == snapshot.Metrics.Count, "Duplicate metrics.");
        return Task.CompletedTask;
    }
    private static async Task Fabrication()
    {
        var p = await new OfflineAnalysisProvider().AnalyzeAsync(Snapshot, default); var o = Output(p);
        foreach (var observation in new[] { o.Observations[0] with { Value = 999.999 }, new MetricObservation("invented.retention", 1) })
            await Invalid(() => Task.FromResult(AnalysisEvidence.Validate(Snapshot, WithOutput(p, o with { Observations = [observation] }))));
        var missing = Snapshot with { Metrics = Snapshot.Metrics.Select(m => m.Key == o.Observations[0].MetricKey ? m with { Value = null } : m).ToArray() };
        await Invalid(() => Task.FromResult(AnalysisEvidence.Validate(missing, WithOutput(p, o with { Observations = [o.Observations[0]] }))));
    }
    private static async Task Schema()
    {
        var p = await new OfflineAnalysisProvider().AnalyzeAsync(Snapshot, default); var o = Output(p);
        var node = JsonNode.Parse(p.Json)!; node["prose"] = "The model invented one hundred players.";
        foreach (var json in new[] { node.ToJsonString(), "null", "{}", p.Json.Replace("\"observations\":[", "\"extra\":1,\"observations\":[", StringComparison.Ordinal) })
            await Invalid(() => Task.FromResult(AnalysisEvidence.Validate(Snapshot, p with { Json = json })));
        await Invalid(() => Task.FromResult(AnalysisEvidence.Validate(Snapshot, WithOutput(p, o with { Assessment = "publish_now" }))));
        await Invalid(() => Task.FromResult(AnalysisEvidence.Validate(Snapshot, WithOutput(p, o with { Hypotheses = null! }))));
    }
    private static async Task Interpretation()
    {
        var p = await new OfflineAnalysisProvider().AnalyzeAsync(Snapshot, default); var o = Output(p);
        foreach (var item in new[] { new AnalysisInterpretation("proven_human_fun", ["experiment.completed_runs"]),
            new AnalysisInterpretation("policy_sensitivity", ["experiment.completed_runs"]), new AnalysisInterpretation("policy_sensitivity", []) })
            await Invalid(() => Task.FromResult(AnalysisEvidence.Validate(Snapshot, WithOutput(p, o with { Hypotheses = [item] }))));
    }
    private static async Task Repetition()
    {
        var reports = new List<AnalysisReport>();
        for (var i = 0; i < 3; i++) reports.Add(AnalysisEvidence.Validate(Snapshot, await new OfflineAnalysisProvider().AnalyzeAsync(Snapshot, default)));
        Check(reports.All(r => r.Provider == "offline" && r.Model == "rule-based-demo-not-llm") && reports.Select(r => r.OutputHash).Distinct().Count() == 1, "Offline provenance/stability failed.");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: ExperimentJson.Options) };
    private static object Tags(string name = "fixture:local") => new { models = new[] { new { name, digest = "test-digest" } } };
    private static async Task Adapter()
    {
        var expected = await new OfflineAnalysisProvider().AnalyzeAsync(Snapshot, default);
        var selection = JsonNode.Parse(expected.Json)!;
        foreach (var observation in selection["observations"]!.AsArray()) observation!.AsObject().Remove("value");
        var chats = 0;
        using var client = new HttpClient(new Handler(async (request, token) => {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Json(Tags());
            if (request.RequestUri.AbsolutePath == "/api/show") return Json(new { details = new { format = "gguf" }, model_info = new Dictionary<string, string> { ["general.architecture"] = "fixture" } });
            Check(request.RequestUri.AbsolutePath == "/api/chat", "Unexpected provider capability."); chats++;
            var body = await request.Content!.ReadAsStringAsync(token);
            Check(!body.Contains(LocalReport.Value.Definition.Hypothesis, StringComparison.Ordinal) && !body.Contains("simops-local-dev-key") && !body.Contains("connectionString"), "Unsafe model input.");
            return Json(new { model = "fixture:local", done = true, done_reason = "stop", message = new { content = selection.ToJsonString() } });
        })) { BaseAddress = new Uri("http://127.0.0.1:11434") };
        var report = AnalysisEvidence.Validate(Snapshot, await new OllamaAnalysisProvider(client, "fixture:local").AnalyzeAsync(Snapshot, default));
        Check(chats == 1 && report.ModelDigest == "test-digest" && report.Provider == "ollama", "Adapter provenance missing.");
    }
    private static async Task Timeout()
    {
        using var client = new HttpClient(new Handler(async (_, token) => { await Task.Delay(System.Threading.Timeout.Infinite, token); return Json(Tags()); })) { BaseAddress = new Uri("http://127.0.0.1:11434") };
        using var cancellation = new CancellationTokenSource(50);
        var timer = Stopwatch.StartNew();
        try { await new OllamaAnalysisProvider(client, "fixture:local").AnalyzeAsync(Snapshot, cancellation.Token); throw new InvalidOperationException("Provider did not time out."); }
        catch (OperationCanceledException) { Check(timer.Elapsed < TimeSpan.FromSeconds(3), "Cancellation did not bound provider execution."); }
    }
    private static async Task ModelFabrication()
    {
        var offline = await new OfflineAnalysisProvider().AnalyzeAsync(Snapshot, default);
        var selection = JsonNode.Parse(offline.Json)!;
        foreach (var observation in selection["observations"]!.AsArray()) observation!.AsObject().Remove("value");
        var invalidKey = JsonNode.Parse(selection.ToJsonString())!;
        invalidKey["observations"]![0]!["metricKey"] = "invented/human_retention";
        foreach (var json in new[] { offline.Json, invalidKey.ToJsonString() })
        {
            using var client = new HttpClient(new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch {
                "/api/tags" => Json(Tags()),
                "/api/show" => Json(new { details = new { format = "gguf" }, model_info = new Dictionary<string, string> { ["general.architecture"] = "fixture" } }),
                _ => Json(new { model = "fixture:local", done = true, done_reason = "stop", message = new { content = json } })
            }))) { BaseAddress = new Uri("http://127.0.0.1:11434") };
            await Invalid(() => new OllamaAnalysisProvider(client, "fixture:local").AnalyzeAsync(Snapshot, default));
        }
    }
    private static async Task LocalOnly()
    {
        var chats = 0;
        using var client = new HttpClient(new Handler((request, _) => {
            if (request.RequestUri!.AbsolutePath == "/api/chat") chats++;
            return Task.FromResult(Json(new { models = new[] { new { name = "remote:local", digest = "x", remote_host = "https://example.invalid" } } }));
        })) { BaseAddress = new Uri("http://127.0.0.1:11434") };
        foreach (var model in new[] { "missing:local", "example:cloud", "remote:local" })
            await Invalid(() => new OllamaAnalysisProvider(client, model).AnalyzeAsync(Snapshot, default));
        Check(chats == 0, "Cloud/missing model was called.");
    }

    private static PostgresRunStore Store() => new(_isolated ?? throw new InvalidOperationException("Isolated DB required."));
    private static async Task<ExperimentReport> Completed(PostgresRunStore store)
    {
        await store.InitializeAsync(); var plan = Fixture();
        var saved = await store.SaveExperimentAsync(new(plan)); await store.MarkExperimentReadyAsync(plan.ExperimentId, saved.PlanHash);
        await store.StartBatchAsync(plan.ExperimentId, new(saved.PlanHash, Guid.NewGuid().ToString("N")));
        while (await store.ClaimSimulationJobAsync() is { } job)
        {
            if (job.Kind == "cell") await store.CompleteSimulationCellAsync(job, ExperimentRunner.ExecuteCell(plan, job.VariantId!, job.AgentId!));
            else
            {
                var report = ExperimentRunner.Execute(plan);
                await store.CompleteSimulationReportAsync(job, report); return report;
            }
        }
        throw new InvalidOperationException("Fixture batch did not aggregate.");
    }
    private static StartAnalysisRequest Request(ExperimentReport report) => new(report.PlanHash, report.ResultDigest, Guid.NewGuid().ToString("N"));
    private static async Task Sql(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_isolated); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("id", id); await command.ExecuteNonQueryAsync();
    }
    private static async Task Admission()
    {
        await using var store = Store(); var report = await Completed(store); var request = Request(report);
        var ids = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.StartAnalysisAsync(report.ExperimentId, request)));
        Check(ids.Distinct().Count() == 1, "Duplicate requests.");
        await Invalid(() => store.StartAnalysisAsync(report.ExperimentId, request with { ResultDigest = "changed" }));
        await store.StartAnalysisAsync(report.ExperimentId, Request(report));
        await Invalid(() => store.StartAnalysisAsync(report.ExperimentId, Request(report)));
        while (await store.ClaimAnalysisAsync() is { } job) await store.FailAnalysisAsync(job, "FIXTURE_CLEANUP", false);
    }
    private static async Task Fencing()
    {
        await using var store = Store(); var report = await Completed(store);
        var id = await store.StartAnalysisAsync(report.ExperimentId, Request(report));
        var stale = (await store.ClaimAnalysisAsync())!;
        await Sql("UPDATE simops.analysis_jobs SET lease_until=now()-interval '1 second' WHERE id=@id", id);
        Check(!await store.HeartbeatAnalysisAsync(stale), "Expired heartbeat resurrected lease.");
        var fresh = (await store.ClaimAnalysisAsync())!;
        try {
            await Sql("UPDATE simops.analysis_jobs SET status='succeeded',report='{}',lock_token=NULL,lease_until=NULL WHERE id=@id", id);
            throw new InvalidOperationException("Report without a snapshot hash passed the DB guard.");
        } catch (PostgresException ex) when (ex.SqlState == "23514") { }
        var output = await new OfflineAnalysisProvider().AnalyzeAsync(fresh.Snapshot, default);
        Check(!await store.CompleteAnalysisAsync(stale, output), "Stale completion saved.");
        Check(await store.CompleteAnalysisAsync(fresh, output), "Fresh completion failed.");
        Check(!await store.CompleteAnalysisAsync(fresh, output), "Duplicate completion saved.");
        foreach (var sql in new[] { "UPDATE simops.analysis_jobs SET report='{}' WHERE id=@id", "UPDATE simops.analysis_jobs SET snapshot='{}' WHERE id=@id", "DELETE FROM simops.analysis_jobs WHERE id=@id" })
        {
            try { await Sql(sql, id); throw new InvalidOperationException("Immutable evidence changed."); }
            catch (PostgresException ex) when (ex.SqlState == "23514") { }
        }
    }
    private static async Task FailureIsolation()
    {
        await using var store = Store(); var report = await Completed(store);
        await store.DecideExperimentAsync(report.ExperimentId, new(report.PlanHash, report.ResultDigest, "rejected", null, "Isolated test decision."));
        var before = JsonSerializer.Serialize(await store.GetExperimentAsync(report.ExperimentId), ExperimentJson.Options);
        var metrics = await store.GetExperimentResultJsonAsync(report.ExperimentId, false);
        await store.StartAnalysisAsync(report.ExperimentId, Request(report)); var job = (await store.ClaimAnalysisAsync())!;
        await Invalid(() => store.CompleteAnalysisAsync(job, new("fixture", "invalid", "x", "{}")));
        await store.FailAnalysisAsync(job, "ANALYSIS_SCHEMA_INVALID", false);
        Check((await store.GetAnalysesAsync(report.ExperimentId)).Single().Report is null, "Invalid result saved.");
        Check(before == JsonSerializer.Serialize(await store.GetExperimentAsync(report.ExperimentId), ExperimentJson.Options) && metrics == await store.GetExperimentResultJsonAsync(report.ExperimentId, false), "Analysis failure mutated experiment.");
    }
    private static async Task Exhaustion()
    {
        await using var store = Store(); var report = await Completed(store);
        var id = await store.StartAnalysisAsync(report.ExperimentId, Request(report));
        for (var i = 0; i < 3; i++) { Check(await store.ClaimAnalysisAsync() is not null, "Missing retry."); await Sql("UPDATE simops.analysis_jobs SET lease_until=now()-interval '1 second' WHERE id=@id", id); }
        Check(await store.ClaimAnalysisAsync() is null, "Exhausted job retried.");
        var job = (await store.GetAnalysesAsync(report.ExperimentId)).Single();
        Check(job.Status == "failed" && job.Attempts == 3 && job.Report is null, "Exhaustion invented fallback result.");
    }

    private static HttpClient Client(bool authenticated = true)
    {
        var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(10) };
        if (authenticated) client.DefaultRequestHeaders.Add("X-SimOps-Admin-Key", Environment.GetEnvironmentVariable("SIMOPS_ADMIN_KEY") ?? "simops-local-dev-key");
        return client;
    }
    private static async Task HttpSecurity()
    {
        const string path = "/api/v1/experiments/difficulty-curve-001/analyses";
        using var anonymous = Client(false); using var client = Client();
        Check((await anonymous.GetAsync(path)).StatusCode == HttpStatusCode.Unauthorized && (await anonymous.PostAsJsonAsync(path, new { })).StatusCode == HttpStatusCode.Unauthorized, "Analysis is public.");
        Check((await client.PostAsJsonAsync(path, new { planHash = "x", resultDigest = "y", idempotencyKey = "z", model = "cloud" })).StatusCode == HttpStatusCode.BadRequest, "Client can override provider.");
        Check((await client.PostAsJsonAsync(path, new { })).StatusCode == HttpStatusCode.BadRequest, "Incomplete command accepted.");
    }
    private static async Task HttpOffline()
    {
        using var client = Client(); var plan = Fixture();
        var response = await client.PostAsJsonAsync("/api/v1/experiments", new SaveExperimentRequest(plan), ExperimentJson.Options); response.EnsureSuccessStatusCode();
        var detail = (await response.Content.ReadFromJsonAsync<ExperimentDetail>(ExperimentJson.Options))!;
        var path = $"/api/v1/experiments/{plan.ExperimentId}";
        (await client.PostAsJsonAsync(path + "/ready", new ExperimentCommandRequest(detail.PlanHash))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(path + "/batches", new StartBatchRequest(detail.PlanHash, Guid.NewGuid().ToString("N")))).EnsureSuccessStatusCode();
        var timer = Stopwatch.StartNew();
        do {
            detail = (await client.GetFromJsonAsync<ExperimentDetail>(path, ExperimentJson.Options))!;
            if (detail.Batch?.Status == "completed") break;
            await Task.Delay(200);
        } while (timer.Elapsed < TimeSpan.FromSeconds(30));
        Check(detail.Batch?.Status == "completed", "Fixture simulation did not complete.");
        await AnalyzeOverHttp(client, plan.ExperimentId, "offline", 2);
    }
    private static async Task HttpLocal()
    {
        using var client = Client(); await AnalyzeOverHttp(client, "difficulty-curve-001", "ollama", 3);
    }
    private static async Task AnalyzeOverHttp(HttpClient client, string id, string provider, int repetitions)
    {
        var path = $"/api/v1/experiments/{id}";
        var before = await client.GetStringAsync(path);
        var metrics = await client.GetStringAsync(path + "/results");
        var result = JsonSerializer.Deserialize<ExperimentReport>(metrics, ExperimentJson.Options)!;
        var reports = new List<AnalysisReport>();
        for (var i = 0; i < repetitions; i++)
        {
            var request = Request(result);
            using var response = await client.PostAsJsonAsync(path + "/analyses", request, ExperimentJson.Options);
            Check(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            var receipt = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
            using var duplicate = await client.PostAsJsonAsync(path + "/analyses", request, ExperimentJson.Options);
            Check((await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid() == receipt, "HTTP idempotency failed.");
            var timer = Stopwatch.StartNew(); AnalysisJob? job;
            do {
                job = (await client.GetFromJsonAsync<AnalysisJob[]>(path + "/analyses", ExperimentJson.Options))!.Single(x => x.Id == receipt);
                if (job.Status is "succeeded" or "failed") break;
                await Task.Delay(500);
            } while (timer.Elapsed < TimeSpan.FromMinutes(7));
            Check(job.Report is not null && job.Status == "succeeded", $"Analysis {receipt} failed: {job.Status}/{job.LastError}");
            var report = job.Report!; Check(report.Provider == provider, "Unexpected provider / silent fallback.");
            var validated = AnalysisEvidence.Validate(job.Snapshot, new(report.Provider, report.Model, report.ModelDigest, JsonSerializer.Serialize(report.Output, ExperimentJson.Options)));
            Check(validated.OutputHash == report.OutputHash && validated.SnapshotHash == job.SnapshotHash, "Stored report does not validate.");
            reports.Add(report);
            Console.WriteLine($"  job={receipt} provider={report.Provider} model={report.Model} digest={report.ModelDigest} elapsedMs={timer.ElapsedMilliseconds} snapshot={report.SnapshotHash} conclusion={report.ConclusionHash}");
        }
        Check(before == await client.GetStringAsync(path) && metrics == await client.GetStringAsync(path + "/results"), "Analysis modified experiment or decision.");
        Console.WriteLine($"  reports={reports.Count}; distinctConclusions={reports.Select(r => r.ConclusionHash).Distinct().Count()}; assessmentAgreement={reports.Count(r => r.Output.Assessment == reports[0].Output.Assessment)}/{reports.Count}");
    }
}
