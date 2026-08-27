using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SimOps.Application;
using SimOps.Experiments;

namespace SimOps.Infrastructure;

public sealed partial class PostgresRunStore
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ExperimentJson.Options);
    private static void JsonParameter(NpgsqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Jsonb, Json(value));

    public async Task<ExperimentDetail> SaveExperimentAsync(SaveExperimentRequest request, CancellationToken token = default)
    {
        ExperimentDefinition definition;
        try { definition = ExperimentJson.Parse(Json(request.Definition)); }
        catch (Exception ex) when (ex is ArgumentException or JsonException) { throw new ExperimentCommandException("EXPERIMENT_INVALID", ex.Message, 400); }
        if (definition.RunsPerCell > 1000 || definition.BootstrapReplicates > 2000)
            throw new ExperimentCommandException("EXPERIMENT_LIMIT", "Server limit: 1000 runs/cell and 2000 bootstrap repetitions.", 400);
        var hash = ExperimentRunner.PlanHash(definition);
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id, 17))", connection, transaction))
        {
            gate.Parameters.AddWithValue("id", definition.ExperimentId);
            await gate.ExecuteNonQueryAsync(token);
        }
        var existing = await ReadExperimentAsync(connection, transaction, definition.ExperimentId, true, token);
        if (existing is not null && existing.PlanHash == hash)
        {
            await transaction.CommitAsync(token);
            return existing;
        }
        if (existing is not null && (existing.Status != "draft" || existing.Revision != request.ExpectedRevision))
            throw new ExperimentCommandException("EXPERIMENT_LOCKED", "Only the current draft revision can be changed.");
        if (existing is null && request.ExpectedRevision != 0)
            throw new ExperimentCommandException("REVISION_CONFLICT", "New experiments require revision 0.");
        await using var command = new NpgsqlCommand(existing is null
            ? "INSERT INTO simops.experiments(id, definition, plan_hash) VALUES (@id,@definition,@hash)"
            : "UPDATE simops.experiments SET definition=@definition, plan_hash=@hash, revision=revision+1 WHERE id=@id", connection, transaction);
        command.Parameters.AddWithValue("id", definition.ExperimentId);
        command.Parameters.AddWithValue("hash", hash);
        JsonParameter(command, "definition", definition);
        await command.ExecuteNonQueryAsync(token);
        await AuditAsync(connection, transaction, definition.ExperimentId, "draft_saved", new { planHash = hash }, token);
        await transaction.CommitAsync(token);
        return new ExperimentDetail(definition.ExperimentId, "draft", (existing?.Revision ?? 0) + 1, hash, definition, null, null);
    }

    public async Task<IReadOnlyList<ExperimentListItem>> ListExperimentsAsync(CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT id,status,revision,plan_hash,created_at FROM simops.experiments ORDER BY created_at DESC,id LIMIT 100");
        await using var reader = await command.ExecuteReaderAsync(token);
        var items = new List<ExperimentListItem>();
        while (await reader.ReadAsync(token)) items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
        return items;
    }

    public async Task<ExperimentDetail?> GetExperimentAsync(string id, CancellationToken token = default)
    {
        ExperimentDetail? experiment;
        object? batch;
        await using (var connection = await _dataSource.OpenConnectionAsync(token))
        {
            experiment = await ReadExperimentAsync(connection, null, id, false, token);
            if (experiment is null) return null;
            await using var command = new NpgsqlCommand("SELECT id FROM simops.simulation_batches WHERE experiment_id=@id", connection);
            command.Parameters.AddWithValue("id", id);
            batch = await command.ExecuteScalarAsync(token);
        }
        // Release the first pooled connection before another method opens one.
        return experiment with { Batch = batch is Guid batchId ? await GetBatchAsync(batchId, token) : null };
    }

    private static async Task<ExperimentDetail?> ReadExperimentAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string id, bool locked, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT id,status,revision,plan_hash,definition::text,decision::text FROM simops.experiments WHERE id=@id" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? new ExperimentDetail(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
            ExperimentJson.Parse(reader.GetString(4)), null, reader.IsDBNull(5) ? null : JsonSerializer.Deserialize<ExperimentDecisionRequest>(reader.GetString(5), ExperimentJson.Options)) : null;
    }

    private static void RequirePlan(ExperimentDetail? experiment, string hash)
    {
        if (experiment is null) throw new ExperimentCommandException("EXPERIMENT_NOT_FOUND", "Experiment not found.", 404);
        if (experiment.PlanHash != hash) throw new ExperimentCommandException("PLAN_CHANGED", "Refresh the registered plan before issuing this command.");
    }

    public async Task MarkExperimentReadyAsync(string id, string hash, CancellationToken token = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var experiment = await ReadExperimentAsync(connection, transaction, id, true, token);
        RequirePlan(experiment, hash);
        if (experiment!.Status == "ready") return;
        if (experiment.Status != "draft") throw new ExperimentCommandException("EXPERIMENT_STATE", "Only drafts can become ready.");
        foreach (var variant in experiment.Definition.Variants)
        {
            var config = experiment.Definition.CreateConfig(variant);
            await using var command = new NpgsqlCommand("""
                INSERT INTO simops.game_configs(checksum,game_version,config_version,content)
                VALUES (@checksum,@game,@version,@config) ON CONFLICT DO NOTHING;
                INSERT INTO simops.experiment_variants(experiment_id,variant_id,role,config_checksum)
                VALUES (@id,@variant,@role,@checksum);
                """, connection, transaction);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("variant", variant.Id);
            command.Parameters.AddWithValue("role", variant.Role);
            command.Parameters.AddWithValue("checksum", config.Checksum);
            command.Parameters.AddWithValue("game", config.GameVersion);
            command.Parameters.AddWithValue("version", config.ConfigVersion);
            JsonParameter(command, "config", config);
            await command.ExecuteNonQueryAsync(token);
        }
        await SetExperimentStateAsync(connection, transaction, id, "ready", token);
        await AuditAsync(connection, transaction, id, "ready", new { planHash = hash }, token);
        await transaction.CommitAsync(token);
    }

    public async Task<Guid> StartBatchAsync(string id, StartBatchRequest request, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            throw new ExperimentCommandException("KEY_INVALID", "A bounded idempotency key is required.", 400);
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        // Serialize admissions, not simulation. This makes the global two-batch capacity race-safe.
        await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(721046)", token);
        var experiment = await ReadExperimentAsync(connection, transaction, id, true, token);
        RequirePlan(experiment, request.PlanHash);
        await using (var prior = new NpgsqlCommand("SELECT id,experiment_id FROM simops.simulation_batches WHERE experiment_id=@id OR idempotency_key=@key", connection, transaction))
        {
            prior.Parameters.AddWithValue("id", id);
            prior.Parameters.AddWithValue("key", request.IdempotencyKey);
            await using var reader = await prior.ExecuteReaderAsync(token);
            Guid? same = null;
            while (await reader.ReadAsync(token))
            {
                if (reader.GetString(1) != id) throw new ExperimentCommandException("IDEMPOTENCY_CONFLICT", "Key belongs to another experiment.");
                same = reader.GetGuid(0);
            }
            if (same is not null) return same.Value;
        }
        if (experiment!.Status != "ready") throw new ExperimentCommandException("EXPERIMENT_STATE", "Mark the experiment ready before starting.");
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM simops.simulation_batches WHERE status IN ('queued','running')", connection, transaction))
            if ((long)(await count.ExecuteScalarAsync(token))! >= 2)
                throw new ExperimentCommandException("SIMULATION_CAPACITY", "Two batches are already active; retry after completion or cancellation.", 429);
        var batchId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO simops.simulation_batches(id,experiment_id,idempotency_key,execution_fingerprint,expected_cells,expected_runs)
            VALUES (@batch,@id,@key,@fingerprint,18,@runs)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("batch", batchId);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("key", request.IdempotencyKey);
            insert.Parameters.AddWithValue("fingerprint", ExperimentRunner.ExecutionFingerprint);
            insert.Parameters.AddWithValue("runs", 18 * experiment.Definition.RunsPerCell);
            await insert.ExecuteNonQueryAsync(token);
        }
        foreach (var variant in experiment.Definition.Variants)
            foreach (var agent in experiment.Definition.AgentIds)
                await InsertSimulationJobAsync(connection, transaction, batchId, "cell", variant.Id, agent, token);
        await InsertSimulationJobAsync(connection, transaction, batchId, "aggregate", null, null, token);
        await SetExperimentStateAsync(connection, transaction, id, "running", token);
        await AuditAsync(connection, transaction, id, "batch_started", new { batchId, request.PlanHash }, token);
        await transaction.CommitAsync(token);
        return batchId;
    }

    private static async Task InsertSimulationJobAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid batch,
        string kind, string? variant, string? agent, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("INSERT INTO simops.simulation_jobs(id,batch_id,kind,variant_id,agent_id) VALUES (@id,@batch,@kind,@variant,@agent)", connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("batch", batch);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("variant", NpgsqlDbType.Text, (object?)variant ?? DBNull.Value);
        command.Parameters.AddWithValue("agent", NpgsqlDbType.Text, (object?)agent ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<BatchProgress?> GetBatchAsync(Guid id, CancellationToken token = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        BatchProgress progress;
        await using (var command = new NpgsqlCommand("""
            SELECT b.status,b.expected_cells,b.expected_runs,b.result_digest,
                (SELECT count(*)::int FROM simops.experiment_cells WHERE batch_id=b.id),
                (SELECT coalesce(sum(valid_runs),0)::int FROM simops.experiment_cells WHERE batch_id=b.id)
            FROM simops.simulation_batches b WHERE b.id=@id
            """, connection))
        {
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            progress = new(id, reader.GetString(0), reader.GetInt32(1), reader.GetInt32(4), reader.GetInt32(2), reader.GetInt32(5), [], reader.IsDBNull(3) ? null : reader.GetString(3));
        }
        var jobs = new List<SimulationJobProgress>();
        await using (var command = new NpgsqlCommand("SELECT kind,variant_id,agent_id,status,attempts,last_error FROM simops.simulation_jobs WHERE batch_id=@id ORDER BY kind DESC,variant_id,agent_id", connection))
        {
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) jobs.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return progress with { Jobs = jobs };
    }

    public async Task<string?> GetExperimentResultJsonAsync(string id, bool full, CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand($"SELECT {(full ? "report" : "summary")}::text FROM simops.simulation_batches WHERE experiment_id=@id AND status='completed'");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(token) as string;
    }

    public async Task<string?> GetRegisteredConfigJsonAsync(string checksum, CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT content::text FROM simops.game_configs WHERE checksum=@checksum");
        command.Parameters.AddWithValue("checksum", checksum);
        return await command.ExecuteScalarAsync(token) as string;
    }

    public async Task<ClaimedSimulationJob?> ClaimSimulationJobAsync(CancellationToken token = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        Guid batchId;
        string experimentId, fingerprint;
        // All mutation paths lock batch -> job. SKIP LOCKED lets other workers claim other batches.
        await using (var candidate = new NpgsqlCommand("""
            SELECT b.id,b.experiment_id,b.execution_fingerprint FROM simops.simulation_batches b
            WHERE b.status IN ('queued','running') AND EXISTS (
                SELECT 1 FROM simops.simulation_jobs j WHERE j.batch_id=b.id AND
                ((j.status='queued' AND j.available_at<=now()) OR (j.status='running' AND j.locked_until<now())) AND
                (j.kind='cell' OR (SELECT count(*) FROM simops.experiment_cells WHERE batch_id=b.id)=b.expected_cells))
            ORDER BY b.created_at,b.id FOR UPDATE OF b SKIP LOCKED LIMIT 1
            """, connection, transaction))
        {
            await using var reader = await candidate.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            batchId = reader.GetGuid(0); experimentId = reader.GetString(1); fingerprint = reader.GetString(2);
        }
        await using (var exhausted = new NpgsqlCommand("""
            UPDATE simops.simulation_jobs SET status='failed',last_error='LEASE_EXPIRED_MAX_ATTEMPTS',lock_token=NULL,locked_until=NULL
            WHERE batch_id=@batch AND status='running' AND locked_until<now() AND attempts>=max_attempts
            """, connection, transaction))
        {
            exhausted.Parameters.AddWithValue("batch", batchId);
            if (await exhausted.ExecuteNonQueryAsync(token) > 0)
            {
                await StopBatchAsync(connection, transaction, batchId, experimentId, "failed", token);
                await transaction.CommitAsync(token);
                return null;
            }
        }
        Guid jobId, lockToken;
        string kind;
        string? variant, agent;
        await using (var claim = new NpgsqlCommand("""
            UPDATE simops.simulation_jobs SET status='running',attempts=attempts+1,lock_token=@token,locked_until=now()+interval '30 seconds'
            WHERE id=(SELECT id FROM simops.simulation_jobs WHERE batch_id=@batch AND attempts<max_attempts AND
                ((status='queued' AND available_at<=now()) OR (status='running' AND locked_until<now())) AND
                (kind='cell' OR (SELECT count(*) FROM simops.experiment_cells WHERE batch_id=@batch)=18)
                ORDER BY kind DESC,variant_id,agent_id FOR UPDATE SKIP LOCKED LIMIT 1)
            RETURNING id,kind,variant_id,agent_id,lock_token
            """, connection, transaction))
        {
            claim.Parameters.AddWithValue("batch", batchId);
            claim.Parameters.AddWithValue("token", Guid.NewGuid());
            await using var reader = await claim.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            jobId = reader.GetGuid(0); kind = reader.GetString(1); variant = reader.IsDBNull(2) ? null : reader.GetString(2);
            agent = reader.IsDBNull(3) ? null : reader.GetString(3); lockToken = reader.GetGuid(4);
        }
        await using (var mark = new NpgsqlCommand("UPDATE simops.simulation_batches SET status='running' WHERE id=@batch", connection, transaction))
        { mark.Parameters.AddWithValue("batch", batchId); await mark.ExecuteNonQueryAsync(token); }
        var experiment = (await ReadExperimentAsync(connection, transaction, experimentId, false, token))!;
        await transaction.CommitAsync(token);
        return new(jobId, batchId, experimentId, kind, variant, agent, lockToken, fingerprint, experiment.Definition);
    }

    private static async Task<bool> OwnSimulationLeaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ClaimedSimulationJob job, CancellationToken token)
    {
        await using (var batch = new NpgsqlCommand("SELECT id FROM simops.simulation_batches WHERE id=@batch AND status='running' AND execution_fingerprint=@fingerprint FOR UPDATE", connection, transaction))
        {
            batch.Parameters.AddWithValue("batch", job.BatchId);
            batch.Parameters.AddWithValue("fingerprint", job.ExecutionFingerprint);
            if (await batch.ExecuteScalarAsync(token) is null) return false;
        }
        await using var lease = new NpgsqlCommand("SELECT id FROM simops.simulation_jobs WHERE id=@id AND batch_id=@batch AND status='running' AND lock_token=@token AND locked_until>clock_timestamp() FOR UPDATE", connection, transaction);
        lease.Parameters.AddWithValue("id", job.Id); lease.Parameters.AddWithValue("batch", job.BatchId); lease.Parameters.AddWithValue("token", job.LockToken);
        return await lease.ExecuteScalarAsync(token) is not null;
    }

    public async Task<bool> HeartbeatSimulationAsync(ClaimedSimulationJob job, CancellationToken token = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        if (!await OwnSimulationLeaseAsync(connection, transaction, job, token)) return false;
        await using var command = new NpgsqlCommand("UPDATE simops.simulation_jobs SET locked_until=clock_timestamp()+interval '30 seconds' WHERE id=@id", connection, transaction);
        command.Parameters.AddWithValue("id", job.Id);
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<bool> CompleteSimulationCellAsync(ClaimedSimulationJob job, CellResult cell, CancellationToken token = default)
    {
        if (job.Kind != "cell" || cell.VariantId != job.VariantId || cell.AgentId != job.AgentId || job.ExecutionFingerprint != ExperimentRunner.ExecutionFingerprint)
            throw new InvalidOperationException("Job execution context differs.");
        ExperimentRunner.ValidateCell(job.Definition, cell);
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        if (!await OwnSimulationLeaseAsync(connection, transaction, job, token)) return false;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO simops.experiment_cells(batch_id,variant_id,agent_id,valid_runs,sample_hash,content)
            VALUES (@batch,@variant,@agent,@runs,@hash,@content)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("batch", job.BatchId); insert.Parameters.AddWithValue("variant", cell.VariantId);
            insert.Parameters.AddWithValue("agent", cell.AgentId); insert.Parameters.AddWithValue("runs", cell.ValidRuns);
            insert.Parameters.AddWithValue("hash", cell.SampleHash); JsonParameter(insert, "content", cell);
            await insert.ExecuteNonQueryAsync(token);
        }
        await SucceedSimulationJobAsync(connection, transaction, job.Id, token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<IReadOnlyList<CellResult>> LoadSimulationCellsAsync(Guid batchId, CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT content::text FROM simops.experiment_cells WHERE batch_id=@batch");
        command.Parameters.AddWithValue("batch", batchId);
        await using var reader = await command.ExecuteReaderAsync(token);
        var cells = new List<CellResult>();
        while (await reader.ReadAsync(token)) cells.Add(JsonSerializer.Deserialize<CellResult>(reader.GetString(0), ExperimentJson.Options)!);
        return cells;
    }

    public async Task<bool> CompleteSimulationReportAsync(ClaimedSimulationJob job, ExperimentReport report, CancellationToken token = default)
    {
        if (job.Kind != "aggregate" || report.PlanHash != ExperimentRunner.PlanHash(job.Definition) ||
            report.CompletedRuns != 18 * job.Definition.RunsPerCell || report.ReplayCheckedRuns != report.CompletedRuns ||
            report.InvalidTransitionCount != 0 || report.ReplayMismatchCount != 0 || job.ExecutionFingerprint != ExperimentRunner.ExecutionFingerprint)
            throw new InvalidOperationException("Incomplete or mixed experiment report.");
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        if (!await OwnSimulationLeaseAsync(connection, transaction, job, token)) return false;
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM simops.experiment_cells WHERE batch_id=@batch", connection, transaction))
        { count.Parameters.AddWithValue("batch", job.BatchId); if ((long)(await count.ExecuteScalarAsync(token))! != 18) throw new InvalidOperationException("Cells are missing."); }
        await using (var update = new NpgsqlCommand("UPDATE simops.simulation_batches SET status='completed',report=@report,summary=@summary,result_digest=@digest,completed_at=now() WHERE id=@batch", connection, transaction))
        {
            update.Parameters.AddWithValue("batch", job.BatchId); update.Parameters.AddWithValue("digest", report.ResultDigest);
            JsonParameter(update, "report", report);
            JsonParameter(update, "summary", report with { Cells = report.Cells.Select(c => c with { Runs = [] }).ToArray() });
            await update.ExecuteNonQueryAsync(token);
        }
        await SucceedSimulationJobAsync(connection, transaction, job.Id, token);
        await SetExperimentStateAsync(connection, transaction, job.ExperimentId, "analyzing", token);
        await AuditAsync(connection, transaction, job.ExperimentId, "metrics_ready", new { job.BatchId, report.ResultDigest, report.ReviewCandidateIds }, token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task FailSimulationJobAsync(ClaimedSimulationJob job, string error, CancellationToken token = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        if (!await OwnSimulationLeaseAsync(connection, transaction, job, token)) return;
        await using var update = new NpgsqlCommand("""
            UPDATE simops.simulation_jobs SET status=CASE WHEN attempts>=max_attempts THEN 'failed' ELSE 'queued' END,
            lock_token=NULL,locked_until=NULL,last_error=@error,available_at=now()+interval '2 seconds' WHERE id=@id RETURNING status
            """, connection, transaction);
        update.Parameters.AddWithValue("id", job.Id); update.Parameters.AddWithValue("error", error[..Math.Min(200, error.Length)]);
        if ((string)(await update.ExecuteScalarAsync(token))! == "failed")
            await StopBatchAsync(connection, transaction, job.BatchId, job.ExperimentId, "failed", token);
        await transaction.CommitAsync(token);
    }

    public async Task CancelSimulationBatchAsync(Guid id, CancellationToken token = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        string experimentId;
        await using (var command = new NpgsqlCommand("SELECT experiment_id,status FROM simops.simulation_batches WHERE id=@id FOR UPDATE", connection, transaction))
        {
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new ExperimentCommandException("BATCH_NOT_FOUND", "Batch not found.", 404);
            if (reader.GetString(1) == "cancelled") return;
            if (reader.GetString(1) is not ("queued" or "running")) throw new ExperimentCommandException("BATCH_TERMINAL", "Completed or failed batches cannot be cancelled.");
            experimentId = reader.GetString(0);
        }
        await StopBatchAsync(connection, transaction, id, experimentId, "cancelled", token);
        await transaction.CommitAsync(token);
    }

    private static async Task StopBatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid batchId, string experimentId, string status, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE simops.simulation_batches SET status=@status,completed_at=now() WHERE id=@batch;
            UPDATE simops.simulation_jobs SET status='cancelled',lock_token=NULL,locked_until=NULL WHERE batch_id=@batch AND status IN ('queued','running');
            """, connection, transaction);
        command.Parameters.AddWithValue("batch", batchId); command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync(token);
        await SetExperimentStateAsync(connection, transaction, experimentId, "failed", token);
        await AuditAsync(connection, transaction, experimentId, "batch_" + status, new { batchId }, token);
    }

    public async Task DecideExperimentAsync(string id, ExperimentDecisionRequest request, CancellationToken token = default)
    {
        if (request.Conclusion is not ("approved_candidate" or "rejected" or "rerun") || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000)
            throw new ExperimentCommandException("DECISION_INVALID", "A conclusion and a reason of 1-2000 characters are required.", 400);
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var experiment = await ReadExperimentAsync(connection, transaction, id, true, token);
        RequirePlan(experiment, request.PlanHash);
        if (experiment!.Decision == request) return;
        if (experiment.Status != "analyzing") throw new ExperimentCommandException("EXPERIMENT_STATE", "Only completed metrics can receive a decision.");
        await using (var query = new NpgsqlCommand("SELECT summary::text FROM simops.simulation_batches WHERE experiment_id=@id AND status='completed'", connection, transaction))
        {
            query.Parameters.AddWithValue("id", id);
            var report = JsonSerializer.Deserialize<ExperimentReport>((string)(await query.ExecuteScalarAsync(token))!, ExperimentJson.Options)!;
            if (report.ResultDigest != request.ResultDigest) throw new ExperimentCommandException("RESULT_CHANGED", "Decision must reference the displayed result digest.");
            if (request.Conclusion == "approved_candidate" ? request.SelectedVariantId is null || !report.ReviewCandidateIds.Contains(request.SelectedVariantId, StringComparer.Ordinal) : request.SelectedVariantId is not null)
                throw new ExperimentCommandException("CANDIDATE_INVALID", "Only a candidate that passed all guardrails can be selected.");
        }
        await using (var update = new NpgsqlCommand("UPDATE simops.experiments SET status='decided',decision=@decision WHERE id=@id", connection, transaction))
        { update.Parameters.AddWithValue("id", id); JsonParameter(update, "decision", request); await update.ExecuteNonQueryAsync(token); }
        await AuditAsync(connection, transaction, id, "human_decision", request, token);
        await transaction.CommitAsync(token);
    }

    private static async Task SucceedSimulationJobAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("UPDATE simops.simulation_jobs SET status='succeeded',lock_token=NULL,locked_until=NULL WHERE id=@id", connection, transaction);
        command.Parameters.AddWithValue("id", id); await command.ExecuteNonQueryAsync(token);
    }
    private static async Task SetExperimentStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string id, string status, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("UPDATE simops.experiments SET status=@status WHERE id=@id", connection, transaction);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("status", status); await command.ExecuteNonQueryAsync(token);
    }
    private static async Task AuditAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string id, string action, object payload, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("INSERT INTO simops.experiment_audit(experiment_id,action,payload) VALUES (@id,@action,@payload)", connection, transaction);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("action", action); JsonParameter(command, "payload", payload);
        await command.ExecuteNonQueryAsync(token);
    }
}
