using SimOps.Experiments;

namespace SimOps.Application;

public sealed record SaveExperimentRequest(ExperimentDefinition Definition, int ExpectedRevision = 0);
public sealed record ExperimentCommandRequest(string PlanHash);
public sealed record StartBatchRequest(string PlanHash, string IdempotencyKey);
public sealed record ExperimentDecisionRequest(string PlanHash, string ResultDigest, string Conclusion,
    string? SelectedVariantId, string Reason);
public sealed record ExperimentListItem(string Id, string Status, int Revision, string PlanHash, DateTimeOffset CreatedAt);
public sealed record ExperimentDetail(string Id, string Status, int Revision, string PlanHash, ExperimentDefinition Definition,
    BatchProgress? Batch, ExperimentDecisionRequest? Decision);
public sealed record BatchProgress(Guid Id, string Status, int ExpectedCells, int CompletedCells, int ExpectedRuns,
    int CompletedRuns, IReadOnlyList<SimulationJobProgress> Jobs, string? ResultDigest);
public sealed record SimulationJobProgress(string Kind, string? VariantId, string? AgentId, string Status, int Attempts, string? LastError);
public sealed record ClaimedSimulationJob(Guid Id, Guid BatchId, string ExperimentId, string Kind, string? VariantId,
    string? AgentId, Guid LockToken, string ExecutionFingerprint, ExperimentDefinition Definition);
public sealed class ExperimentCommandException(string code, string message, int statusCode = 409) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
