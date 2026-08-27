using SimOps.Game.Core;

namespace SimOps.Experiments;

public sealed record RunEvidence(string Seed, string Outcome, int ClearedStages, int TotalTurns, int FinalHealth,
    int MaxHealth, int Score, string ResultHash, string ActionLogHash);
public sealed record StageMetric(int Stage, int Entries, int Clears, int Failures, double? ConditionalPassRate,
    double CumulativeFailureRate, string? UndefinedReason)
{
    public static StageMetric FromCounts(int stage, int entries, int clears, int cohortSize)
    {
        if (stage is < 1 or > 6 || cohortSize <= 0 || clears < 0 || entries < clears || entries > cohortSize)
            throw new ArgumentException("Funnel counts violate cohort conservation.");
        return new StageMetric(stage, entries, clears, entries - clears,
            entries == 0 ? null : (double?)clears / entries, (cohortSize - clears) / (double)cohortSize,
            entries == 0 ? "NO_STAGE_ENTRIES" : null);
    }

    public static double? LargestFailureJump(IReadOnlyList<StageMetric> stages)
    {
        if (stages.Count != 6 || stages.Any(stage => stage.ConditionalPassRate is null)) return null;
        if (stages.Where((stage, index) => stage.Stage != index + 1).Any()) throw new ArgumentException("Stages must be ordered 1-6.");
        return Enumerable.Range(1, 5).Max(index => Math.Max(0,
            stages[index - 1].ConditionalPassRate!.Value - stages[index].ConditionalPassRate!.Value));
    }
}
public sealed record Distribution(double Mean, double Median, double P10, double P90, double P95);
public sealed record ReplayExample(string Seed, string ResultHash, IReadOnlyList<GameAction> Actions);
public sealed record CellResult(string VariantId, string AgentId, string AgentVersion, string ConfigChecksum, int ValidRuns,
    double ClearRate, double CurveTargetMae, Distribution Turns, double MeanHealthRatio,
    double? RewardEntropy, double? DominantRewardShare, double MaximumTurnReachedRate,
    IReadOnlyList<StageMetric> Stages, IReadOnlyDictionary<string, int> ActionCounts,
    IReadOnlyDictionary<string, int> RewardCounts, IReadOnlyList<RunEvidence> Runs, IReadOnlyList<ReplayExample> Examples,
    string SampleHash);
public sealed record PairedEstimate(double Difference, double Lower95, double Upper95, int Pairs,
    string Method = "paired-seed percentile bootstrap; no multiplicity adjustment");
public sealed record GuardrailResult(string Key, bool Passed, double? Observed, string Requirement);
public sealed record AgentComparison(string AgentId, PairedEstimate ClearRateDifference, int PairedSurvivors,
    double? PairedSurvivorTurnsRatio, double? RewardEntropyRatio);
public sealed record VariantComparison(string VariantId, PairedEstimate NoviceMaeDifference,
    IReadOnlyList<AgentComparison> Agents, IReadOnlyList<GuardrailResult> Checks, bool EligibleForHumanReview);
public sealed record ExperimentReport(string ExperimentId, string PlanHash, string CalculatorVersion,
    string CalculatorArtifactHash, string AgentArtifactHash, string GameCoreArtifactHash,
    ExperimentDefinition Definition, IReadOnlyList<CellResult> Cells, IReadOnlyList<VariantComparison> Comparisons,
    PairedEstimate TreatmentMaeDifference, string TreatmentMaeDifferenceDirection,
    IReadOnlyList<string> ReviewCandidateIds, int CompletedRuns, int ReplayCheckedRuns, int InvalidTransitionCount,
    int ReplayMismatchCount, string ResultDigest, string PublicationState = "not_published; human approval required");

public static class PairedStatistics
{
    public static double CurveMae(IReadOnlyList<RunEvidence> runs, IReadOnlyList<double> targets)
    {
        if (runs.Count == 0 || targets.Count != 6) throw new ArgumentException("A nonempty sample and six targets are required.");
        var error = 0d;
        for (var stage = 1; stage <= 6; stage++)
            error += Math.Abs(runs.Count(run => run.ClearedStages < stage) / (double)runs.Count - targets[stage - 1]);
        return error / 6d;
    }

