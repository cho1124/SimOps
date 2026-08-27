using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimOps.Experiments;

namespace SimOps.Application;

public sealed record AnalysisMetric(string Key, double? Value, string Unit, string DefinitionKey);
public sealed record AnalysisGuard(string VariantId, string Key, bool Passed, string MetricKey);
public sealed record MetricSnapshot(int SchemaVersion, string ExperimentId, string PlanHash, string ResultDigest,
    string CalculatorVersion, string CalculatorArtifactHash, string AgentArtifactHash, string GameCoreArtifactHash,
    IReadOnlyList<AnalysisMetric> Metrics, IReadOnlyList<AnalysisGuard> Guards, IReadOnlyList<string> ReviewCandidateIds);
public sealed record MetricObservation(string MetricKey, double Value);
public sealed record AnalysisInterpretation(string Code, IReadOnlyList<string> MetricKeys);
// No model-authored prose/numbers outside observations: interpretation is a bounded, versioned vocabulary.
public sealed record AnalysisOutput(int SchemaVersion, string Assessment, IReadOnlyList<MetricObservation> Observations,
    IReadOnlyList<AnalysisInterpretation> Hypotheses, IReadOnlyList<AnalysisInterpretation> NextExperiments);
public sealed record ProviderAnalysis(string Provider, string Model, string ModelDigest, string Json);
public interface IAnalysisProvider
{
    Task<ProviderAnalysis> AnalyzeAsync(MetricSnapshot snapshot, CancellationToken token);
}
public sealed record StartAnalysisRequest(string PlanHash, string ResultDigest, string IdempotencyKey);
public sealed record ClaimedAnalysisJob(Guid Id, Guid LockToken, MetricSnapshot Snapshot, string SnapshotHash);
public sealed record AnalysisReport(string Provider, string Model, string ModelDigest, string PromptVersion,
    string ValidationVersion, string SnapshotHash, string OutputHash, string ConclusionHash, AnalysisOutput Output);
public sealed record AnalysisJob(Guid Id, string Status, int Attempts, string? LastError, string SnapshotHash,
    MetricSnapshot Snapshot, AnalysisReport? Report, DateTimeOffset CreatedAt);
