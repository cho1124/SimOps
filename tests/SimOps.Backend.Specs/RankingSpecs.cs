using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using SimOps.Agent.Core;
using SimOps.Application;
using SimOps.Game.Core;
using SimOps.Infrastructure;

internal static class RankingSpecs
{
    private static readonly string ConnectionString = Environment.GetEnvironmentVariable("SIMOPS_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=54329;Database=simops;Username=simops;Password=simops-local-only";
    private static readonly RunTicketSigner Signer = new(Environment.GetEnvironmentVariable("SIMOPS_TICKET_SIGNING_KEY")
        ?? "simops-local-ticket-signing-key-not-for-production");

    public static async Task<int> RunAsync(bool databaseOnly)
    {
        var tests = databaseOnly
            ? new (string Name, Func<Task> Test)[] {
                ("RANK-001 lower score cannot replace a best run", BestRunAsync),
                ("RANK-002 all tie breakers and concurrent completion are stable", TieBreakAsync),
                ("RANK-003 synthetic and rejected runs cannot enter ranking", ExclusionAsync),
                ("RANK-004 closed season rejects leaderboard mutations", FrozenSeasonAsync),
                ("SEASON-002 fixed season context cannot be changed", ImmutableSeasonAsync),
                ("TICKET-004 expired tickets cannot create runs", ExpiredTicketAsync),
            }
            : new (string Name, Func<Task> Test)[] {
                ("PLAYER-001 register, signed ticket, replay, ranking and ownership", EndToEndAsync),
                ("TICKET-001 concurrent begin and submit are idempotent", IdempotencyAsync),
                ("TICKET-002 tampering and foreign ownership are rejected", TamperingAsync),
                ("TICKET-003 altered checksum and schema are rejected", ChecksumAsync),
            };
        var failures = 0;
        foreach (var test in tests)
        {
            try { await test.Test(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {error}"); }
            await Task.Delay(1100); // Keep tests below the intentional shared submission limit.
        }
        Console.WriteLine($"Ranking Specs: {tests.Length - failures} passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }

    private static HttpClient Client(string? credential = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("SIMOPS_API_URL") ?? "http://127.0.0.1:5080") };
        if (credential is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return client;
    }

    private static async Task<T> Post<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body, ContractJson.Options);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(ContractJson.Options))!;
    }

    private static async Task<PlayerCredential> RegisterAsync(HttpClient client) =>
        await Post<PlayerCredential>(client, "/api/v1/player/register", new RegisterPlayerRequest("Spec " + Guid.NewGuid().ToString("N")[..8]));

    private static async Task<BeginRunResponse> BeginAsync(HttpClient client, string? key = null)
    {
        var season = (await client.GetFromJsonAsync<SeasonInfo>("/api/v1/public/seasons/active", ContractJson.Options))!;
        return await Post<BeginRunResponse>(client, "/api/v1/player/tickets", new BeginRunRequest(season.SeasonId, CoreArtifact.Checksum, key ?? Guid.NewGuid().ToString("N")));
    }

    private static (HumanRunSubmission Submission, VerificationOutput Output) Play(BeginRunResponse ticket, bool lose = false)
    {
        var config = GameConfig.CreateBaseline();
        var score = ScoreRule.CreateBaseline();
        var seed = ulong.Parse(ticket.Context.BaseSeed, CultureInfo.InvariantCulture);
        var agent = AgentFactory.CreateDefinitions().Single(agent => agent.Persona == AgentPersona.Greedy);
        IReadOnlyList<GameAction> actions;
        RunResult result;
        if (lose)
        {
            var game = new GameSimulation(config, score);
            var state = game.Reset(new RunContext(config.GameVersion, config.Checksum, score.Version, score.Checksum, seed));
            while (state.Phase != RunPhase.Terminal)
                state = game.Apply(new GameAction(state.NextActionSequence, GameActionType.EndTurn)).Observation;
            actions = game.ActionLog;
            result = game.GetCanonicalResult();
        }
        else
        {
            var run = SyntheticSimulation.Execute(config, score, agent, seed);
            actions = run.Actions;
            result = run.Result;
        }
        var submittedActions = actions.Select(a => new SubmittedAction(a.Sequence, a.ActionType, a.RewardId)).ToArray();
        var submission = new HumanRunSubmission(ticket.RunTicket, Guid.NewGuid().ToString("N"), CoreArtifact.Checksum, 1, result.ResultHash, submittedActions);
        var replay = new RunSubmission(submission.IdempotencyKey, "", "", config.GameVersion, config.Checksum, score.Version, score.Checksum,
            ticket.Context.BaseSeed, result.ResultHash, submittedActions);
        return (submission, new ReplayVerifier().Verify(replay));
    }

    private static async Task<RunStatusResponse> WaitAsync(HttpClient client, Guid runId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var run = (await client.GetFromJsonAsync<RunStatusResponse>($"/api/v1/player/runs/{runId}", ContractJson.Options))!;
            if (run.Status is "verified" or "rejected" or "failed") return run;
            await Task.Delay(100);
        }
        throw new TimeoutException("Human verification did not finish.");
    }