    public static void ValidatePairs(IReadOnlyList<RunEvidence> control, IReadOnlyList<RunEvidence> treatment)
    {
        if (control.Count == 0 || control.Count != treatment.Count ||
            control.Select(run => run.Seed).Distinct(StringComparer.Ordinal).Count() != control.Count ||
            control.Where((run, index) => run.Seed != treatment[index].Seed).Any())
            throw new ArgumentException("Paired samples must contain the same distinct, ordered seeds.");
    }

    public static PairedEstimate Bootstrap(IReadOnlyList<RunEvidence> control, IReadOnlyList<RunEvidence> treatment,
        int repetitions, ulong seed, IReadOnlyList<double>? curveTargets = null)
    {
        ValidatePairs(control, treatment);
        if (repetitions < 1) throw new ArgumentOutOfRangeException(nameof(repetitions));
        var random = new DeterministicRandom(seed);
        var differences = new double[repetitions];
        var controlFailures = new int[6];
        var treatmentFailures = new int[6];
        for (var iteration = 0; iteration < repetitions; iteration++)
        {
            Array.Clear(controlFailures);
            Array.Clear(treatmentFailures);
            var clearDifference = 0;
            for (var sample = 0; sample < control.Count; sample++)
            {
                var index = random.NextInt(control.Count);
                if (curveTargets is null)
                    clearDifference += (treatment[index].ClearedStages == 6 ? 1 : 0) - (control[index].ClearedStages == 6 ? 1 : 0);
                else
                {
                    for (var stage = 1; stage <= 6; stage++)
                    {
                        if (control[index].ClearedStages < stage) controlFailures[stage - 1]++;
                        if (treatment[index].ClearedStages < stage) treatmentFailures[stage - 1]++;
                    }
                }
            }
            if (curveTargets is null) differences[iteration] = clearDifference / (double)control.Count;
            else
                for (var stage = 0; stage < 6; stage++)
                    differences[iteration] += (Math.Abs(treatmentFailures[stage] / (double)control.Count - curveTargets[stage])
                        - Math.Abs(controlFailures[stage] / (double)control.Count - curveTargets[stage])) / 6d;
        }
        Array.Sort(differences);
        var observed = curveTargets is null
            ? (treatment.Count(run => run.ClearedStages == 6) - control.Count(run => run.ClearedStages == 6)) / (double)control.Count
            : CurveMae(treatment, curveTargets) - CurveMae(control, curveTargets);
        return new PairedEstimate(observed, Quantile(differences, 0.025), Quantile(differences, 0.975), control.Count);
    }

    public static (int Count, double? Ratio) PairedSurvivorTurns(IReadOnlyList<RunEvidence> control, IReadOnlyList<RunEvidence> treatment)
    {
        ValidatePairs(control, treatment);
        var count = 0;
        long controlTurns = 0, treatmentTurns = 0;
        for (var index = 0; index < control.Count; index++)
        {
            if (control[index].ClearedStages != 6 || treatment[index].ClearedStages != 6) continue;
            count++;
            controlTurns += control[index].TotalTurns;
            treatmentTurns += treatment[index].TotalTurns;
        }
        return (count, controlTurns == 0 ? null : treatmentTurns / (double?)controlTurns);
    }

    public static Distribution Describe(IEnumerable<int> values)
    {
        var sorted = values.Select(value => (double)value).Order().ToArray();
        if (sorted.Length == 0) throw new ArgumentException("Distribution requires observations.");
        return new Distribution(sorted.Average(), Quantile(sorted, .5), Quantile(sorted, .1), Quantile(sorted, .9), Quantile(sorted, .95));
    }

    private static double Quantile(double[] sorted, double probability)
    {
        var position = (sorted.Length - 1) * probability;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
