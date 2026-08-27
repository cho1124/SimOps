using System.Text.Json;
using Npgsql;
using SimOps.Application;
using SimOps.Experiments;

namespace SimOps.Infrastructure;

public sealed partial class PostgresRunStore
{
    public async Task<Guid> StartAnalysisAsync(string experimentId, StartAnalysisRequest request, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            throw new ExperimentCommandException("KEY_INVALID", "A bounded idempotency key is required.", 400);
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(721047)", token);
        // Read only a completed, immutable summary. No raw actions/player data enters the model boundary.
        string? summary;
        await using (var result = new NpgsqlCommand("SELECT summary::text FROM simops.simulation_batches WHERE experiment_id=@id AND status='completed'", connection, transaction))
        {
            result.Parameters.AddWithValue("id", experimentId);
            summary = await result.ExecuteScalarAsync(token) as string;
        }
        if (summary is null) throw new ExperimentCommandException("ANALYSIS_NOT_READY", "A completed experiment is required.");
        var report = JsonSerializer.Deserialize<ExperimentReport>(summary, ExperimentJson.Options)!;
        if (request.PlanHash != report.PlanHash || request.ResultDigest != report.ResultDigest)
            throw new ExperimentCommandException("ANALYSIS_RESULT_CHANGED", "Refresh the experiment result before requesting analysis.");
        var snapshot = AnalysisEvidence.CreateSnapshot(report);
        var hash = AnalysisEvidence.SnapshotHash(snapshot);
        await using (var prior = new NpgsqlCommand("SELECT id,experiment_id,snapshot_hash FROM simops.analysis_jobs WHERE idempotency_key=@key", connection, transaction))
        {
            prior.Parameters.AddWithValue("key", request.IdempotencyKey);
            await using var reader = await prior.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                if (reader.GetString(1) != experimentId || reader.GetString(2) != hash)
                    throw new ExperimentCommandException("IDEMPOTENCY_CONFLICT", "Key belongs to another analysis input.");
                return reader.GetGuid(0);
            }
        }
        await using (var capacity = new NpgsqlCommand("SELECT count(*) FROM simops.analysis_jobs WHERE status IN ('queued','running')", connection, transaction))
            if ((long)(await capacity.ExecuteScalarAsync(token))! >= 2)
                throw new ExperimentCommandException("ANALYSIS_CAPACITY", "Two analyses are already active.", 429);
        await using (var history = new NpgsqlCommand("SELECT count(*) FROM simops.analysis_jobs WHERE experiment_id=@id", connection, transaction))
        {
            history.Parameters.AddWithValue("id", experimentId);
            if ((long)(await history.ExecuteScalarAsync(token))! >= 10)
                throw new ExperimentCommandException("ANALYSIS_LIMIT", "Local demo limit: ten analyses per experiment.", 429);
        }
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand("INSERT INTO simops.analysis_jobs(id,experiment_id,idempotency_key,snapshot_hash,snapshot) VALUES (@id,@experiment,@key,@hash,@snapshot)", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("experiment", experimentId);
        command.Parameters.AddWithValue("key", request.IdempotencyKey);
        command.Parameters.AddWithValue("hash", hash);
        JsonParameter(command, "snapshot", snapshot);
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return id;
    }

