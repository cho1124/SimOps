using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimOps.Agent.Core;
using SimOps.Game.Core;

namespace SimOps.Experiments;

public sealed record VariantDefinition(string Id, string Role, IReadOnlyList<int> AttackPercentByStage);
public sealed record DecisionRules(double MinimumMaeImprovement, double NoviceClearRateMinimum, double NoviceClearRateMaximum,
    double NoviceStage1PassRateMinimum, double NoviceStage3CumulativeFailureMaximum,
    double NoviceAdjacentConditionalFailureJumpMaximum, double GreedyClearRateMinimum,
    double GreedyNoviceClearRateGapMinimum, double OtherNonRandomClearRateMinimum,
    double PairedSurvivorTurnsRatioMaximum, double RewardEntropyRatioMinimum,
    double DominantRewardShareMaximum, double MaximumTurnReachedRateMaximum);
public sealed record ExperimentDefinition(int SchemaVersion, string ExperimentId, string Hypothesis, string GameVersion,
    string ControlConfigChecksum, string ScoreRuleChecksum, string AgentVersion, IReadOnlyList<string> AgentIds,
    int RunsPerCell, string FirstSeed, int BootstrapReplicates, string BootstrapSeed,
    IReadOnlyList<VariantDefinition> Variants, string PrimaryMetric, IReadOnlyList<double> TargetCumulativeFailureRates,
    DecisionRules DecisionRules)
{
    public const string SupportedPrimaryMetric = "novice_curve_target_mae.v1";

    public void Validate()
    {
        var control = GameConfig.CreateBaseline();
        if (SchemaVersion != 1 || PrimaryMetric != SupportedPrimaryMetric || GameVersion != control.GameVersion ||
            ControlConfigChecksum != control.Checksum || ScoreRuleChecksum != ScoreRule.CreateBaseline().Checksum)
            throw new ArgumentException("Unsupported schema, metric, game, config or score-rule context.");
        RequireId(ExperimentId);
        if (string.IsNullOrWhiteSpace(Hypothesis) || Hypothesis.Length > 4000) throw new ArgumentException("A bounded hypothesis is required.");
        if (RunsPerCell is < 1 or > 10000 || BootstrapReplicates is < 100 or > 10000)
            throw new ArgumentException("Runs/cell must be 1-10000; bootstrap repetitions must be 100-10000.");
        if (!ulong.TryParse(FirstSeed, NumberStyles.None, CultureInfo.InvariantCulture, out var firstSeed) ||
            firstSeed > ulong.MaxValue - (ulong)(RunsPerCell - 1) ||
            !ulong.TryParse(BootstrapSeed, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("Seed range is invalid or would overflow.");
        var supportedAgents = AgentFactory.CreateDefinitions();
        if (AgentIds is null || AgentIds.Count != supportedAgents.Count || AgentIds.Distinct(StringComparer.Ordinal).Count() != AgentIds.Count ||
            supportedAgents.Any(agent => agent.Version != AgentVersion || !AgentIds.Contains(agent.Id, StringComparer.Ordinal)))
            throw new ArgumentException("Exactly the six supported, distinct Agent definitions are required.");
        if (Variants is null || Variants.Count != 3 || Variants.Any(v => v is null) ||
            Variants.Select(v => v.Id).Distinct(StringComparer.Ordinal).Count() != Variants.Count ||
            Variants.Count(v => v.Role == "control") != 1 || Variants.Count(v => v.Role == "treatment") != 2)
            throw new ArgumentException("Exactly one control and two distinct treatment variants are required.");
        foreach (var variant in Variants)
        {
            RequireId(variant.Id);
            if (variant.AttackPercentByStage is null || variant.AttackPercentByStage.Count != 6 ||
                variant.AttackPercentByStage.Any(value => value is < 100 or > 300) || variant.AttackPercentByStage[0] != 100 ||
                (variant.Role == "control" && variant.AttackPercentByStage.Any(value => value != 100)))
                throw new ArgumentException("Six attack percentages 100-300 are required; Stage 1 and control must remain unchanged.");
        }
        if (TargetCumulativeFailureRates is null || TargetCumulativeFailureRates.Count != 6 ||
            TargetCumulativeFailureRates.Any(value => !double.IsFinite(value) || value < 0 || value > 1) ||
            TargetCumulativeFailureRates.Zip(TargetCumulativeFailureRates.Skip(1)).Any(pair => pair.First > pair.Second))
            throw new ArgumentException("Six nondecreasing target probabilities are required.");
        if (DecisionRules is null) throw new ArgumentException("Decision rules are required.");
        var rules = DecisionRules;
        var probabilities = new[] { rules.MinimumMaeImprovement, rules.NoviceClearRateMinimum, rules.NoviceClearRateMaximum,
            rules.NoviceStage1PassRateMinimum, rules.NoviceStage3CumulativeFailureMaximum,
            rules.NoviceAdjacentConditionalFailureJumpMaximum, rules.GreedyClearRateMinimum, rules.GreedyNoviceClearRateGapMinimum,
            rules.OtherNonRandomClearRateMinimum, rules.RewardEntropyRatioMinimum, rules.DominantRewardShareMaximum, rules.MaximumTurnReachedRateMaximum };
        if (probabilities.Any(value => !double.IsFinite(value) || value is < 0 or > 1) || rules.MinimumMaeImprovement <= 0 ||
            rules.NoviceClearRateMinimum > rules.NoviceClearRateMaximum || !double.IsFinite(rules.PairedSurvivorTurnsRatioMaximum) ||
            rules.PairedSurvivorTurnsRatioMaximum < 1 || rules.PairedSurvivorTurnsRatioMaximum > 3)
            throw new ArgumentException("Decision thresholds are invalid.");
    }

    public GameConfig CreateConfig(VariantDefinition variant)
    {
        var baseline = GameConfig.CreateBaseline();
        if (variant.Role == "control") return baseline;
        var encounters = baseline.Encounters.Select((encounter, index) => new EncounterDefinition(encounter.Id, encounter.Stage,
            encounter.MaxHealth, checked((encounter.AttackPower * variant.AttackPercentByStage[index] + 99) / 100),
            encounter.GuardAmount, encounter.EmpowerAmount, encounter.HeavyAttackPercent, encounter.AttackWeight,
            encounter.HeavyAttackWeight, encounter.GuardWeight, encounter.EmpowerWeight, encounter.ParTurns)).ToArray();
        return new GameConfig(baseline.GameVersion, $"{ExperimentId}-{variant.Id}", baseline.InitialMaxHealth, baseline.InitialAttack,
            baseline.BaseActionPoints, baseline.StrikeBonus, baseline.GuardAmount, baseline.TechniqueDamage,
            baseline.TechniqueCooldownTurns, baseline.InitialItemCharges, baseline.ItemHealAmount, baseline.MaximumTurnsPerEncounter,
            encounters, baseline.Rewards);
    }

    private static void RequireId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("IDs must be 1-64 ASCII letters, digits or hyphens.");
    }
}

public static class ExperimentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ExperimentDefinition Parse(string json)
    {
        var definition = JsonSerializer.Deserialize<ExperimentDefinition>(json, Options) ?? throw new ArgumentException("Missing experiment definition.");
        definition.Validate();
        return definition;
    }
}
