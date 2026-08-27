using System.Text.Json;
using System.Globalization;
using SimOps.Experiments;
using SimOps.Game.Core;

var registered = ExperimentJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "registered-plan.json")));
var fixture = registered with { ExperimentId = "spec-fixture", RunsPerCell = 20, FirstSeed = "0", BootstrapReplicates = 100 };
var tests = new (string Name, Action Body)[] {
    ("EXP-CALC-001 definitions reject invalid contexts, duplicate cells and seed overflow", DefinitionValidation),
    ("EXP-CALC-002 variants change only registered attacks and preserve control", VariantIsolation),
    ("EXP-CALC-003 bootstrap preserves pair identity and known deltas", PairedBootstrap),
    ("EXP-CALC-004 cumulative curve MAE uses the starting cohort", CohortMetric),
    ("EXP-CALC-005 turn guardrail uses only paired survivors", SurvivorGuardrail),
    ("EXP-CALC-006 full fixture replays every run and reproduces the result digest", RepeatExecution),
    ("EXP-CALC-007 execution snapshots the definition and supports cancellation", SnapshotAndCancellation),
    ("EXP-CALC-008 unobserved stages remain undefined and cannot pass guardrails", MissingStage),
    ("EXP-CALC-009 culture does not change the experiment result digest", CultureIndependence),
};
var failed = 0;
foreach (var test in tests)
{
    try { test.Body(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"Experiment Specs: {tests.Length - failed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

void DefinitionValidation()
{
    Reject<ArgumentException>(() => (fixture with { FirstSeed = ulong.MaxValue.ToString() }).Validate());
    Reject<ArgumentException>(() => (fixture with { GameVersion = "other" }).Validate());
    Reject<ArgumentException>(() => (fixture with { RunsPerCell = 0 }).Validate());
    Reject<ArgumentException>(() => (fixture with { AgentIds = ["novice", "novice", "random", "greedy", "defensive", "explorer"] }).Validate());
    Reject<ArgumentException>(() => (fixture with { TargetCumulativeFailureRates = [0, .1, .05, .1, .2, .3] }).Validate());
    Reject<ArgumentException>(() => (fixture with { TargetCumulativeFailureRates = [0, 0, 0, 0, 0, double.NaN] }).Validate());
    Reject<ArgumentException>(() => (fixture with { DecisionRules = fixture.DecisionRules with { MinimumMaeImprovement = 0 } }).Validate());
    var duplicates = fixture.Variants.ToArray();
    duplicates[2] = duplicates[1];
    Reject<ArgumentException>(() => (fixture with { Variants = duplicates }).Validate());
    var json = JsonSerializer.Serialize(fixture, ExperimentJson.Options);
    Reject<JsonException>(() => ExperimentJson.Parse(json[..^1] + ",\"typoThreshold\":42}"));
    var missing = System.Text.Json.Nodes.JsonNode.Parse(json)!;
    missing["decisionRules"]!.AsObject().Remove("dominantRewardShareMaximum");
    Reject<JsonException>(() => ExperimentJson.Parse(missing.ToJsonString()));
}

void VariantIsolation()
{
    var baseline = GameConfig.CreateBaseline();
    Check(fixture.CreateConfig(fixture.Variants[0]).Checksum == baseline.Checksum, "Control was modified.");
    var expected = new[] { new[] { 4, 8, 9, 11, 12, 15 }, new[] { 4, 6, 9, 12, 15, 20 } };
    for (var variant = 1; variant <= 2; variant++)
    {
        var changed = fixture.CreateConfig(fixture.Variants[variant]);
        Check(changed.Checksum != baseline.Checksum, "Treatment reused baseline checksum.");
        for (var index = 0; index < 6; index++)
        {
            Check(changed.Encounters[index].AttackPower == expected[variant - 1][index], "Attack rounding differs from registration.");
            Check(changed.Encounters[index].MaxHealth == baseline.Encounters[index].MaxHealth &&
                changed.Encounters[index].HeavyAttackWeight == baseline.Encounters[index].HeavyAttackWeight, "Unexpected encounter change.");
        }
        Check(JsonSerializer.Serialize(changed.Rewards) == JsonSerializer.Serialize(baseline.Rewards), "Rewards changed.");
    }
    Check(GameConfig.CreateBaseline().Checksum == baseline.Checksum, "Baseline was mutated.");
}

void PairedBootstrap()
{
    var control = new[] { Run("0", 6), Run("1", 0) };
    var equal = PairedStatistics.Bootstrap(control, control, 100, 123);
    Check(equal.Difference == 0 && equal.Lower95 == 0 && equal.Upper95 == 0, "Identical pairs must have zero uncertainty in their difference.");
    var better = new[] { Run("0", 6), Run("1", 6) };
    var delta = PairedStatistics.Bootstrap(control, better, 100, 123);
    Check(delta.Difference == .5 && delta.Lower95 >= 0 && delta.Upper95 <= 1, "Known binary difference failed.");
    Check(delta == PairedStatistics.Bootstrap(control, better, 100, 123), "Bootstrap seed is not reproducible.");
    Reject<ArgumentException>(() => PairedStatistics.ValidatePairs(control, better.Reverse().ToArray()));
    Reject<ArgumentException>(() => PairedStatistics.ValidatePairs([Run("0", 6), Run("0", 6)], [Run("0", 6), Run("0", 6)]));
}

void CohortMetric()
{
    var runs = new[] { Run("0", 0), Run("1", 6) };
    Check(PairedStatistics.CurveMae(runs, [.5, .5, .5, .5, .5, .5]) == 0, "An early death disappeared from late-stage cumulative failures.");
    Check(Math.Abs(PairedStatistics.CurveMae(runs, [0, 0, 0, 0, 0, 0]) - .5) < 1e-12, "Curve error is not the average absolute cohort error.");
    var same = PairedStatistics.Bootstrap(runs, runs, 100, 42, fixture.TargetCumulativeFailureRates);
    Check(same.Lower95 == 0 && same.Upper95 == 0, "Identical nonlinear metric pairs changed.");
}

void SurvivorGuardrail()
{
    var control = new[] { Run("0", 6, 10), Run("1", 6, 100) };
    var treatment = new[] { Run("0", 6, 12), Run("1", 0, 1) };
    var result = PairedStatistics.PairedSurvivorTurns(control, treatment);
    Check(result.Count == 1 && result.Ratio == 1.2, "Early failure incorrectly improved duration.");
    Check(PairedStatistics.PairedSurvivorTurns(control, [Run("0", 0), Run("1", 0)]).Ratio is null, "Empty survivor intersection became zero.");
}

void RepeatExecution()
{
    var first = ExperimentRunner.Execute(fixture);
    var second = ExperimentRunner.Execute(fixture);
    Check(first.CompletedRuns == 360 && first.ReplayCheckedRuns == 360 && first.Cells.Count == 18, "Cell completion count changed.");
    Check(first.ResultDigest == second.ResultDigest, "Repeated experiment digest changed.");
    Check(first.PlanHash == second.PlanHash && first.Cells.All(cell => cell.Runs.Count == 20), "Plan or sample count changed.");
    foreach (var cell in first.Cells)
    {
        Check(cell.Stages.Sum(stage => stage.Failures) + cell.Runs.Count(run => run.ClearedStages == 6) == cell.ValidRuns, "Funnel conservation failed.");
        Check(cell.Stages[0].Entries == cell.ValidRuns, "Stage 1 entry count differs from cohort.");
        for (var index = 1; index < 6; index++) Check(cell.Stages[index].Entries == cell.Stages[index - 1].Clears, "Stage entry conservation failed.");
    }
    Check(first.PublicationState.StartsWith("not_published", StringComparison.Ordinal), "Experiment published a config.");
    Check(first.Comparisons.All(comparison => comparison.EligibleForHumanReview == comparison.Checks.All(check => check.Passed)), "Approval bypassed a guardrail.");
}

void SnapshotAndCancellation()
{
    var mutableTarget = fixture.TargetCumulativeFailureRates.ToArray();
    var mutable = fixture with { TargetCumulativeFailureRates = mutableTarget };
    var before = ExperimentRunner.Execute(fixture);
    var after = ExperimentRunner.Execute(mutable, _ => mutableTarget[5] = .9);
    Check(before.ResultDigest == after.ResultDigest, "Caller mutation changed a running experiment.");
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Reject<OperationCanceledException>(() => ExperimentRunner.Execute(fixture, cancellationToken: cancellation.Token));
}

void MissingStage()
{
    // Degenerate target is still a valid profile; unsupported/missing observations must never become a pass rate of zero.
    var stage = StageMetric.FromCounts(6, 0, 0, 10);
    Check(stage.ConditionalPassRate is null && stage.CumulativeFailureRate == 1 && stage.UndefinedReason == "NO_STAGE_ENTRIES", "No-entry semantics changed.");
    var stages = Enumerable.Range(1, 6).Select(index => StageMetric.FromCounts(index, index == 1 ? 10 : 0, 0, 10)).ToArray();
    Check(StageMetric.LargestFailureJump(stages) is null, "Missing stage observations became a measurable failure jump.");
    Check(!(StageMetric.LargestFailureJump(stages) <= .15), "Undefined failure jump passed a guardrail.");
    Reject<ArgumentException>(() => StageMetric.FromCounts(3, 10, 11, 10));
}

static RunEvidence Run(string seed, int cleared, int turns = 10) => new(seed, cleared == 6 ? "victory" : "defeat", cleared, turns, 1, 1, 0, "hash", "actions");

void CultureIndependence()
{
    var original = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var english = ExperimentRunner.Execute(fixture);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var french = ExperimentRunner.Execute(fixture);
        Check(english.ResultDigest == french.ResultDigest, "Localized threshold strings changed the canonical result.");
    }
    finally { CultureInfo.CurrentCulture = original; }
}
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Reject<T>(Action body) where T : Exception
{
    try { body(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