    public async Task<IReadOnlyList<AnalysisJob>> GetAnalysesAsync(string experimentId, CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT id,status,attempts,last_error,snapshot_hash,snapshot::text,report::text,created_at FROM simops.analysis_jobs WHERE experiment_id=@id ORDER BY created_at DESC,id LIMIT 10");
        command.Parameters.AddWithValue("id", experimentId);
        await using var reader = await command.ExecuteReaderAsync(token);
        var jobs = new List<AnalysisJob>();
        while (await reader.ReadAsync(token)) jobs.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4),
            JsonSerializer.Deserialize<MetricSnapshot>(reader.GetString(5), ExperimentJson.Options)!,
            reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<AnalysisReport>(reader.GetString(6), ExperimentJson.Options),
            reader.GetFieldValue<DateTimeOffset>(7)));
        return jobs;
    }

    public async Task<ClaimedAnalysisJob?> ClaimAnalysisAsync(CancellationToken token = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await ExecuteAsync(connection, transaction, """
            UPDATE simops.analysis_jobs SET status='failed',lock_token=NULL,lease_until=NULL,last_error='ANALYSIS_ATTEMPTS_EXHAUSTED'
            WHERE attempts>=3 AND ((status='running' AND lease_until<now()) OR status='queued');
            """, token);
        var lockToken = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            WITH next AS (
                SELECT id FROM simops.analysis_jobs WHERE attempts<3 AND
                ((status='queued' AND available_at<=now()) OR (status='running' AND lease_until<now()))
                ORDER BY created_at,id FOR UPDATE SKIP LOCKED LIMIT 1
            ) UPDATE simops.analysis_jobs j SET status='running',attempts=attempts+1,lock_token=@lock,lease_until=now()+interval '30 seconds'
            FROM next WHERE j.id=next.id RETURNING j.id,j.snapshot::text,j.snapshot_hash
            """, connection, transaction);
        command.Parameters.AddWithValue("lock", lockToken);
        ClaimedAnalysisJob? claimed = null;
        await using (var reader = await command.ExecuteReaderAsync(token))
            if (await reader.ReadAsync(token)) claimed = new(reader.GetGuid(0), lockToken,
                JsonSerializer.Deserialize<MetricSnapshot>(reader.GetString(1), ExperimentJson.Options)!, reader.GetString(2));
        await transaction.CommitAsync(token);
        return claimed;
    }

    public async Task<bool> HeartbeatAnalysisAsync(ClaimedAnalysisJob job, CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand("UPDATE simops.analysis_jobs SET lease_until=now()+interval '30 seconds' WHERE id=@id AND lock_token=@lock AND status='running' AND lease_until>now()");
        command.Parameters.AddWithValue("id", job.Id); command.Parameters.AddWithValue("lock", job.LockToken);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    public async Task<bool> CompleteAnalysisAsync(ClaimedAnalysisJob job, ProviderAnalysis output, CancellationToken token = default)
    {
        if (AnalysisEvidence.SnapshotHash(job.Snapshot) != job.SnapshotHash)
            throw new AnalysisValidationException("ANALYSIS_SNAPSHOT_CHANGED");
        var validated = AnalysisEvidence.Validate(job.Snapshot, output);
        await using var command = _dataSource.CreateCommand("""
            UPDATE simops.analysis_jobs SET status='succeeded',report=@report,lock_token=NULL,lease_until=NULL,last_error=NULL
            WHERE id=@id AND lock_token=@lock AND status='running' AND lease_until>now() AND snapshot_hash=@hash
            """);
        command.Parameters.AddWithValue("id", job.Id); command.Parameters.AddWithValue("lock", job.LockToken);
        command.Parameters.AddWithValue("hash", job.SnapshotHash); JsonParameter(command, "report", validated);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    public async Task FailAnalysisAsync(ClaimedAnalysisJob job, string code, bool retryable, CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand("""
            UPDATE simops.analysis_jobs SET status=CASE WHEN attempts<3 AND @retry THEN 'queued' ELSE 'failed' END,
                available_at=now()+interval '2 seconds',lock_token=NULL,lease_until=NULL,last_error=@code
            WHERE id=@id AND lock_token=@lock AND status='running' AND lease_until>now()
            """);
        command.Parameters.AddWithValue("id", job.Id); command.Parameters.AddWithValue("lock", job.LockToken);
        command.Parameters.AddWithValue("retry", retryable); command.Parameters.AddWithValue("code", code);
        await command.ExecuteNonQueryAsync(token);
    }
}