public sealed class AnalysisValidationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class AnalysisEvidence
{
    public const string PromptVersion = "bounded-analyst-1.0.0";
    public const string ValidationVersion = "evidence-validator-1.0.0";
    public static string Hash<T>(T input) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, ExperimentJson.Options))));

    public static MetricSnapshot CreateSnapshot(ExperimentReport report)
    {
        var metrics = new List<AnalysisMetric>();
        void Add(string key, double? value, string unit, string definition) => metrics.Add(new(key, value, unit, definition));
        Add("experiment.completed_runs", report.CompletedRuns, "count", "valid-run-count");
        Add("experiment.replay_mismatches", report.ReplayMismatchCount, "count", "replay-mismatch-count");
        Add("experiment.review_candidates", report.ReviewCandidateIds.Count, "count", "review-candidate-count");
        foreach (var cell in report.Cells)
        {
            var prefix = $"cell/{cell.VariantId}/{cell.AgentId}";
            Add(prefix + "/clear_rate", cell.ClearRate, "ratio", "clear-rate");
            Add(prefix + "/curve_mae", cell.CurveTargetMae, "ratio", "curve-target-mae");
            if (cell.AgentId != "novice") continue;
            foreach (var stage in cell.Stages)
            {
                Add(prefix + $"/stage/{stage.Stage}/conditional_pass", stage.ConditionalPassRate, "ratio", "conditional-stage-pass-rate");
                Add(prefix + $"/stage/{stage.Stage}/cumulative_failure", stage.CumulativeFailureRate, "ratio", "cumulative-failure-rate");
            }
        }
        var guards = new List<AnalysisGuard>();
        foreach (var comparison in report.Comparisons)
        {
            var prefix = $"comparison/{comparison.VariantId}";
            Add(prefix + "/mae_delta", comparison.NoviceMaeDifference.Difference, "ratio_delta", "curve-target-mae");
            Add(prefix + "/mae_lower95", comparison.NoviceMaeDifference.Lower95, "ratio_delta", "paired-bootstrap-ci");
            Add(prefix + "/mae_upper95", comparison.NoviceMaeDifference.Upper95, "ratio_delta", "paired-bootstrap-ci");
            foreach (var guard in comparison.Checks)
            {
                var key = prefix + "/guard/" + guard.Key;
                Add(key, guard.Observed, "guard_observation", "guardrail-observation");
                guards.Add(new(comparison.VariantId, guard.Key, guard.Passed, key));
            }
        }
        return new(1, report.ExperimentId, report.PlanHash, report.ResultDigest, report.CalculatorVersion,
            report.CalculatorArtifactHash, report.AgentArtifactHash, report.GameCoreArtifactHash,
            metrics.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            guards.OrderBy(x => x.MetricKey, StringComparer.Ordinal).ToArray(),
            report.ReviewCandidateIds.Order(StringComparer.Ordinal).ToArray());
    }

    public static string SnapshotHash(MetricSnapshot snapshot) => Hash(snapshot with {
        Metrics = snapshot.Metrics.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
        Guards = snapshot.Guards.OrderBy(x => x.MetricKey, StringComparer.Ordinal).ToArray(),
        ReviewCandidateIds = snapshot.ReviewCandidateIds.Order(StringComparer.Ordinal).ToArray() });

    public static string Assessment(MetricSnapshot snapshot) => snapshot.ReviewCandidateIds.Count == 0 ? "no_candidates" : "review_candidates";

    // These are hypotheses, NOT causal findings. Only evidence-compatible choices are offered to the provider.
    public static IReadOnlyList<AnalysisInterpretation> AllowedHypotheses(MetricSnapshot snapshot)
    {
        var failed = snapshot.Guards.Where(x => !x.Passed && x.Key == "novice.adjacent_failure_jump")
            .Where(x => snapshot.Metrics.Single(m => m.Key == x.MetricKey).Value is not null).Select(x => x.MetricKey).ToArray();
        var result = new List<AnalysisInterpretation>();
        if (failed.Length > 0) result.Add(new("failure_concentration", failed));
        result.Add(new("policy_sensitivity", snapshot.Metrics.Where(x => x.Key.EndsWith("/clear_rate", StringComparison.Ordinal)).Select(x => x.Key).ToArray()));
        return result;
    }

    public static IReadOnlyList<AnalysisInterpretation> AllowedNextExperiments(MetricSnapshot snapshot) =>
        AllowedHypotheses(snapshot).Select(h => new AnalysisInterpretation(
            h.Code == "failure_concentration" ? "redistribute_pressure" : "replicate_seeds", h.MetricKeys)).ToArray();

    public static AnalysisReport Validate(MetricSnapshot snapshot, ProviderAnalysis provider)
    {
        if (provider.Json.Length > 24_000) throw new AnalysisValidationException("ANALYSIS_TOO_LARGE");
        AnalysisOutput output;
        try { output = JsonSerializer.Deserialize<AnalysisOutput>(provider.Json, ExperimentJson.Options)
            ?? throw new JsonException(); }
        catch (JsonException) { throw new AnalysisValidationException("ANALYSIS_SCHEMA_INVALID"); }
        if (output.SchemaVersion != 1 || output.Assessment != Assessment(snapshot))
            throw new AnalysisValidationException("ANALYSIS_CONCLUSION_INVALID");
        if (output.Observations is null || output.Observations.Count is < 1 or > 12 ||
            output.Observations.Any(x => x is null || x.MetricKey is null) ||
            output.Observations.Select(x => x.MetricKey).Distinct(StringComparer.Ordinal).Count() != output.Observations.Count)
            throw new AnalysisValidationException("ANALYSIS_SCHEMA_INVALID");
        var metrics = snapshot.Metrics.ToDictionary(x => x.Key, StringComparer.Ordinal);
        foreach (var observation in output.Observations)
            if (!metrics.TryGetValue(observation.MetricKey, out var metric) || metric.Value is null ||
                !double.IsFinite(observation.Value) || observation.Value != metric.Value.Value)
                throw new AnalysisValidationException("ANALYSIS_EVIDENCE_INVALID");
        ValidateInterpretations(output.Hypotheses, AllowedHypotheses(snapshot), metrics);
        ValidateInterpretations(output.NextExperiments, AllowedNextExperiments(snapshot), metrics);
        return new(provider.Provider, provider.Model, provider.ModelDigest, PromptVersion, ValidationVersion,
            SnapshotHash(snapshot), Hash(output), Hash(new { output.Assessment,
                hypotheses = output.Hypotheses.Select(x => x.Code).Order(StringComparer.Ordinal).ToArray(),
                nextExperiments = output.NextExperiments.Select(x => x.Code).Order(StringComparer.Ordinal).ToArray() }), output);
    }

    private static void ValidateInterpretations(IReadOnlyList<AnalysisInterpretation>? items,
        IReadOnlyList<AnalysisInterpretation> allowed, Dictionary<string, AnalysisMetric> metrics)
    {
        if (items is null || items.Count is < 1 or > 2 || items.Any(x => x is null || x.Code is null) ||
            items.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new AnalysisValidationException("ANALYSIS_SCHEMA_INVALID");
        foreach (var item in items)
        {
            var option = allowed.SingleOrDefault(x => x.Code == item.Code);
            if (option is null || item.MetricKeys is null || item.MetricKeys.Count is < 1 or > 6 ||
                item.MetricKeys.Distinct(StringComparer.Ordinal).Count() != item.MetricKeys.Count ||
                item.MetricKeys.Any(key => key is null || !option.MetricKeys.Contains(key, StringComparer.Ordinal) ||
                    !metrics.TryGetValue(key, out var metric) || metric.Value is null))
                throw new AnalysisValidationException("ANALYSIS_EVIDENCE_INVALID");
        }
    }
}
