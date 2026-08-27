namespace SimOps.Application;

public sealed record PublishConfigRequest(string ExperimentId, string PlanHash, string ResultDigest,
    string VariantId, Guid ExpectedSeasonId, string Name, string Reason, string IdempotencyKey);
public sealed record RollbackConfigRequest(Guid TargetSeasonId, Guid ExpectedSeasonId, string Name, string Reason, string IdempotencyKey);
public sealed record Publication(Guid Id, string Kind, Guid PreviousSeasonId, Guid SeasonId, string ConfigChecksum,
    string? ExperimentId, string Reason, DateTimeOffset CreatedAt);
