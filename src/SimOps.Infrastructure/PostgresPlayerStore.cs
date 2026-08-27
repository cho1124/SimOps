using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SimOps.Application;
using SimOps.Game.Core;

namespace SimOps.Infrastructure;

public sealed partial class PostgresRunStore
{
    public static readonly Guid BaselineSeasonId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    public async Task<PlayerCredential> RegisterPlayerAsync(RegisterPlayerRequest request, CancellationToken token = default)
    {
        var nickname = request.RequestedNickname?.Trim().Normalize() ?? "";
        if (nickname.Length is < 1 or > 24 || nickname.Any(character => !char.IsLetterOrDigit(character) && character is not ' ' and not '_' and not '-'))
            throw new SubmissionValidationException("NICKNAME_INVALID", "Nickname must contain 1-24 letters, digits, spaces, underscores or hyphens.");
        var id = Guid.NewGuid();
        var credential = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO simops.human_players(id,nickname,credential_hash) VALUES (@id,@name,@hash)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", nickname);
        command.Parameters.AddWithValue("hash", StableHash.Sha256Hex(credential));
        await command.ExecuteNonQueryAsync(token);
        return new PlayerCredential(id, credential, nickname);
    }

    public async Task<Guid> AuthenticatePlayerAsync(string credential, CancellationToken token = default)
    {
        if (credential.Length != 64) throw new PlayerAccessException();
        await using var command = _dataSource.CreateCommand(
            "SELECT id FROM simops.human_players WHERE credential_hash=@hash AND status='active'");
        command.Parameters.AddWithValue("hash", StableHash.Sha256Hex(credential));
        return await command.ExecuteScalarAsync(token) is Guid id ? id : throw new PlayerAccessException();
    }