    private static async Task EndToEndAsync()
    {
        using var guest = Client();
        var player = await RegisterAsync(guest);
        using var client = Client(player.Credential);
        var ticket = await BeginAsync(client);
        var fixture = Play(ticket);
        var receipt = await Post<SubmissionReceipt>(client, "/api/v1/player/runs", fixture.Submission);
        var run = await WaitAsync(client, receipt.RunId);
        Check(run.Status == "verified" && run.Population == "human", "Human run did not verify.");
        var ranking = (await client.GetFromJsonAsync<LeaderboardResponse>($"/api/v1/public/seasons/{ticket.Context.SeasonId}/leaderboard?around=true&limit=5", ContractJson.Options))!;
        Check(ranking.CurrentPlayer?.RunId == receipt.RunId && ranking.Entries.Any(e => e.PlayerId == player.PlayerId), "Around-me ranking missed player.");
        using var other = Client((await RegisterAsync(guest)).Credential);
        using var denied = await other.GetAsync($"/api/v1/player/runs/{receipt.RunId}");
        Check(denied.StatusCode == HttpStatusCode.NotFound, "Other player read private run.");
        await using var db = new NpgsqlConnection(ConnectionString);
        await db.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT credential_hash FROM simops.human_players WHERE id=@id", db);
        query.Parameters.AddWithValue("id", player.PlayerId);
        Check((string)(await query.ExecuteScalarAsync())! == StableHash.Sha256Hex(player.Credential), "Credential was not stored as a hash.");
    }

