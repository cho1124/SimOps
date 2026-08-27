using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SimOps.Agent.Core;
using SimOps.Game.Core;

namespace SimOps.Experiments;

public static class ExperimentRunner
{
    public const string CalculatorVersion = "difficulty-calculator-1.0.0";

    public static ExperimentReport Execute(ExperimentDefinition definition, Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Snapshot the caller's mutable collections before any progress callbacks or execution.
        definition = ExperimentJson.Parse(JsonSerializer.Serialize(definition, ExperimentJson.Options));
        var planHash = Hash(definition);
        var cells = new List<CellResult>();
        var firstSeed = ulong.Parse(definition.FirstSeed, CultureInfo.InvariantCulture);
        var bootstrapSeed = ulong.Parse(definition.BootstrapSeed, CultureInfo.InvariantCulture);
        var score = ScoreRule.CreateBaseline();
        foreach (var variant in definition.Variants)
        {
            var config = definition.CreateConfig(variant);
            foreach (var agentId in definition.AgentIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var agent = AgentFactory.CreateDefinitions().Single(agent => agent.Id == agentId);
                var metrics = new PersonaMetrics(agent, config.Rewards.Count);
                var runs = new List<RunEvidence>();
                var examples = new List<ReplayExample>();
                var entries = new int[6];
                var clears = new int[6];
                var actionCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var rewardCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var maximumTurnsReached = 0;
                for (var offset = 0; offset < definition.RunsPerCell; offset++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var seed = checked(firstSeed + (ulong)offset);
                    var run = SyntheticSimulation.Execute(config, score, agent, seed);
                    if (run.Result.Outcome is not (RunOutcome.Victory or RunOutcome.Defeat))
                        throw new InvalidOperationException($"Unexpected terminal outcome in {variant.Id}/{agentId}/{seed}.");
                    maximumTurnsReached += VerifyReplay(config, score, run);
                    metrics.Add(run);
                    foreach (var stage in run.Result.StageSummaries)
                    {
                        entries[stage.Stage - 1]++;
                        if (stage.Cleared) clears[stage.Stage - 1]++;
                    }
                    foreach (var pair in run.ActionCounts) Add(actionCounts, pair.Key.ToString(), pair.Value);
                    foreach (var pair in run.RewardCounts) Add(rewardCounts, pair.Key, pair.Value);
                    var result = run.Result;
                    runs.Add(new RunEvidence(seed.ToString(CultureInfo.InvariantCulture), result.Outcome.ToString().ToLowerInvariant(),
                        result.ClearedStages, result.TotalTurns, result.FinalHealth, result.MaxHealth, result.FinalScore,
                        result.ResultHash, Hash(run.Actions)));
                    if (examples.Count == 0 || (examples.Count == 1 && runs[0].Outcome != runs[^1].Outcome))
                        examples.Add(new ReplayExample(runs[^1].Seed, result.ResultHash, run.Actions));
                }
                var stages = Enumerable.Range(0, 6).Select(index => StageMetric.FromCounts(index + 1, entries[index], clears[index], runs.Count)).ToArray();
                cells.Add(new CellResult(variant.Id, agentId, agent.Version, config.Checksum, runs.Count,
                    metrics.ClearRate!.Value, PairedStatistics.CurveMae(runs, definition.TargetCumulativeFailureRates),
                    PairedStatistics.Describe(runs.Select(run => run.TotalTurns)), runs.Average(run => run.FinalHealth / (double)run.MaxHealth),
                    metrics.RewardEntropy, rewardCounts.Count == 0 ? null : rewardCounts.Values.Max() / (double?)rewardCounts.Values.Sum(),
                    maximumTurnsReached / (double)entries.Sum(), stages, actionCounts, rewardCounts, runs, examples, Hash(runs)));
                progress?.Invoke($"{variant.Id}/{agentId}: runs={runs.Count}, clear={metrics.ClearRate:P1}, config={config.Checksum[..12]}");
            }
        }
        var controlId = definition.Variants.Single(variant => variant.Role == "control").Id;
        var comparisons = new List<VariantComparison>();
        var treatments = definition.Variants.Where(variant => variant.Role == "treatment").ToArray();
        foreach (var treatment in treatments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            comparisons.Add(Compare(definition, cells, controlId, treatment.Id, bootstrapSeed));
        }
        var betweenTreatments = PairedStatistics.Bootstrap(Cell(cells, treatments[0].Id, "novice").Runs,
            Cell(cells, treatments[1].Id, "novice").Runs, definition.BootstrapReplicates, bootstrapSeed, definition.TargetCumulativeFailureRates);
        var candidates = comparisons.Where(comparison => comparison.EligibleForHumanReview)
            .OrderBy(comparison => Cell(cells, comparison.VariantId, "novice").CurveTargetMae)
            .ThenBy(comparison => comparison.VariantId, StringComparer.Ordinal).Select(comparison => comparison.VariantId).ToArray();
        var completed = cells.Sum(cell => cell.ValidRuns);
        return new ExperimentReport(definition.ExperimentId, planHash, CalculatorVersion,
            ArtifactHash(typeof(ExperimentRunner)), ArtifactHash(typeof(SyntheticSimulation)), ArtifactHash(typeof(GameSimulation)),
            definition, cells, comparisons, betweenTreatments, $"{treatments[1].Id} minus {treatments[0].Id}", candidates,
            completed, completed, 0, 0, Hash(new { planHash, cells, comparisons, betweenTreatments, candidates }));
    }

