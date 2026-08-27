using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimOps.Agent.Core;
using SimOps.Game.Core;

namespace SimOps.Application;

public sealed record SubmittedAction(int Sequence, GameActionType ActionType, string? RewardId);

public sealed record RunSubmission(
    string IdempotencyKey,
    string AgentId,
    string AgentVersion,
    string GameVersion,
    string ConfigChecksum,
    string ScoreRuleVersion,
    string ScoreRuleChecksum,
    string BaseSeed,
    string ClientResultHash,
    IReadOnlyList<SubmittedAction> Actions);

public sealed record SubmissionReceipt(Guid RunId, string Status, bool Existing);

public sealed record VerifiedSummary(
    string Outcome,
    int ClearedStages,
    int TotalTurns,
    int FinalHealth,
    int MaxHealth,
    int FinalScore,
    string ResultHash);

public sealed record RunStatusResponse(
    Guid RunId,
    string Population,
    string Status,
    string? RejectionCode,
    VerifiedSummary? Result,
    int ActionCount,
    int EventCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt);

public sealed record ApiError(string Code, string Message, bool Retryable, string CorrelationId);

public sealed record RecordedEvent(int Sequence, string EventType, int Stage, int Turn, string PayloadJson);

public sealed record VerifiedStage(int Stage, string EncounterId, bool Cleared, int Turns);

public sealed record VerificationOutput(
    bool Verified,
    string? RejectionCode,
    VerifiedSummary? Summary,
    IReadOnlyList<VerifiedStage> Stages,
    IReadOnlyList<RecordedEvent> Events);

public sealed class SubmissionValidationException : Exception
{
    public SubmissionValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class SubmissionConflictException : Exception
{
    public SubmissionConflictException()
        : base("The idempotency key was already used with a different payload.")
    {
    }
}

public static class ContractJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public static class SubmissionValidator
{
    public const int MaximumActions = 10_000;

    public static void Validate(RunSubmission submission)
    {
        if (submission is null)
        {
            throw new SubmissionValidationException("SUBMISSION_REQUIRED", "A submission body is required.");
        }

        if (string.IsNullOrWhiteSpace(submission.IdempotencyKey) || submission.IdempotencyKey.Length > 128)
        {
            throw new SubmissionValidationException("IDEMPOTENCY_KEY_INVALID", "Idempotency key must contain 1-128 characters.");
        }

        if (submission.Actions is null || submission.Actions.Count == 0 || submission.Actions.Count > MaximumActions)
        {
            throw new SubmissionValidationException("ACTION_LOG_SIZE_INVALID", $"Action count must be between 1 and {MaximumActions}.");
        }

        if (!ulong.TryParse(submission.BaseSeed, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new SubmissionValidationException("SEED_INVALID", "Base seed must be an unsigned 64-bit integer string.");
        }

        if (string.IsNullOrWhiteSpace(submission.ClientResultHash) || submission.ClientResultHash.Length != 64)
        {
            throw new SubmissionValidationException("RESULT_HASH_INVALID", "Result hash must contain 64 characters.");
        }

        var knownAgent = AgentFactory.CreateDefinitions().Any(definition =>
            string.Equals(definition.Id, submission.AgentId, StringComparison.Ordinal) &&
            string.Equals(definition.Version, submission.AgentVersion, StringComparison.Ordinal));
        if (!knownAgent)
        {
            throw new SubmissionValidationException("AGENT_VERSION_UNKNOWN", "The agent definition is not supported.");
        }

        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();
        if (!string.Equals(submission.GameVersion, config.GameVersion, StringComparison.Ordinal) ||
            !string.Equals(submission.ConfigChecksum, config.Checksum, StringComparison.Ordinal) ||
            !string.Equals(submission.ScoreRuleVersion, scoreRule.Version, StringComparison.Ordinal) ||
            !string.Equals(submission.ScoreRuleChecksum, scoreRule.Checksum, StringComparison.Ordinal))
        {
            throw new SubmissionValidationException("VERSION_MISMATCH", "The submitted game, config, or score version is not supported.");
        }

        for (var index = 0; index < submission.Actions.Count; index++)
        {
            var action = submission.Actions[index];
            if (action is null || action.Sequence != index)
            {
                throw new SubmissionValidationException("ACTION_SEQUENCE_INVALID", "Action sequences must be contiguous from zero.");
            }

            if (!Enum.IsDefined(action.ActionType) || action.RewardId?.Length > 128)
            {
                throw new SubmissionValidationException("ACTION_SCHEMA_INVALID", "Action type or reward ID is invalid.");
            }
        }
    }
}
