using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SimOps.Agent.Core;
using SimOps.Application;
using SimOps.Game.Core;

namespace SimOps.Infrastructure;

public sealed record ClaimedJob(Guid JobId, Guid RunId, Guid LockToken, int Attempts);

public sealed class PostgresRunStore : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresRunStore(string connectionString)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction,
            "SELECT pg_advisory_xact_lock(721042); CREATE SCHEMA IF NOT EXISTS simops; " +
            "CREATE TABLE IF NOT EXISTS simops.schema_migrations (version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());",
            cancellationToken);
        await using var versionCommand = new NpgsqlCommand("SELECT count(*) FROM simops.schema_migrations WHERE version = 1", connection, transaction);
        var applied = (long)(await versionCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        if (applied == 0)
        {
            var assembly = typeof(PostgresRunStore).Assembly;
            var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith("001_initial.sql", StringComparison.Ordinal));
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Initial schema resource is missing.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, sql, cancellationToken);
            await ExecuteAsync(connection, transaction, "INSERT INTO simops.schema_migrations(version) VALUES (1)", cancellationToken);
        }

        await SeedCatalogAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT 1");
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
    }

    public async Task<SubmissionReceipt> SubmitAsync(RunSubmission submission, CancellationToken cancellationToken = default)
    {
        SubmissionValidator.Validate(submission);
        var requestHash = StableHash.Sha256Hex(JsonSerializer.Serialize(submission, ContractJson.Options));
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", submission.IdempotencyKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var existingCommand = new NpgsqlCommand(
            "SELECT id, request_hash, status FROM simops.runs WHERE idempotency_key = @key", connection, transaction))
        {
            existingCommand.Parameters.AddWithValue("key", submission.IdempotencyKey);
            await using var reader = await existingCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (!string.Equals(reader.GetString(1), requestHash, StringComparison.Ordinal))
                {
                    throw new SubmissionConflictException();
                }

                return new SubmissionReceipt(reader.GetGuid(0), reader.GetString(2), true);
            }
        }

        var runId = Guid.NewGuid();
        await using (var insertRun = new NpgsqlCommand(
            """
            INSERT INTO simops.runs
              (id, population, agent_id, agent_version, game_version, config_checksum,
               score_rule_version, score_rule_checksum, base_seed, idempotency_key,
               request_hash, client_result_hash, action_count, status)
            VALUES
              (@id, 'synthetic', @agent, @agentVersion, @gameVersion, @config,
               @scoreVersion, @scoreChecksum, @seed, @key, @requestHash, @resultHash, @actionCount, 'submitted')
            """, connection, transaction))
        {
            insertRun.Parameters.AddWithValue("id", runId);
            insertRun.Parameters.AddWithValue("agent", submission.AgentId);
            insertRun.Parameters.AddWithValue("agentVersion", submission.AgentVersion);
            insertRun.Parameters.AddWithValue("gameVersion", submission.GameVersion);
            insertRun.Parameters.AddWithValue("config", submission.ConfigChecksum);
            insertRun.Parameters.AddWithValue("scoreVersion", submission.ScoreRuleVersion);
            insertRun.Parameters.AddWithValue("scoreChecksum", submission.ScoreRuleChecksum);
            insertRun.Parameters.AddWithValue("seed", decimal.Parse(submission.BaseSeed, CultureInfo.InvariantCulture));
            insertRun.Parameters.AddWithValue("key", submission.IdempotencyKey);
            insertRun.Parameters.AddWithValue("requestHash", requestHash);
            insertRun.Parameters.AddWithValue("resultHash", submission.ClientResultHash);
            insertRun.Parameters.AddWithValue("actionCount", submission.Actions.Count);
            await insertRun.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var batch = new NpgsqlBatch(connection, transaction))
        {
            foreach (var action in submission.Actions)
            {
                var command = new NpgsqlBatchCommand(
                    "INSERT INTO simops.run_actions(run_id, sequence, action_type, reward_id) VALUES (@run, @sequence, @type, @reward)");
                command.Parameters.AddWithValue("run", runId);
                command.Parameters.AddWithValue("sequence", action.Sequence);
                command.Parameters.AddWithValue("type", (int)action.ActionType);
                command.Parameters.AddWithValue("reward", NpgsqlDbType.Text, (object?)action.RewardId ?? DBNull.Value);
                batch.BatchCommands.Add(command);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var jobCommand = new NpgsqlCommand(
            "INSERT INTO simops.jobs(id, job_type, run_id, status) VALUES (@id, 'verify_run', @run, 'queued')", connection, transaction))
        {
            jobCommand.Parameters.AddWithValue("id", Guid.NewGuid());
            jobCommand.Parameters.AddWithValue("run", runId);
            await jobCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new SubmissionReceipt(runId, "submitted", false);
    }

    public async Task<RunStatusResponse?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT r.id, r.population, r.status, r.rejection_code, r.result_json::text,
                   r.action_count, (SELECT count(*)::integer FROM simops.run_events e WHERE e.run_id = r.id),
                   r.created_at, r.verified_at
            FROM simops.runs r WHERE r.id = @id
            """);
        command.Parameters.AddWithValue("id", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RunStatusResponse(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<VerifiedSummary>(reader.GetString(4), ContractJson.Options),
            reader.GetInt32(5), reader.GetInt32(6), reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8));
    }

    public async Task<ClaimedJob?> ClaimJobAsync(CancellationToken cancellationToken = default)
    {
        var token = Guid.NewGuid();
        await using var command = _dataSource.CreateCommand(
            """
            WITH exhausted AS (
              UPDATE simops.jobs SET status = 'failed', last_error = 'LEASE_EXPIRED_MAX_ATTEMPTS',
                locked_until = NULL, lock_token = NULL
              WHERE status = 'running' AND locked_until < now() AND attempts >= max_attempts
              RETURNING run_id
            ), failed_runs AS (
              UPDATE simops.runs SET status = 'failed', rejection_code = 'VERIFY_INTERNAL_ERROR'
              WHERE id IN (SELECT run_id FROM exhausted) AND status NOT IN ('verified', 'rejected')
              RETURNING id
            ), candidate AS (
              SELECT id FROM simops.jobs
              WHERE attempts < max_attempts AND
                ((status = 'queued' AND available_at <= now()) OR
                 (status = 'running' AND locked_until < now()))
              ORDER BY created_at, id
              FOR UPDATE SKIP LOCKED LIMIT 1
            ), claimed AS (
              UPDATE simops.jobs j SET status = 'running', attempts = attempts + 1,
                locked_until = now() + interval '30 seconds', lock_token = @token
              FROM candidate c WHERE j.id = c.id
              RETURNING j.id, j.run_id, j.lock_token, j.attempts
            ), marked AS (
              UPDATE simops.runs SET status = 'verifying'
              WHERE id IN (SELECT run_id FROM claimed) AND status = 'submitted'
              RETURNING id
            )
            SELECT id, run_id, lock_token, attempts FROM claimed
            """);
        command.Parameters.AddWithValue("token", token);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ClaimedJob(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt32(3))
            : null;
    }

    public async Task<RunSubmission> LoadSubmissionAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        RunSubmission submission;
        await using (var command = new NpgsqlCommand(
            """
            SELECT idempotency_key, agent_id, agent_version, game_version, config_checksum,
                   score_rule_version, score_rule_checksum, base_seed::text, client_result_hash
            FROM simops.runs WHERE id = @id
            """, connection))
        {
            command.Parameters.AddWithValue("id", runId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Queued run was not found.");
            }

            submission = new RunSubmission(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), Array.Empty<SubmittedAction>());
        }

        var actions = new List<SubmittedAction>();
        await using (var command = new NpgsqlCommand(
            "SELECT sequence, action_type, reward_id FROM simops.run_actions WHERE run_id = @id ORDER BY sequence", connection))
        {
            command.Parameters.AddWithValue("id", runId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                actions.Add(new SubmittedAction(reader.GetInt32(0), (GameActionType)reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        return submission with { Actions = actions };
    }

    public async Task CompleteJobAsync(ClaimedJob job, VerificationOutput output, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT id FROM simops.jobs WHERE id = @id AND status = 'running' AND lock_token = @token FOR UPDATE", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("id", job.JobId);
            lockCommand.Parameters.AddWithValue("token", job.LockToken);
            if (await lockCommand.ExecuteScalarAsync(cancellationToken) is null)
            {
                return;
            }
        }

        await using (var update = new NpgsqlCommand(
            """
            UPDATE simops.runs SET status = @status, rejection_code = @code,
              result_json = @result, verified_at = CASE WHEN @status = 'verified' THEN now() ELSE NULL END
            WHERE id = @id AND status NOT IN ('verified', 'rejected', 'failed')
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("status", output.Verified ? "verified" : "rejected");
            update.Parameters.AddWithValue("code", NpgsqlDbType.Text, (object?)output.RejectionCode ?? DBNull.Value);
            update.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb,
                output.Summary is null ? DBNull.Value : JsonSerializer.Serialize(output.Summary, ContractJson.Options));
            update.Parameters.AddWithValue("id", job.RunId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var batch = new NpgsqlBatch(connection, transaction))
        {
            foreach (var stage in output.Stages)
            {
                var command = new NpgsqlBatchCommand(
                    "INSERT INTO simops.run_stage_summaries(run_id, stage_index, encounter_id, cleared, turns) " +
                    "VALUES (@run, @stage, @encounter, @cleared, @turns) ON CONFLICT DO NOTHING");
                command.Parameters.AddWithValue("run", job.RunId);
                command.Parameters.AddWithValue("stage", stage.Stage);
                command.Parameters.AddWithValue("encounter", stage.EncounterId);
                command.Parameters.AddWithValue("cleared", stage.Cleared);
                command.Parameters.AddWithValue("turns", stage.Turns);
                batch.BatchCommands.Add(command);
            }

            foreach (var entry in output.Events)
            {
                var command = new NpgsqlBatchCommand(
                    "INSERT INTO simops.run_events(run_id, sequence, event_type, stage_index, turn_index, payload) " +
                    "VALUES (@run, @sequence, @type, @stage, @turn, @payload) ON CONFLICT DO NOTHING");
                command.Parameters.AddWithValue("run", job.RunId);
                command.Parameters.AddWithValue("sequence", entry.Sequence);
                command.Parameters.AddWithValue("type", entry.EventType);
                command.Parameters.AddWithValue("stage", entry.Stage);
                command.Parameters.AddWithValue("turn", entry.Turn);
                command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, entry.PayloadJson);
                batch.BatchCommands.Add(command);
            }

            if (batch.BatchCommands.Count > 0)
            {
                await batch.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var complete = new NpgsqlCommand(
            "UPDATE simops.jobs SET status = 'succeeded', completed_at = now(), locked_until = NULL, lock_token = NULL WHERE id = @id AND lock_token = @token", connection, transaction))
        {
            complete.Parameters.AddWithValue("id", job.JobId);
            complete.Parameters.AddWithValue("token", job.LockToken);
            await complete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailJobAsync(ClaimedJob job, string errorCode, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE simops.jobs SET status = CASE WHEN attempts >= max_attempts THEN 'failed' ELSE 'queued' END,
              available_at = now() + interval '2 seconds', locked_until = NULL, lock_token = NULL, last_error = @error
            WHERE id = @id AND lock_token = @token;
            UPDATE simops.runs SET status = 'failed', rejection_code = 'VERIFY_INTERNAL_ERROR'
            WHERE id = @run AND status NOT IN ('verified', 'rejected') AND
              EXISTS (SELECT 1 FROM simops.jobs WHERE id = @id AND status = 'failed');
            """, connection, transaction);
        command.Parameters.AddWithValue("id", job.JobId);
        command.Parameters.AddWithValue("token", job.LockToken);
        command.Parameters.AddWithValue("run", job.RunId);
        command.Parameters.AddWithValue("error", errorCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static async Task SeedCatalogAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var config = GameConfig.CreateBaseline();
        var score = ScoreRule.CreateBaseline();
        await using var batch = new NpgsqlBatch(connection, transaction);
        var configCommand = new NpgsqlBatchCommand(
            "INSERT INTO simops.game_configs(checksum, game_version, config_version, content) VALUES (@checksum, @game, @version, @content) ON CONFLICT DO NOTHING");
        configCommand.Parameters.AddWithValue("checksum", config.Checksum);
        configCommand.Parameters.AddWithValue("game", config.GameVersion);
        configCommand.Parameters.AddWithValue("version", config.ConfigVersion);
        configCommand.Parameters.AddWithValue("content", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(config, ContractJson.Options));
        batch.BatchCommands.Add(configCommand);
        var scoreCommand = new NpgsqlBatchCommand(
            "INSERT INTO simops.score_rules(checksum, version, definition) VALUES (@checksum, @version, @definition) ON CONFLICT DO NOTHING");
        scoreCommand.Parameters.AddWithValue("checksum", score.Checksum);
        scoreCommand.Parameters.AddWithValue("version", score.Version);
        scoreCommand.Parameters.AddWithValue("definition", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(score, ContractJson.Options));
        batch.BatchCommands.Add(scoreCommand);
        foreach (var agent in AgentFactory.CreateDefinitions())
        {
            var command = new NpgsqlBatchCommand(
                "INSERT INTO simops.agent_definitions(agent_id, version, persona) VALUES (@id, @version, @persona) ON CONFLICT DO NOTHING");
            command.Parameters.AddWithValue("id", agent.Id);
            command.Parameters.AddWithValue("version", agent.Version);
            command.Parameters.AddWithValue("persona", (int)agent.Persona);
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