    private static VariantComparison Compare(ExperimentDefinition definition, List<CellResult> cells, string controlId, string treatmentId, ulong bootstrapSeed)
    {
        var rules = definition.DecisionRules;
        var novice = Cell(cells, treatmentId, "novice");
        var greedy = Cell(cells, treatmentId, "greedy");
        var primary = PairedStatistics.Bootstrap(Cell(cells, controlId, "novice").Runs, novice.Runs,
            definition.BootstrapReplicates, bootstrapSeed, definition.TargetCumulativeFailureRates);
        var checks = new List<GuardrailResult>();
        void Check(string key, double? value, bool passed, FormattableString requirement) =>
            checks.Add(new(key, passed, value, requirement.ToString(CultureInfo.InvariantCulture)));
        Check("novice.mae.improvement", primary.Difference, primary.Difference <= -rules.MinimumMaeImprovement, $"difference <= {-rules.MinimumMaeImprovement}");
        Check("novice.mae.ci", primary.Upper95, primary.Upper95 < 0, $"upper 95% bound < 0");
        Check("novice.clear", novice.ClearRate, novice.ClearRate >= rules.NoviceClearRateMinimum && novice.ClearRate <= rules.NoviceClearRateMaximum,
            $"{rules.NoviceClearRateMinimum} <= rate <= {rules.NoviceClearRateMaximum}");
        Check("novice.stage1", novice.Stages[0].ConditionalPassRate, novice.Stages[0].ConditionalPassRate >= rules.NoviceStage1PassRateMinimum, $">= {rules.NoviceStage1PassRateMinimum}");
        Check("novice.stage3.cumulative_failure", novice.Stages[2].CumulativeFailureRate, novice.Stages[2].CumulativeFailureRate <= rules.NoviceStage3CumulativeFailureMaximum,
            $"<= {rules.NoviceStage3CumulativeFailureMaximum}");
        var largestJump = StageMetric.LargestFailureJump(novice.Stages);
        Check("novice.adjacent_failure_jump", largestJump, largestJump <= rules.NoviceAdjacentConditionalFailureJumpMaximum,
            $"<= {rules.NoviceAdjacentConditionalFailureJumpMaximum}; undefined fails");
        Check("greedy.clear", greedy.ClearRate, greedy.ClearRate >= rules.GreedyClearRateMinimum, $">= {rules.GreedyClearRateMinimum}");
        Check("greedy_novice.clear_gap", greedy.ClearRate - novice.ClearRate, greedy.ClearRate - novice.ClearRate >= rules.GreedyNoviceClearRateGapMinimum,
            $">= {rules.GreedyNoviceClearRateGapMinimum}");
        var agentComparisons = new List<AgentComparison>();
        foreach (var agentId in definition.AgentIds)
        {
            var control = Cell(cells, controlId, agentId);
            var treatment = Cell(cells, treatmentId, agentId);
            var survivorTurns = PairedStatistics.PairedSurvivorTurns(control.Runs, treatment.Runs);
            var entropyRatio = control.RewardEntropy > 0 ? treatment.RewardEntropy / control.RewardEntropy : null;
            agentComparisons.Add(new AgentComparison(agentId,
                PairedStatistics.Bootstrap(control.Runs, treatment.Runs, definition.BootstrapReplicates, bootstrapSeed),
                survivorTurns.Count, survivorTurns.Ratio, entropyRatio));
            if (agentId is not ("random" or "novice" or "greedy"))
                Check($"{agentId}.clear", treatment.ClearRate, treatment.ClearRate >= rules.OtherNonRandomClearRateMinimum, $">= {rules.OtherNonRandomClearRateMinimum}");
            Check($"{agentId}.paired_survivor_turns", survivorTurns.Ratio, survivorTurns.Ratio <= rules.PairedSurvivorTurnsRatioMaximum, $"<= {rules.PairedSurvivorTurnsRatioMaximum}; empty fails");
            Check($"{agentId}.entropy_ratio", entropyRatio, entropyRatio >= rules.RewardEntropyRatioMinimum, $">= {rules.RewardEntropyRatioMinimum}; undefined fails");
            Check($"{agentId}.dominant_reward", treatment.DominantRewardShare, treatment.DominantRewardShare <= rules.DominantRewardShareMaximum, $"<= {rules.DominantRewardShareMaximum}; undefined fails");
            Check($"{agentId}.max_turn_rate", treatment.MaximumTurnReachedRate, treatment.MaximumTurnReachedRate <= rules.MaximumTurnReachedRateMaximum, $"<= {rules.MaximumTurnReachedRateMaximum}");
        }
        return new VariantComparison(treatmentId, primary, agentComparisons, checks, checks.All(check => check.Passed));
    }

    private static int VerifyReplay(GameConfig config, ScoreRule score, SyntheticRunRecord run)
    {
        var replay = new GameSimulation(config, score);
        replay.Reset(run.Context);
        var turnLimitEndings = 0;
        foreach (var action in run.Actions)
        {
            var step = replay.Apply(action);
            if (!step.Accepted) throw new InvalidOperationException($"Replay action rejected at seed {run.Seed}.");
            if (step.DomainEvents.Contains("run.turn-limit-defeat", StringComparer.Ordinal)) turnLimitEndings++;
        }
        if (replay.GetCanonicalResult().ResultHash != run.Result.ResultHash)
            throw new InvalidOperationException($"Replay hash mismatch at seed {run.Seed}.");
        return turnLimitEndings;
    }

    private static CellResult Cell(IEnumerable<CellResult> cells, string variant, string agent) => cells.Single(cell => cell.VariantId == variant && cell.AgentId == agent);
    private static void Add(IDictionary<string, int> counts, string key, int amount) => counts[key] = counts.TryGetValue(key, out var value) ? value + amount : amount;
    private static string Hash<T>(T value) => StableHash.Sha256Hex(JsonSerializer.Serialize(value, ExperimentJson.Options));
    private static string ArtifactHash(Type type) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(type.Assembly.Location)));
}