    public async Task<SeasonInfo?> GetActiveSeasonAsync(CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT id,name,status,game_version,game_core_checksum,config_checksum,score_rule_version,score_rule_checksum,starts_at,ends_at " +
            "FROM simops.seasons WHERE status='active' AND starts_at<=now() AND (ends_at IS NULL OR ends_at>now())");
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadSeason(reader) : null;
    }

    public async Task<BeginRunResponse> BeginHumanRunAsync(Guid playerId, BeginRunRequest request, RunTicketSigner signer, CancellationToken token = default)
    {
        ValidateKey(request.IdempotencyKey);
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var keyLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", connection, transaction))
        {
            keyLock.Parameters.AddWithValue("key", $"begin:{playerId:N}:{request.IdempotencyKey}");
            await keyLock.ExecuteNonQueryAsync(token);
        }
        await using (var existing = new NpgsqlCommand("SELECT claims::text FROM simops.run_tickets WHERE player_id=@player AND begin_key=@key", connection, transaction))
        {
            existing.Parameters.AddWithValue("player", playerId);
            existing.Parameters.AddWithValue("key", request.IdempotencyKey);
            if (await existing.ExecuteScalarAsync(token) is string json)
            {
                var previous = JsonSerializer.Deserialize<TicketClaims>(json, ContractJson.Options)!;
                if (previous.SeasonId != request.SeasonId || previous.GameCoreChecksum != request.ClientGameCoreChecksum) throw new SubmissionConflictException();
                return new BeginRunResponse(previous.TicketId, signer.Sign(previous), previous, previous.ExpiresAt);
            }
        }
        SeasonInfo season;
        await using (var select = new NpgsqlCommand(
            "SELECT id,name,status,game_version,game_core_checksum,config_checksum,score_rule_version,score_rule_checksum,starts_at,ends_at " +
            "FROM simops.seasons WHERE id=@id AND status='active' AND starts_at<=now() AND (ends_at IS NULL OR ends_at>now()) FOR SHARE", connection, transaction))
        {
            select.Parameters.AddWithValue("id", request.SeasonId);
            await using var reader = await select.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new SubmissionValidationException("SEASON_NOT_ACTIVE", "An active season is required.");
            season = ReadSeason(reader);
        }
        if (request.ClientGameCoreChecksum != season.GameCoreChecksum)
            throw new SubmissionValidationException("CHECKSUM_MISMATCH", "Client Game Core does not match the season.");
        var expires = DateTimeOffset.UtcNow.AddHours(2);
        if (season.EndsAt < expires) expires = season.EndsAt.Value;
        // PostgreSQL timestamps have microsecond precision. Keep signed/DB expiry values identical.
        expires = new DateTimeOffset(expires.Ticks - expires.Ticks % 10, TimeSpan.Zero);
        var claims = new TicketClaims(Guid.NewGuid(), playerId, season.SeasonId, season.GameVersion,
            season.GameCoreChecksum, season.ConfigChecksum, season.ScoreRuleVersion, season.ScoreRuleChecksum,
            BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)).ToString(CultureInfo.InvariantCulture),
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), expires);
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO simops.run_tickets(id,player_id,season_id,begin_key,claims,expires_at) VALUES (@id,@player,@season,@key,@claims,@expires)", connection, transaction))
        {
            insert.Parameters.AddWithValue("id", claims.TicketId);
            insert.Parameters.AddWithValue("player", playerId);
            insert.Parameters.AddWithValue("season", claims.SeasonId);
            insert.Parameters.AddWithValue("key", request.IdempotencyKey);
            insert.Parameters.AddWithValue("claims", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(claims, ContractJson.Options));
            insert.Parameters.AddWithValue("expires", expires);
            await insert.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return new BeginRunResponse(claims.TicketId, signer.Sign(claims), claims, expires);
    }

    public async Task<SubmissionReceipt> SubmitHumanRunAsync(Guid playerId, HumanRunSubmission request, RunTicketSigner signer, CancellationToken token = default)
    {
        var claims = signer.Verify(request.RunTicket);
        if (claims.PlayerId != playerId) throw new PlayerAccessException();
        if (request.ClientGameCoreChecksum != claims.GameCoreChecksum)
            throw new SubmissionValidationException("CHECKSUM_MISMATCH", "Submitted Game Core checksum differs from the ticket.");
        if (request.ActionLogSchemaVersion != 1)
            throw new SubmissionValidationException("ACTION_SCHEMA_INVALID", "Only action log schema version 1 is supported.");
        var submission = new RunSubmission(request.IdempotencyKey, "", "", claims.GameVersion, claims.ConfigChecksum,
            claims.ScoreRuleVersion, claims.ScoreRuleChecksum, claims.BaseSeed, request.ClientResultHash, request.Actions);
        SubmissionValidator.Validate(submission, requireAgent: false, registeredConfig: await LoadRegisteredConfigAsync(claims.ConfigChecksum, token));
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        bool used;
        bool expired;
        await using (var ticket = new NpgsqlCommand(
            "SELECT claims::text, used_at IS NOT NULL, expires_at<=now() FROM simops.run_tickets WHERE id=@id AND player_id=@player FOR UPDATE", connection, transaction))
        {
            ticket.Parameters.AddWithValue("id", claims.TicketId);
            ticket.Parameters.AddWithValue("player", playerId);
            await using var reader = await ticket.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) || JsonSerializer.Deserialize<TicketClaims>(reader.GetString(0), ContractJson.Options) != claims)
                throw new SubmissionValidationException("TICKET_INVALID", "Ticket does not match its issued context.");
            used = reader.GetBoolean(1);
            expired = reader.GetBoolean(2);
        }
        if (used)
        {
            await using var previous = new NpgsqlCommand("SELECT idempotency_key,request_hash,status FROM simops.runs WHERE ticket_id=@id", connection, transaction);
            previous.Parameters.AddWithValue("id", claims.TicketId);
            await using var reader = await previous.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Used ticket has no run.");
            if (reader.GetString(0) != $"human:{playerId:N}:{submission.IdempotencyKey}")
                throw new SubmissionValidationException("TICKET_REUSED", "Ticket has already been submitted.");
            if (reader.GetString(1) != StableHash.Sha256Hex(JsonSerializer.Serialize(submission, ContractJson.Options)))
                throw new SubmissionConflictException();
            return new SubmissionReceipt(claims.TicketId, reader.GetString(2), true);
        }
        if (expired || claims.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new SubmissionValidationException("TICKET_EXPIRED", "Ticket submission deadline has passed.");
        await using (var season = new NpgsqlCommand(
            "SELECT id FROM simops.seasons WHERE id=@id AND status='active' AND starts_at<=now() AND (ends_at IS NULL OR ends_at>now()) FOR SHARE", connection, transaction))
        {
            season.Parameters.AddWithValue("id", claims.SeasonId);
            if (await season.ExecuteScalarAsync(token) is null)
                throw new SubmissionValidationException("SEASON_NOT_ACTIVE", "Season is closed for submission.");
        }
        var receipt = await InsertRunAsync(connection, transaction, submission, claims, token);
        if (receipt.RunId != claims.TicketId) throw new SubmissionConflictException();
        await using (var consume = new NpgsqlCommand("UPDATE simops.run_tickets SET used_at=now() WHERE id=@id", connection, transaction))
        {
            consume.Parameters.AddWithValue("id", claims.TicketId);
            await consume.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return receipt;
    }

    public async Task<bool> PlayerOwnsRunAsync(Guid playerId, Guid runId, CancellationToken token = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT EXISTS(SELECT 1 FROM simops.runs WHERE id=@run AND player_id=@player)");
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("player", playerId);
        return (bool)(await command.ExecuteScalarAsync(token))!;
    }

    public async Task<LeaderboardResponse?> GetLeaderboardAsync(Guid seasonId, Guid? playerId, bool around, int offset, int limit, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new SubmissionValidationException("PAGE_INVALID", "offset >= 0 and limit 1-100 are required.");
        if (around && playerId is null) throw new PlayerAccessException();
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token);
        string status;
        await using (var season = new NpgsqlCommand("SELECT CASE WHEN ends_at<=now() THEN 'closed' ELSE status END FROM simops.seasons WHERE id=@id", connection, transaction))
        {
            season.Parameters.AddWithValue("id", seasonId);
            if (await season.ExecuteScalarAsync(token) is not string value) return null;
            status = value;
        }
        const string ranking = """
            WITH ranked AS (
              SELECT row_number() OVER (ORDER BY e.score DESC,e.cleared_stages DESC,e.total_turns,
                e.health_ratio DESC,e.verified_at,e.player_id) AS rank,
                e.player_id,p.nickname,e.run_id,e.score,e.cleared_stages,e.total_turns,e.final_health,e.max_health,e.verified_at
              FROM simops.leaderboard_entries e JOIN simops.human_players p ON p.id=e.player_id WHERE e.season_id=@season
            )
            """;
        LeaderboardEntry? current = null;
        if (playerId is not null)
        {
            await using var me = new NpgsqlCommand(ranking + "SELECT * FROM ranked WHERE player_id=@player", connection, transaction);
            me.Parameters.AddWithValue("season", seasonId);
            me.Parameters.AddWithValue("player", playerId.Value);
            await using var reader = await me.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) current = ReadEntry(reader);
        }
        if (around && current is not null) offset = (int)Math.Max(0, current.Rank - 1 - limit / 2);
        var entries = new List<LeaderboardEntry>();
        await using (var query = new NpgsqlCommand(ranking + "SELECT * FROM ranked ORDER BY rank OFFSET @offset LIMIT @limit", connection, transaction))
        {
            query.Parameters.AddWithValue("season", seasonId);
            query.Parameters.AddWithValue("offset", offset);
            query.Parameters.AddWithValue("limit", limit);
            await using var reader = await query.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) entries.Add(ReadEntry(reader));
        }
        await using var count = new NpgsqlCommand("SELECT count(*) FROM simops.leaderboard_entries WHERE season_id=@season", connection, transaction);
        count.Parameters.AddWithValue("season", seasonId);
        var total = (long)(await count.ExecuteScalarAsync(token))!;
        await transaction.CommitAsync(token);
        return new LeaderboardResponse(seasonId, status, total, entries, current);
    }

    private static LeaderboardEntry ReadEntry(NpgsqlDataReader reader) => new(reader.GetInt64(0), reader.GetGuid(1),
        reader.GetString(2), reader.GetGuid(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetFieldValue<DateTimeOffset>(9));
    private static SeasonInfo ReadSeason(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9));

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new SubmissionValidationException("IDEMPOTENCY_KEY_INVALID", "Idempotency key must contain 1-128 characters.");
    }

    private static async Task SeedSeasonAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        var config = GameConfig.CreateBaseline();
        var score = ScoreRule.CreateBaseline();
        await using var command = new NpgsqlCommand(
            "INSERT INTO simops.seasons(id,name,status,game_version,game_core_checksum,config_checksum,score_rule_version,score_rule_checksum) " +
            "SELECT @id,'Local baseline','active',@game,@core,@config,@scoreVersion,@score WHERE NOT EXISTS(SELECT 1 FROM simops.seasons WHERE status='active') ON CONFLICT DO NOTHING", connection, transaction);
        command.Parameters.AddWithValue("id", BaselineSeasonId);
        command.Parameters.AddWithValue("game", config.GameVersion);
        command.Parameters.AddWithValue("core", CoreArtifact.Checksum);
        command.Parameters.AddWithValue("config", config.Checksum);
        command.Parameters.AddWithValue("scoreVersion", score.Version);
        command.Parameters.AddWithValue("score", score.Checksum);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task UpdateLeaderboardAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId, CancellationToken token)
    {
        // Lock the season against publication/closure until Run + leaderboard + Job completion commit together.
        await using (var season = new NpgsqlCommand(
            "SELECT s.id FROM simops.seasons s JOIN simops.runs r ON r.season_id=s.id WHERE r.id=@run " +
            "AND s.status='active' AND s.starts_at<=now() AND (s.ends_at IS NULL OR s.ends_at>now()) FOR SHARE OF s", connection, transaction))
        {
            season.Parameters.AddWithValue("run", runId);
            if (await season.ExecuteScalarAsync(token) is null) return;
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO simops.leaderboard_entries AS existing
                (season_id,player_id,run_id,score,cleared_stages,total_turns,final_health,max_health,verified_at)
            SELECT season_id,player_id,id,(result_json->>'finalScore')::integer,(result_json->>'clearedStages')::integer,
                (result_json->>'totalTurns')::integer,(result_json->>'finalHealth')::integer,(result_json->>'maxHealth')::integer,verified_at
            FROM simops.runs WHERE id=@run AND population='human' AND status='verified'
            ON CONFLICT(season_id,player_id) DO UPDATE SET run_id=excluded.run_id,score=excluded.score,
                cleared_stages=excluded.cleared_stages,total_turns=excluded.total_turns,final_health=excluded.final_health,
                max_health=excluded.max_health,verified_at=excluded.verified_at
            WHERE (excluded.score,excluded.cleared_stages,-excluded.total_turns,excluded.health_ratio)
                > (existing.score,existing.cleared_stages,-existing.total_turns,existing.health_ratio)
               OR ((excluded.score,excluded.cleared_stages,excluded.total_turns,excluded.health_ratio)
                = (existing.score,existing.cleared_stages,existing.total_turns,existing.health_ratio)
                AND excluded.verified_at < existing.verified_at)
            """, connection, transaction);
        command.Parameters.AddWithValue("run", runId);
        await command.ExecuteNonQueryAsync(token);
    }
}
