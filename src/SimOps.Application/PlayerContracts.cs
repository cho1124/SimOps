using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimOps.Game.Core;

namespace SimOps.Application;

public sealed record RegisterPlayerRequest(string RequestedNickname);
public sealed record PlayerCredential(Guid PlayerId, string Credential, string NormalizedNickname);
public sealed record SeasonInfo(Guid SeasonId, string Name, string Status, string GameVersion,
    string GameCoreChecksum, string ConfigChecksum, string ScoreRuleVersion, string ScoreRuleChecksum,
    DateTimeOffset StartsAt, DateTimeOffset? EndsAt);
public sealed record BeginRunRequest(Guid SeasonId, string ClientGameCoreChecksum, string IdempotencyKey);
public sealed record TicketClaims(Guid TicketId, Guid PlayerId, Guid SeasonId, string GameVersion,
    string GameCoreChecksum, string ConfigChecksum, string ScoreRuleVersion, string ScoreRuleChecksum,
    string BaseSeed, string Nonce, DateTimeOffset ExpiresAt);
public sealed record BeginRunResponse(Guid RunId, string RunTicket, TicketClaims Context, DateTimeOffset ExpiresAt);
public sealed record HumanRunSubmission(string RunTicket, string IdempotencyKey, string ClientGameCoreChecksum,
    int ActionLogSchemaVersion, string ClientResultHash, IReadOnlyList<SubmittedAction> Actions);
public sealed record LeaderboardEntry(long Rank, Guid PlayerId, string Nickname, Guid RunId,
    int Score, int ClearedStages, int TotalTurns, int FinalHealth, int MaxHealth, DateTimeOffset VerifiedAt);
public sealed record LeaderboardResponse(Guid SeasonId, string Status, long TotalPlayers,
    IReadOnlyList<LeaderboardEntry> Entries, LeaderboardEntry? CurrentPlayer,
    string RankingRule = "score desc, stages desc, turns asc, health ratio desc, verified time asc, player id asc");

public sealed class PlayerAccessException : Exception
{
    public PlayerAccessException() : base("A valid player credential is required.") { }
}

public static class CoreArtifact
{
    public static readonly string Checksum = Convert.ToHexStringLower(
        SHA256.HashData(File.ReadAllBytes(typeof(GameSimulation).Assembly.Location)));
}

public sealed class RunTicketSigner
{
    private readonly byte[] _key;

    public RunTicketSigner(string secret)
    {
        _key = Encoding.UTF8.GetBytes(secret);
        if (_key.Length < 32) throw new ArgumentException("Ticket signing key must contain at least 32 bytes.", nameof(secret));
    }

    public string Sign(TicketClaims claims)
    {
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(claims, ContractJson.Options));
        return payload + "." + Encode(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(payload)));
    }

    // Expiry is checked in the transaction after successful duplicate lookup, so lost responses remain retryable.
    public TicketClaims Verify(string ticket)
    {
        try
        {
            if (string.IsNullOrEmpty(ticket) || ticket.Length > 8192) throw new FormatException();
            var parts = ticket.Split('.');
            if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(
                    HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(parts[0])), Decode(parts[1])))
                throw new FormatException();
            return JsonSerializer.Deserialize<TicketClaims>(Decode(parts[0]), ContractJson.Options)
                ?? throw new FormatException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new SubmissionValidationException("TICKET_INVALID", "Ticket format or signature is invalid.");
        }
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='));
    }
}
