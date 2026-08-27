using System.Text.Json;
using Npgsql;
using SimOps.Application;
using SimOps.Experiments;
using SimOps.Game.Core;
using SimOps.Game.Transport;

namespace SimOps.Infrastructure;

public sealed partial class PostgresRunStore
{
    public async Task<GameConfig> LoadRegisteredConfigAsync(string checksum, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(checksum) || checksum.Length != 64)
            throw new SubmissionValidationException("VERSION_MISMATCH", "A registered config checksum is required.");
        var json = await GetRegisteredConfigJsonAsync(checksum, token);
        if (json is null) throw new SubmissionValidationException("VERSION_MISMATCH", "Config is not registered.");
        var config = JsonSerializer.Deserialize<GameConfig>(json, ContractJson.Options)!;
        if (config.Checksum != checksum || config.GameVersion != GameConfig.CreateBaseline().GameVersion)
            throw new SubmissionValidationException("VERSION_MISMATCH", "Config content or Game Core version differs.");
        return config;
    }

    public async Task<PublishedConfig?> GetSeasonConfigAsync(Guid seasonId, CancellationToken token = default)
    {
        string? checksum;
        await using (var command = _dataSource.CreateCommand("SELECT config_checksum FROM simops.seasons WHERE id=@id"))
        { command.Parameters.AddWithValue("id", seasonId); checksum = await command.ExecuteScalarAsync(token) as string; }
        return checksum is null ? null : PublishedConfig.From(await LoadRegisteredConfigAsync(checksum, token));
    }

    public async Task<IReadOnlyList<Publication>> ListPublicationsAsync(CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT id,kind,previous_season_id,season_id,config_checksum,experiment_id,reason,created_at FROM simops.config_publications ORDER BY created_at DESC,id LIMIT 100");
        await using var reader = await command.ExecuteReaderAsync(token);
        var items = new List<Publication>();
        while (await reader.ReadAsync(token)) items.Add(ReadPublication(reader));
        return items;
    }
    private static Publication ReadPublication(NpgsqlDataReader r) => new(r.GetGuid(0),r.GetString(1),r.GetGuid(2),r.GetGuid(3),r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),r.GetString(6),r.GetFieldValue<DateTimeOffset>(7));

    public Task<Publication> PublishConfigAsync(PublishConfigRequest request, CancellationToken token = default) =>
        TransitionSeasonAsync("publish", request, null, token);
    public Task<Publication> RollbackConfigAsync(RollbackConfigRequest request, CancellationToken token = default) =>
        TransitionSeasonAsync("rollback", null, request, token);

    private async Task<Publication> TransitionSeasonAsync(string kind, PublishConfigRequest? publish, RollbackConfigRequest? rollback, CancellationToken token)
    {
        var name = publish is not null ? publish.Name : rollback!.Name;
        var reason = publish is not null ? publish.Reason : rollback!.Reason;
        var key = publish is not null ? publish.IdempotencyKey : rollback!.IdempotencyKey;
        var expected = publish?.ExpectedSeasonId ?? rollback!.ExpectedSeasonId;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000 ||
            string.IsNullOrWhiteSpace(key) || key.Length > 100 || expected == Guid.Empty ||
            (publish is not null && (string.IsNullOrWhiteSpace(publish.ExperimentId) || publish.ExperimentId.Length > 64)))
            throw new ExperimentCommandException("PUBLICATION_INVALID", "Name, reason and bounded idempotency key are required.", 400);
        var requestHash = AnalysisEvidence.Hash(new { kind, publish, rollback });
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(721048)", token);
        await using (var prior = new NpgsqlCommand("SELECT id,kind,previous_season_id,season_id,config_checksum,experiment_id,reason,created_at,request_hash FROM simops.config_publications WHERE idempotency_key=@key", connection, transaction))
        {
            prior.Parameters.AddWithValue("key", key);
            await using var reader = await prior.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                if (reader.GetString(8) != requestHash) throw new ExperimentCommandException("IDEMPOTENCY_CONFLICT", "Key was used for another publication.");
                return ReadPublication(reader);
            }
        }
        SeasonInfo active;
        await using (var query = new NpgsqlCommand("SELECT id,name,status,game_version,game_core_checksum,config_checksum,score_rule_version,score_rule_checksum,starts_at,ends_at FROM simops.seasons WHERE status='active' FOR UPDATE", connection, transaction))
        {
            await using var reader = await query.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new ExperimentCommandException("SEASON_NOT_ACTIVE", "An active season is required.");
            active = ReadSeason(reader);
        }
        if (active.SeasonId != expected) throw new ExperimentCommandException("SEASON_CHANGED", "Refresh the active season; another publication already occurred.");
        if (active.GameCoreChecksum != CoreArtifact.Checksum || active.ScoreRuleChecksum != ScoreRule.CreateBaseline().Checksum)
            throw new ExperimentCommandException("PUBLICATION_VERSION_UNSUPPORTED", "This host cannot publish the active season runtime.");
        string checksum;
        if (publish is not null)
        {
            // Decision is immutable. Never infer approval from AI or guardrail success alone.
            var experiment = await ReadExperimentAsync(connection, transaction, publish.ExperimentId, false, token);
            if (experiment?.Status != "decided" || experiment.Decision is not { Conclusion: "approved_candidate" } decision ||
                decision.PlanHash != publish.PlanHash || decision.ResultDigest != publish.ResultDigest || decision.SelectedVariantId != publish.VariantId)
                throw new ExperimentCommandException("CONFIG_NOT_APPROVED", "A matching human-approved candidate is required.");
            var config = experiment.Definition.CreateConfig(experiment.Definition.Variants.Single(v => v.Id == publish.VariantId));
            try { PublishedConfig.From(config); }
            catch (ArgumentException)
            {
                throw new ExperimentCommandException("PUBLICATION_CONFIG_UNSUPPORTED", "Candidate exceeds the supported client config envelope.", 400);
            }
            checksum = config.Checksum;
        }
        else
        {
            // Roll back only to an earlier closed season that actually preceded a recorded transition.
            await using var target = new NpgsqlCommand("SELECT s.config_checksum FROM simops.seasons s WHERE s.id=@id AND s.status='closed' AND s.game_core_checksum=@core AND s.score_rule_checksum=@score AND EXISTS(SELECT 1 FROM simops.config_publications p WHERE p.previous_season_id=s.id)", connection, transaction);
            target.Parameters.AddWithValue("id", rollback!.TargetSeasonId); target.Parameters.AddWithValue("core", active.GameCoreChecksum); target.Parameters.AddWithValue("score", active.ScoreRuleChecksum);
            checksum = await target.ExecuteScalarAsync(token) as string ?? throw new ExperimentCommandException("ROLLBACK_TARGET_INVALID", "Select a previously published compatible season.");
        }
        if (checksum == active.ConfigChecksum) throw new ExperimentCommandException("CONFIG_ALREADY_ACTIVE", "This config is already active.");
        // The active row lock excludes ticket issuance, submission, and ranking completion during the transition.
        await using (var close = new NpgsqlCommand("UPDATE simops.seasons SET status='closed',ends_at=COALESCE(ends_at,clock_timestamp()) WHERE id=@id", connection, transaction))
        { close.Parameters.AddWithValue("id", active.SeasonId); await close.ExecuteNonQueryAsync(token); }
        var seasonId = Guid.NewGuid(); var publicationId = Guid.NewGuid();
        await using (var create = new NpgsqlCommand("""
            INSERT INTO simops.seasons(id,name,status,game_version,game_core_checksum,config_checksum,score_rule_version,score_rule_checksum)
            VALUES (@id,@name,'active',@game,@core,@config,@version,@score);
            INSERT INTO simops.config_publications(id,kind,idempotency_key,request_hash,previous_season_id,season_id,config_checksum,experiment_id,reason)
            VALUES (@publication,@kind,@key,@hash,@previous,@id,@config,@experiment,@reason);
            """, connection, transaction))
        {
            create.Parameters.AddWithValue("id", seasonId); create.Parameters.AddWithValue("name", name);
            create.Parameters.AddWithValue("game", active.GameVersion); create.Parameters.AddWithValue("core", active.GameCoreChecksum);
            create.Parameters.AddWithValue("config", checksum); create.Parameters.AddWithValue("version", active.ScoreRuleVersion); create.Parameters.AddWithValue("score", active.ScoreRuleChecksum);
            create.Parameters.AddWithValue("publication", publicationId); create.Parameters.AddWithValue("kind", kind);
            create.Parameters.AddWithValue("key", key); create.Parameters.AddWithValue("hash", requestHash);
            create.Parameters.AddWithValue("previous", active.SeasonId); create.Parameters.AddWithValue("experiment", NpgsqlTypes.NpgsqlDbType.Text, (object?)publish?.ExperimentId ?? DBNull.Value);
            create.Parameters.AddWithValue("reason", reason); await create.ExecuteNonQueryAsync(token);
        }
        Publication result;
        await using (var read = new NpgsqlCommand("SELECT id,kind,previous_season_id,season_id,config_checksum,experiment_id,reason,created_at FROM simops.config_publications WHERE id=@id", connection, transaction))
        { read.Parameters.AddWithValue("id", publicationId); await using var reader = await read.ExecuteReaderAsync(token); await reader.ReadAsync(token); result = ReadPublication(reader); }
        await transaction.CommitAsync(token);
        return result;
    }
}
