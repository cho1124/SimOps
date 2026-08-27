using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using SimOps.Agent.Core;
using SimOps.Application;
using SimOps.Experiments;
using SimOps.Game.Core;
using SimOps.Game.Transport;
using SimOps.Infrastructure;

internal static class LiveOpsSpecs
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task<T> Post<T>(HttpClient api, string path, object value, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await api.PostAsJsonAsync(path, value, ContractJson.Options);
        Check(response.StatusCode == status, $"{path}: {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(ContractJson.Options))!;
    }
    private static async Task Reject(HttpClient api, string path, object request, HttpStatusCode expected)
    { using var response = await api.PostAsJsonAsync(path, request, ContractJson.Options); Check(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); }
    public static async Task<int> RunAsync(bool unity)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SIMOPS_CONNECTION_STRING") ?? "Host=127.0.0.1;Port=54329;Database=simops;Username=simops;Password=simops-local-only");
        if (settings.Host != "127.0.0.1" || settings.Port != 54329) throw new InvalidOperationException("Isolated local database required.");
        var database = "simops_liveops_spec_" + Guid.NewGuid().ToString("N");
        var root = Directory.GetCurrentDirectory();
        var children = new List<Process>(); var outputs = new List<Task<string>>();
        await using var admin = new NpgsqlConnection(settings.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        settings.Database = database; settings.Pooling = false;
        var passed = 0; var failed = false;
        try
        {
            using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 5081)) { probe.Start(); probe.Stop(); }
            foreach (var project in new[] { "SimOps.Api", "SimOps.Worker" })
            {
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(Path.Combine(root, "src", project, "bin", "Release", "net10.0", project + ".dll"));
                if (project == "SimOps.Api") { start.ArgumentList.Add("--urls"); start.ArgumentList.Add("http://127.0.0.1:5081"); }
                start.Environment["SIMOPS_CONNECTION_STRING"] = settings.ConnectionString;
                start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; start.Environment["DOTNET_ENVIRONMENT"] = "Development";
                start.Environment["SIMOPS_ADMIN_KEY"] = "liveops-test-admin"; start.Environment["SIMOPS_APPROVER_KEY"] = "liveops-test-approver";
                start.Environment["SIMOPS_TICKET_SIGNING_KEY"] = "liveops-fixture-signing-key-only-not-production";
                start.Environment["SIMOPS_ANALYSIS_PROVIDER"] = "offline";
                var process = Process.Start(start)!; children.Add(process);
                outputs.Add(process.StandardOutput.ReadToEndAsync()); outputs.Add(process.StandardError.ReadToEndAsync());
            }
            using var api = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5081"), Timeout = TimeSpan.FromSeconds(20) };
            var ready = false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                try { ready = (await api.GetAsync("/health/ready")).IsSuccessStatusCode; } catch (HttpRequestException) { }
                if (ready) break; await Task.Delay(100);
            }
            Check(ready, "Isolated API did not start.");
            var baseline = (await api.GetFromJsonAsync<SeasonInfo>("/api/v1/public/seasons/active", ContractJson.Options))!;
            await Reject(api, "/api/v1/liveops/publish", new { }, HttpStatusCode.Unauthorized);
            api.DefaultRequestHeaders.Add("X-SimOps-Admin-Key", "liveops-test-admin");
            await Reject(api, "/api/v1/liveops/publish", new { }, HttpStatusCode.Forbidden);
            api.DefaultRequestHeaders.Add("X-SimOps-Approver-Key", "liveops-test-approver");
            await Reject(api, "/api/v1/liveops/publish", new { }, HttpStatusCode.BadRequest);
            var unauthorizedPlan = new PublishConfigRequest("absent", "x", "y", "uniform", baseline.SeasonId, "test", "test", Guid.NewGuid().ToString("N"));
            await Reject(api, "/api/v1/liveops/publish", unauthorizedPlan, HttpStatusCode.Conflict);
            await Reject(api, "/api/v1/liveops/publish", unauthorizedPlan with { Name = null! }, HttpStatusCode.BadRequest);
            await Reject(api, "/api/v1/liveops/publish", unauthorizedPlan with { ExperimentId = null! }, HttpStatusCode.BadRequest);
            Console.WriteLine("PASS LIVE-001 admin/approver/strict schema and unapproved publication rejection"); passed++;

            // Deliberately permissive TEST criteria, isolated DB only. Never changes the real preregistered experiment.
            var plan = ExperimentJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "registered-plan.json"))) with {
                ExperimentId = "liveops-fixture", Hypothesis = "TEST ONLY: exercise positive publication, not a balance claim.", RunsPerCell = 200, BootstrapReplicates = 100,
                DecisionRules = new(.001, 0, 1, 0, 1, 1, 0, 0, 0, 3, 0, 1, 1) };
            var experiment = await Post<ExperimentDetail>(api, "/api/v1/experiments", new SaveExperimentRequest(plan));
            var path = "/api/v1/experiments/" + plan.ExperimentId;
            await Post<ExperimentDetail>(api, path + "/ready", new ExperimentCommandRequest(experiment.PlanHash));
            await Post<JsonElement>(api, path + "/batches", new StartBatchRequest(experiment.PlanHash, "fixture-batch"), HttpStatusCode.Accepted);
            for (var i = 0; i < 300; i++)
            {
                experiment = (await api.GetFromJsonAsync<ExperimentDetail>(path, ExperimentJson.Options))!;
                if (experiment.Batch?.Status == "completed") break; await Task.Delay(100);
            }
            Check(experiment.Batch?.Status == "completed", "Fixture did not complete.");
            var report = (await api.GetFromJsonAsync<ExperimentReport>(path + "/results", ExperimentJson.Options))!;
            Check(report.ReviewCandidateIds.Contains("uniform"), "Fixture must have a computed eligible candidate.");
            var publish = new PublishConfigRequest(plan.ExperimentId, report.PlanHash, report.ResultDigest, "uniform", baseline.SeasonId, "Isolated treatment", "Automated fixture approval", "publish-fixture");
            await Reject(api, "/api/v1/liveops/publish", publish, HttpStatusCode.Conflict);
            var decision = new ExperimentDecisionRequest(report.PlanHash, report.ResultDigest, "approved_candidate", "uniform", "Isolated automated fixture decision.");
            api.DefaultRequestHeaders.Remove("X-SimOps-Approver-Key");
            await Reject(api, path + "/decision", decision, HttpStatusCode.Forbidden);
            api.DefaultRequestHeaders.Add("X-SimOps-Approver-Key", "liveops-test-approver");
            await Post<ExperimentDetail>(api, path + "/decision", decision);
            // Fail the last insert inside the transaction, not an early input check.
            await using (var faultDb = new NpgsqlConnection(settings.ConnectionString))
            {
                await faultDb.OpenAsync();
                await using (var inject = new NpgsqlCommand("""
                    CREATE FUNCTION simops.liveops_fixture_fault() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN RAISE EXCEPTION 'Isolated publication failure fixture' USING ERRCODE='23514'; END $$;
                    CREATE TRIGGER liveops_fixture_fault BEFORE INSERT ON simops.config_publications
                    FOR EACH ROW EXECUTE FUNCTION simops.liveops_fixture_fault();
                    """, faultDb)) await inject.ExecuteNonQueryAsync();
                await Reject(api, "/api/v1/liveops/publish", publish with { IdempotencyKey = "failed-publication" }, HttpStatusCode.InternalServerError);
                Check((await api.GetFromJsonAsync<SeasonInfo>("/api/v1/public/seasons/active", ContractJson.Options))!.SeasonId == baseline.SeasonId, "Failed publication closed the active season.");
                Check((await api.GetFromJsonAsync<Publication[]>("/api/v1/liveops/publications", ContractJson.Options))!.Length == 0, "Failed publication left an audit row.");
                await using (var count = new NpgsqlCommand("SELECT count(*) FROM simops.seasons", faultDb))
                    Check((long)(await count.ExecuteScalarAsync())! == 1, "Failed publication left a partial season.");
                await using var remove = new NpgsqlCommand("DROP TRIGGER liveops_fixture_fault ON simops.config_publications; DROP FUNCTION simops.liveops_fixture_fault()", faultDb);
                await remove.ExecuteNonQueryAsync();
            }
            Console.WriteLine("PASS LIVE-007 injected final-insert failure rolls back season closure, creation and audit together"); passed++;
            var player = await Post<PlayerCredential>(api, "/api/v1/player/register", new RegisterPlayerRequest("LiveOpsFixture"));
            api.DefaultRequestHeaders.Authorization = new("Bearer", player.Credential);
            var oldTicket = await Post<BeginRunResponse>(api, "/api/v1/player/tickets", new BeginRunRequest(baseline.SeasonId, CoreArtifact.Checksum, "old-ticket"));
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Post<Publication>(api, "/api/v1/liveops/publish", publish)));
            var publication = concurrent[0];
            Check(concurrent.All(x => x.Id == publication.Id), "Duplicate publication created multiple seasons.");
            Check(publication.ConfigChecksum != baseline.ConfigChecksum, "Treatment was not published.");
            await Reject(api, "/api/v1/liveops/publish", publish with { IdempotencyKey = "stale-request" }, HttpStatusCode.Conflict);
            await Reject(api, "/api/v1/liveops/publish", publish with { Reason = "changed" }, HttpStatusCode.Conflict);
            Console.WriteLine("PASS LIVE-002 eligible human decision, atomic publication, concurrent idempotency and stale-season fencing"); passed++;

            var snapshot = (await api.GetFromJsonAsync<PublishedConfig>($"/api/v1/public/seasons/{publication.SeasonId}/config", ContractJson.Options))!;
            var config = snapshot.ToConfig();
            Check(config.Checksum == plan.CreateConfig(plan.Variants.Single(v => v.Id == "uniform")).Checksum, "Published snapshot drifted.");
            snapshot.attackPowers[1]++; try { snapshot.ToConfig(); throw new InvalidOperationException("Tampered snapshot accepted."); } catch (ArgumentException) { } snapshot.attackPowers[1]--;
            var ticket = await Post<BeginRunResponse>(api, "/api/v1/player/tickets", new BeginRunRequest(publication.SeasonId, CoreArtifact.Checksum, "treatment-ticket"));
            var actions = Simulate(config, ticket);
            var receipt = await Post<SubmissionReceipt>(api, "/api/v1/player/runs", actions, HttpStatusCode.Accepted);
            RunStatusResponse? status = null;
            for (var i = 0; i < 100; i++) { status = await api.GetFromJsonAsync<RunStatusResponse>($"/api/v1/player/runs/{receipt.RunId}", ContractJson.Options); if (status?.Status == "verified") break; await Task.Delay(100); }
            Check(status?.Status == "verified", "Worker did not replay published treatment.");
            var ranking = (await api.GetFromJsonAsync<LeaderboardResponse>($"/api/v1/public/seasons/{publication.SeasonId}/leaderboard", ContractJson.Options))!;
            Check(ranking.CurrentPlayer?.RunId == ticket.RunId, "Verified treatment did not enter its ranking.");
            await Reject(api, "/api/v1/player/runs", Simulate(GameConfig.CreateBaseline(), oldTicket), HttpStatusCode.BadRequest);
            Console.WriteLine("PASS LIVE-003 config checksum, signed treatment run, Worker replay and season-isolated ranking"); passed++;

            var runnerStart = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { Path.Combine(root, "src", "SimOps.Runner", "bin", "Release", "net10.0", "SimOps.Runner.dll"), "42", "--season", publication.SeasonId.ToString(), "--api-url", "http://127.0.0.1:5081" }) runnerStart.ArgumentList.Add(arg);
            var runner = Process.Start(runnerStart)!; children.Add(runner);
            var runnerOutput = runner.StandardOutput.ReadToEndAsync(); var runnerError = runner.StandardError.ReadToEndAsync();
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30))) await runner.WaitForExitAsync(deadline.Token);
            Check(runner.ExitCode == 0 && (await runnerOutput).Contains("configChecksum=" + config.Checksum), "Runner did not load the published config: " + await runnerError);
            children.Remove(runner); runner.Dispose();
            Console.WriteLine("PASS LIVE-RUNNER-001 actual console Runner uses the published treatment snapshot"); passed++;

            if (unity)
            {
                var start = new ProcessStartInfo(Path.Combine(root, "artifacts", "unity", "windows", "SimOps.exe")) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "-batchmode", "-force-d3d11", "--simops-online-smoke", "--simops-api-url", "http://127.0.0.1:5081", "-logFile", Path.Combine(root, "artifacts", "unity", "logs", "liveops-online-smoke.log") }) start.ArgumentList.Add(arg);
                var process = Process.Start(start)!; children.Add(process);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60)); await process.WaitForExitAsync(deadline.Token);
                Check(process.ExitCode == 0, "Unity treatment smoke failed.");
                children.Remove(process); process.Dispose();
                Console.WriteLine("PASS LIVE-UNITY-001 actual Windows Player fetches and verifies published treatment"); passed++;
            }
            var followup = (await api.GetFromJsonAsync<ExperimentDefinition>($"/api/v1/catalog/experiment-template?controlSeasonId={publication.SeasonId}", ExperimentJson.Options))!
                with { ExperimentId = "liveops-followup", RunsPerCell = 20, FirstSeed = "20000", BootstrapReplicates = 100 };
            Check(followup.SchemaVersion == 2 && followup.ControlConfigChecksum == config.Checksum, "Follow-up did not pin published control.");
            var followupPath = "/api/v1/experiments/" + followup.ExperimentId;
            var savedFollowup = await Post<ExperimentDetail>(api, "/api/v1/experiments", new SaveExperimentRequest(followup));
            await Post<ExperimentDetail>(api, followupPath + "/ready", new ExperimentCommandRequest(savedFollowup.PlanHash));
            await Post<JsonElement>(api, followupPath + "/batches", new StartBatchRequest(savedFollowup.PlanHash, "followup-batch"), HttpStatusCode.Accepted);
            for (var i = 0; i < 300; i++) {
                var state = await api.GetFromJsonAsync<ExperimentDetail>(followupPath, ExperimentJson.Options);
                if (state?.Batch?.Status == "completed") break; await Task.Delay(100);
            }
            var followupReport = (await api.GetFromJsonAsync<ExperimentReport>(followupPath + "/results", ExperimentJson.Options))!;
            Check(followupReport.Cells.Where(x => x.VariantId == "control").All(x => x.ConfigChecksum == config.Checksum), "Follow-up reverted to baseline.");
            Check((await api.GetFromJsonAsync<ExperimentReport>(path + "/results", ExperimentJson.Options))!.ResultDigest == report.ResultDigest, "Follow-up overwrote original evidence.");
            Console.WriteLine("PASS LIVE-006 published config is the immutable control of a newly registered experiment"); passed++;
            var rollback = new RollbackConfigRequest(baseline.SeasonId, publication.SeasonId, "Isolated rollback", "Fixture rollback", "rollback-fixture");
            await Reject(api, "/api/v1/liveops/rollback", rollback with { TargetSeasonId = Guid.NewGuid() }, HttpStatusCode.Conflict);
            var reverted = await Post<Publication>(api, "/api/v1/liveops/rollback", rollback);
            Check(reverted.SeasonId != baseline.SeasonId && reverted.ConfigChecksum == baseline.ConfigChecksum, "Rollback reused an old season or wrong config.");
            Check((await Post<Publication>(api, "/api/v1/liveops/rollback", rollback)).Id == reverted.Id, "Rollback is not idempotent.");
            var historical = (await api.GetFromJsonAsync<LeaderboardResponse>($"/api/v1/public/seasons/{publication.SeasonId}/leaderboard", ContractJson.Options))!;
            Check(historical.Status == "closed" && historical.CurrentPlayer?.RunId == ticket.RunId, "Old leaderboard was lost.");
            Check((await api.GetFromJsonAsync<PublishedConfig>($"/api/v1/public/seasons/{publication.SeasonId}/config", ContractJson.Options))!.ToConfig().Checksum == config.Checksum, "Old replay config changed.");
            await using (var store = new PostgresRunStore(settings.ConnectionString)) { await store.InitializeAsync(); Check((await store.GetActiveSeasonAsync())!.SeasonId == reverted.SeasonId, "Restart replaced active season."); }
            Console.WriteLine("PASS LIVE-004 rollback creates a new season; historical config, rankings and restart state preserved"); passed++;
            await using (var db = new NpgsqlConnection(settings.ConnectionString))
            {
                await db.OpenAsync();
                foreach (var sql in new[] { "UPDATE simops.config_publications SET reason='changed'", "DELETE FROM simops.config_publications", $"UPDATE simops.seasons SET status='active' WHERE id='{publication.SeasonId}'" })
                {
                    try { await using var command = new NpgsqlCommand(sql, db); await command.ExecuteNonQueryAsync(); throw new InvalidOperationException("History mutation succeeded."); }
                    catch (PostgresException error) when (error.SqlState == "23514") { }
                }
            }
            Console.WriteLine("PASS LIVE-005 publication audit and closed season are immutable"); passed++;
            Console.WriteLine($"LiveOps specs: {passed} passed, isolated database only."); return 0;
        }
        catch (Exception ex) { failed = true; Console.Error.WriteLine(ex); return 1; }
        finally
        {
            foreach (var process in children) { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); process.Dispose(); }
            foreach (var output in outputs) { var text = await output; if (failed) Console.Error.WriteLine(text[^Math.Min(3000, text.Length)..]); }
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database}", admin); await drop.ExecuteNonQueryAsync();
        }
    }
    private static HumanRunSubmission Simulate(GameConfig config, BeginRunResponse ticket)
    {
        var agent = AgentFactory.CreateDefinitions().Single(x => x.Id == "greedy");
        var run = SyntheticSimulation.Execute(config, ScoreRule.CreateBaseline(), agent, ulong.Parse(ticket.Context.BaseSeed));
        return new(ticket.RunTicket, ticket.RunId.ToString("N"), CoreArtifact.Checksum, 1, run.Result.ResultHash,
            run.Actions.Select(x => new SubmittedAction(x.Sequence, x.ActionType, x.RewardId)).ToArray());
    }
}