    private static async Task IdempotencyAsync()
    {
        using var guest = Client();
        using var client = Client((await RegisterAsync(guest)).Credential);
        var key = Guid.NewGuid().ToString("N");
        var tickets = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => BeginAsync(client, key)));
        Check(tickets.Select(t => t.RunTicket).Distinct().Count() == 1, "Begin retry generated a new ticket.");
        var submission = Play(tickets[0]).Submission;
        var receipts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Post<SubmissionReceipt>(client, "/api/v1/player/runs", submission)));
        Check(receipts.Select(r => r.RunId).Distinct().Count() == 1, "Duplicate human submissions created multiple runs.");
        Check((await WaitAsync(client, receipts[0].RunId)).Status == "verified", "Idempotent run failed.");
        using var conflict = await client.PostAsJsonAsync("/api/v1/player/runs", submission with { ClientResultHash = new string('0', 64) }, ContractJson.Options);
        Check(conflict.StatusCode == HttpStatusCode.Conflict, "Changed payload reused key.");
        using var reused = await client.PostAsJsonAsync("/api/v1/player/runs", submission with { IdempotencyKey = Guid.NewGuid().ToString("N") }, ContractJson.Options);
        var error = await reused.Content.ReadFromJsonAsync<ApiError>(ContractJson.Options);
        Check(error?.Code == "TICKET_REUSED", "Used ticket accepted a new key.");
    }

    private static async Task TamperingAsync()
    {
        using var guest = Client();
        using var client = Client((await RegisterAsync(guest)).Credential);
        using var other = Client((await RegisterAsync(guest)).Credential);
        var submission = Play(await BeginAsync(client)).Submission;
        using var denied = await other.PostAsJsonAsync("/api/v1/player/runs", submission, ContractJson.Options);
        Check(denied.StatusCode == HttpStatusCode.Unauthorized, "Foreign player used ticket.");
        using var tampered = await client.PostAsJsonAsync("/api/v1/player/runs", submission with { RunTicket = "a" + submission.RunTicket[1..] }, ContractJson.Options);
        Check((await tampered.Content.ReadFromJsonAsync<ApiError>(ContractJson.Options))?.Code == "TICKET_INVALID", "Tampered ticket passed.");
    }

    private static async Task ChecksumAsync()
    {
        using var guest = Client();
        using var client = Client((await RegisterAsync(guest)).Credential);
        var submission = Play(await BeginAsync(client)).Submission;
        using var checksum = await client.PostAsJsonAsync("/api/v1/player/runs", submission with { ClientGameCoreChecksum = "bad" }, ContractJson.Options);
        Check((await checksum.Content.ReadFromJsonAsync<ApiError>(ContractJson.Options))?.Code == "CHECKSUM_MISMATCH", "Bad Core checksum passed.");
        using var schema = await client.PostAsJsonAsync("/api/v1/player/runs", submission with { ActionLogSchemaVersion = 2 }, ContractJson.Options);
        Check((await schema.Content.ReadFromJsonAsync<ApiError>(ContractJson.Options))?.Code == "ACTION_SCHEMA_INVALID", "Unknown schema passed.");
    }

    private static async Task<(Guid RunId, VerificationOutput Output)> SubmitDirect(PostgresRunStore store, Guid playerId, bool lose = false)
    {
        var ticket = await store.BeginHumanRunAsync(playerId, new BeginRunRequest(PostgresRunStore.BaselineSeasonId, CoreArtifact.Checksum, Guid.NewGuid().ToString("N")), Signer);
        var fixture = Play(ticket, lose);
        var receipt = await store.SubmitHumanRunAsync(playerId, fixture.Submission, Signer);
        return (receipt.RunId, fixture.Output);
    }

    private static async Task CompleteDirect(PostgresRunStore store, (Guid RunId, VerificationOutput Output) fixture)
    {
        var job = await store.ClaimJobAsync() ?? throw new InvalidOperationException("Stop external Worker for DB ranking specs.");
        Check(job.RunId == fixture.RunId, "Another queued job interfered with ranking specs.");
        await store.CompleteJobAsync(job, fixture.Output);
    }

    private static async Task BestRunAsync()
    {
        await using var store = new PostgresRunStore(ConnectionString);
        var player = await store.RegisterPlayerAsync(new RegisterPlayerRequest("DB best run"));
        var high = await SubmitDirect(store, player.PlayerId);
        await CompleteDirect(store, high);
        var low = await SubmitDirect(store, player.PlayerId, lose: true);
        Check(high.Output.Summary!.FinalScore > low.Output.Summary!.FinalScore, "Score fixture is invalid.");
        await CompleteDirect(store, low);
        var ranking = await store.GetLeaderboardAsync(PostgresRunStore.BaselineSeasonId, player.PlayerId, true, 0, 5);
        Check(ranking?.CurrentPlayer?.RunId == high.RunId, "Lower score replaced best run.");
    }

    private static async Task TieBreakAsync()
    {
        await using var store = new PostgresRunStore(ConnectionString);
        var player = await store.RegisterPlayerAsync(new RegisterPlayerRequest("DB tie rules"));
        // Trusted-worker output fixtures isolate ranking comparison from probabilistic gameplay outcomes.
        Guid best = Guid.Empty;
        var index = 0;
        foreach (var metrics in new[] { (2, 20, 50), (3, 20, 50), (3, 19, 50), (3, 19, 60), (3, 19, 60) })
        {
            var fixture = await SubmitDirect(store, player.PlayerId);
            var output = fixture.Output with { Summary = fixture.Output.Summary! with {
                FinalScore = 500, ClearedStages = metrics.Item1, TotalTurns = metrics.Item2, FinalHealth = metrics.Item3, MaxHealth = 100 } };
            await CompleteDirect(store, (fixture.RunId, output));
            var ranking = await store.GetLeaderboardAsync(PostgresRunStore.BaselineSeasonId, player.PlayerId, true, 0, 5);
            if (index == 4) Check(ranking!.CurrentPlayer!.RunId == best, "Exact tie replaced earlier record.");
            else Check(ranking!.CurrentPlayer!.RunId == fixture.RunId, "A better tie breaker did not replace the record.");
            best = ranking!.CurrentPlayer!.RunId;
            index++;
        }
        var one = await SubmitDirect(store, player.PlayerId);
        var two = await SubmitDirect(store, player.PlayerId, lose: true);
        var firstJob = (await store.ClaimJobAsync())!;
        var secondJob = (await store.ClaimJobAsync())!;
        Check(firstJob.RunId == one.RunId && secondJob.RunId == two.RunId, "Concurrent fixture claim order changed.");
        await Task.WhenAll(store.CompleteJobAsync(firstJob, one.Output), store.CompleteJobAsync(secondJob, two.Output));
        Check((await store.GetLeaderboardAsync(PostgresRunStore.BaselineSeasonId, player.PlayerId, true, 0, 5))?.CurrentPlayer?.RunId == one.RunId,
            "Concurrent completion lost the higher score.");
    }

    private static async Task ExclusionAsync()
    {
        await using var store = new PostgresRunStore(ConnectionString);
        var player = await store.RegisterPlayerAsync(new RegisterPlayerRequest("DB rejected"));
        var fixture = await SubmitDirect(store, player.PlayerId);
        await CompleteDirect(store, (fixture.RunId, new VerificationOutput(false, "RESULT_MISMATCH", null, [], [])));
        Check((await store.GetLeaderboardAsync(PostgresRunStore.BaselineSeasonId, player.PlayerId, true, 0, 5))?.CurrentPlayer is null, "Rejected run entered ranking.");
        await ExpectDatabaseRejection("""
            INSERT INTO simops.leaderboard_entries(season_id,player_id,run_id,score,cleared_stages,total_turns,final_health,max_health,verified_at)
            SELECT @season,@player,id,1,1,1,1,1,coalesce(verified_at,now()) FROM simops.runs WHERE population='synthetic' LIMIT 1
            """, player.PlayerId);
    }

    private static async Task FrozenSeasonAsync()
    {
        await ExpectDatabaseRejection("""
            UPDATE simops.seasons SET status='closed' WHERE id=@season;
            UPDATE simops.leaderboard_entries SET score=score WHERE season_id=@season
            """);
    }

    private static Task ImmutableSeasonAsync() => ExpectDatabaseRejection("UPDATE simops.seasons SET game_version='mutated' WHERE id=@season");

    private static async Task ExpiredTicketAsync()
    {
        await using var store = new PostgresRunStore(ConnectionString);
        var player = await store.RegisterPlayerAsync(new RegisterPlayerRequest("DB expired"));
        var ticket = await store.BeginHumanRunAsync(player.PlayerId, new BeginRunRequest(PostgresRunStore.BaselineSeasonId, CoreArtifact.Checksum, Guid.NewGuid().ToString("N")), Signer);
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE simops.run_tickets SET expires_at=now()-interval '1 second' WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", ticket.RunId);
        await command.ExecuteNonQueryAsync();
        try { await store.SubmitHumanRunAsync(player.PlayerId, Play(ticket).Submission, Signer); }
        catch (SubmissionValidationException error) when (error.Code == "TICKET_EXPIRED") { return; }
        throw new InvalidOperationException("Expired ticket was accepted.");
    }

    private static async Task ExpectDatabaseRejection(string sql, Guid? player = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("season", PostgresRunStore.BaselineSeasonId);
            if (player.HasValue) command.Parameters.AddWithValue("player", player.Value);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException error) when (error.SqlState == "P0001") { return; }
        finally { await transaction.RollbackAsync(); }
        throw new InvalidOperationException("Expected database guard did not reject mutation.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
