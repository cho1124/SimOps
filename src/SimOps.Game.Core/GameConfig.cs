using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SimOps.Game.Core;

public sealed class EncounterDefinition
{
    public EncounterDefinition(
        string id,
        int stage,
        int maxHealth,
        int attackPower,
        int guardAmount,
        int empowerAmount,
        int heavyAttackPercent,
        int attackWeight,
        int heavyAttackWeight,
        int guardWeight,
        int empowerWeight,
        int parTurns)
    {
        Id = RequireId(id, nameof(id));
        Stage = stage;
        MaxHealth = maxHealth;
        AttackPower = attackPower;
        GuardAmount = guardAmount;
        EmpowerAmount = empowerAmount;
        HeavyAttackPercent = heavyAttackPercent;
        AttackWeight = attackWeight;
        HeavyAttackWeight = heavyAttackWeight;
        GuardWeight = guardWeight;
        EmpowerWeight = empowerWeight;
        ParTurns = parTurns;
    }

    public string Id { get; }

    public int Stage { get; }

    public int MaxHealth { get; }

    public int AttackPower { get; }

    public int GuardAmount { get; }

    public int EmpowerAmount { get; }

    public int HeavyAttackPercent { get; }

    public int AttackWeight { get; }

    public int HeavyAttackWeight { get; }

    public int GuardWeight { get; }

    public int EmpowerWeight { get; }

    public int ParTurns { get; }

    public int TotalIntentWeight => AttackWeight + HeavyAttackWeight + GuardWeight + EmpowerWeight;

    private static string RequireId(string id, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("ID cannot be empty.", parameterName);
        }

        return id;
    }
}

public sealed class RewardDefinition
{
    public RewardDefinition(
        string id,
        RewardCategory category,
        RewardEffectType effectType,
        int value,
        int maxStacks)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("ID cannot be empty.", nameof(id));
        }

        Id = id;
        Category = category;
        EffectType = effectType;
        Value = value;
        MaxStacks = maxStacks;
    }

    public string Id { get; }

    public RewardCategory Category { get; }

    public RewardEffectType EffectType { get; }

    public int Value { get; }

    public int MaxStacks { get; }
}

public sealed class ScoreRule
{
    public ScoreRule(
        string version,
        int progressPerStage,
        int bossBonus,
        int maximumSurvivalBonus,
        int tempoPerTurn)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Version cannot be empty.", nameof(version));
        }

        if (progressPerStage < 0 || bossBonus < 0 || maximumSurvivalBonus < 0 || tempoPerTurn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(progressPerStage), "Score values cannot be negative.");
        }

        Version = version;
        ProgressPerStage = progressPerStage;
        BossBonus = bossBonus;
        MaximumSurvivalBonus = maximumSurvivalBonus;
        TempoPerTurn = tempoPerTurn;
        Checksum = StableHash.Sha256Hex(BuildCanonicalValue());
    }

    public string Version { get; }

    public int ProgressPerStage { get; }

    public int BossBonus { get; }

    public int MaximumSurvivalBonus { get; }

    public int TempoPerTurn { get; }

    public string Checksum { get; }

    public static ScoreRule CreateBaseline()
    {
        return new ScoreRule(
            version: "0.1.0-floor",
            progressPerStage: 10_000,
            bossBonus: 20_000,
            maximumSurvivalBonus: 5_000,
            tempoPerTurn: 100);
    }

    private string BuildCanonicalValue()
    {
        return string.Join(
            "|",
            Version,
            ProgressPerStage.ToString(CultureInfo.InvariantCulture),
            BossBonus.ToString(CultureInfo.InvariantCulture),
            MaximumSurvivalBonus.ToString(CultureInfo.InvariantCulture),
            TempoPerTurn.ToString(CultureInfo.InvariantCulture));
    }
}

public sealed class GameConfig
{
    private readonly EncounterDefinition[] _encounters;
    private readonly RewardDefinition[] _rewards;

    public GameConfig(
        string gameVersion,
        string configVersion,
        int initialMaxHealth,
        int initialAttack,
        int baseActionPoints,
        int strikeBonus,
        int guardAmount,
        int techniqueDamage,
        int techniqueCooldownTurns,
        int initialItemCharges,
        int itemHealAmount,
        int maximumTurnsPerEncounter,
        IReadOnlyList<EncounterDefinition> encounters,
        IReadOnlyList<RewardDefinition> rewards)
    {
        GameVersion = RequireValue(gameVersion, nameof(gameVersion));
        ConfigVersion = RequireValue(configVersion, nameof(configVersion));
        InitialMaxHealth = initialMaxHealth;
        InitialAttack = initialAttack;
        BaseActionPoints = baseActionPoints;
        StrikeBonus = strikeBonus;
        GuardAmount = guardAmount;
        TechniqueDamage = techniqueDamage;
        TechniqueCooldownTurns = techniqueCooldownTurns;
        InitialItemCharges = initialItemCharges;
        ItemHealAmount = itemHealAmount;
        MaximumTurnsPerEncounter = maximumTurnsPerEncounter;
        _encounters = Copy(encounters, nameof(encounters));
        _rewards = Copy(rewards, nameof(rewards));

        Validate();
        Checksum = StableHash.Sha256Hex(BuildCanonicalValue());
    }

    public string GameVersion { get; }

    public string ConfigVersion { get; }

    public string Checksum { get; }

    public int InitialMaxHealth { get; }

    public int InitialAttack { get; }

    public int BaseActionPoints { get; }

    public int StrikeBonus { get; }

    public int GuardAmount { get; }

    public int TechniqueDamage { get; }

    public int TechniqueCooldownTurns { get; }

    public int InitialItemCharges { get; }

    public int ItemHealAmount { get; }

    public int MaximumTurnsPerEncounter { get; }

    public IReadOnlyList<EncounterDefinition> Encounters => _encounters;

    public IReadOnlyList<RewardDefinition> Rewards => _rewards;

    public static GameConfig CreateBaseline()
    {
        return new GameConfig(
            gameVersion: "0.1.0",
            configVersion: "baseline-0.1.0",
            initialMaxHealth: 80,
            initialAttack: 10,
            baseActionPoints: 2,
            strikeBonus: 0,
            guardAmount: 7,
            techniqueDamage: 18,
            techniqueCooldownTurns: 2,
            initialItemCharges: 2,
            itemHealAmount: 14,
            maximumTurnsPerEncounter: 30,
            encounters: new[]
            {
                new EncounterDefinition("striker-1", 1, 20, 4, 4, 1, 150, 75, 10, 10, 5, 4),
                new EncounterDefinition("guardian-2", 2, 28, 5, 7, 1, 150, 45, 10, 40, 5, 5),
                new EncounterDefinition("charger-3", 3, 36, 6, 5, 2, 175, 45, 35, 10, 10, 6),
                new EncounterDefinition("striker-4", 4, 45, 7, 5, 2, 175, 55, 25, 10, 10, 7),
                new EncounterDefinition("guardian-5", 5, 55, 8, 9, 2, 180, 35, 20, 35, 10, 8),
                new EncounterDefinition("boss-6", 6, 70, 10, 10, 3, 190, 35, 30, 20, 15, 10),
            },
            rewards: CreateBaselineRewards());
    }

    private static RewardDefinition[] CreateBaselineRewards()
    {
        return new[]
        {
            new RewardDefinition("offense-sharpened-edge", RewardCategory.Offense, RewardEffectType.Attack, 2, 3),
            new RewardDefinition("offense-precise-strike", RewardCategory.Offense, RewardEffectType.StrikeBonus, 2, 2),
            new RewardDefinition("offense-overcharge", RewardCategory.Offense, RewardEffectType.TechniqueBonus, 5, 2),
            new RewardDefinition("defense-braced-stance", RewardCategory.Defense, RewardEffectType.GuardBonus, 3, 3),
            new RewardDefinition("defense-reinforced-frame", RewardCategory.Defense, RewardEffectType.MaxHealth, 8, 3),
            new RewardDefinition("defense-opening-guard", RewardCategory.Defense, RewardEffectType.StartTurnBlock, 3, 2),
            new RewardDefinition("sustain-vitality", RewardCategory.Sustain, RewardEffectType.MaxHealth, 10, 2),
            new RewardDefinition("sustain-field-medicine", RewardCategory.Sustain, RewardEffectType.ItemHealBonus, 5, 2),
            new RewardDefinition("sustain-extra-charge", RewardCategory.Sustain, RewardEffectType.ItemCharges, 1, 2),
            new RewardDefinition("tactics-quick-cycle", RewardCategory.Tactics, RewardEffectType.TechniqueCooldownReduction, 1, 1),
            new RewardDefinition("tactics-battle-rhythm", RewardCategory.Tactics, RewardEffectType.ActionPoints, 1, 1),
            new RewardDefinition("tactics-fortified-technique", RewardCategory.Tactics, RewardEffectType.TechniqueBonus, 3, 3),
        };
    }

    private void Validate()
    {
        if (InitialMaxHealth <= 0 || InitialAttack < 0 || BaseActionPoints <= 0)
        {
            throw new ArgumentException("Player baseline values are invalid.");
        }

        if (StrikeBonus < 0 || GuardAmount < 0 || TechniqueDamage < 0 || TechniqueCooldownTurns < 0)
        {
            throw new ArgumentException("Action baseline values are invalid.");
        }

        if (InitialItemCharges < 0 || ItemHealAmount < 0 || MaximumTurnsPerEncounter <= 0)
        {
            throw new ArgumentException("Item or turn-limit values are invalid.");
        }

        if (_encounters.Length != 6)
        {
            throw new ArgumentException("Exactly six encounters are required.");
        }

        var encounterIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < _encounters.Length; index++)
        {
            var encounter = _encounters[index];
            if (encounter.Stage != index + 1)
            {
                throw new ArgumentException("Encounter stages must be ordered from 1 to 6.");
            }

            if (!encounterIds.Add(encounter.Id))
            {
                throw new ArgumentException("Encounter IDs must be unique.");
            }

            if (encounter.MaxHealth <= 0 || encounter.AttackPower < 0 || encounter.GuardAmount < 0 ||
                encounter.EmpowerAmount < 0 || encounter.HeavyAttackPercent < 100 || encounter.ParTurns <= 0 ||
                encounter.TotalIntentWeight <= 0)
            {
                throw new ArgumentException("Encounter values are invalid.");
            }

            if (encounter.AttackWeight < 0 || encounter.HeavyAttackWeight < 0 ||
                encounter.GuardWeight < 0 || encounter.EmpowerWeight < 0)
            {
                throw new ArgumentException("Intent weights cannot be negative.");
            }
        }

        if (_rewards.Length < 3)
        {
            throw new ArgumentException("At least three rewards are required.");
        }

        var rewardIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < _rewards.Length; index++)
        {
            var reward = _rewards[index];
            if (!rewardIds.Add(reward.Id))
            {
                throw new ArgumentException("Reward IDs must be unique.");
            }

            if (reward.Value <= 0 || reward.MaxStacks <= 0)
            {
                throw new ArgumentException("Reward values and stack limits must be positive.");
            }
        }

        ValidateRewardPoolCapacity();
    }

    private void ValidateRewardPoolCapacity()
    {
        var exhaustionCosts = new List<int>(_rewards.Length);
        for (var index = 0; index < _rewards.Length; index++)
        {
            exhaustionCosts.Add(_rewards[index].MaxStacks);
        }

        exhaustionCosts.Sort();
        var spentSelections = 0;
        var exhaustedRewards = 0;

        // Stages 1-5 each create one offer. Validate the worst valid selection path
        // before every offer so pool exhaustion cannot become a runtime failure.
        for (var completedSelections = 0; completedSelections < 5; completedSelections++)
        {
            while (exhaustedRewards < exhaustionCosts.Count &&
                   spentSelections + exhaustionCosts[exhaustedRewards] <= completedSelections)
            {
                spentSelections += exhaustionCosts[exhaustedRewards];
                exhaustedRewards += 1;
            }

            if (_rewards.Length - exhaustedRewards < 3)
            {
                throw new ArgumentException(
                    "Reward pool cannot provide three eligible rewards after every normal stage.");
            }
        }
    }

    private string BuildCanonicalValue()
    {
        var builder = new StringBuilder();
        Append(builder, GameVersion);
        Append(builder, ConfigVersion);
        Append(builder, InitialMaxHealth);
        Append(builder, InitialAttack);
        Append(builder, BaseActionPoints);
        Append(builder, StrikeBonus);
        Append(builder, GuardAmount);
        Append(builder, TechniqueDamage);
        Append(builder, TechniqueCooldownTurns);
        Append(builder, InitialItemCharges);
        Append(builder, ItemHealAmount);
        Append(builder, MaximumTurnsPerEncounter);

        for (var index = 0; index < _encounters.Length; index++)
        {
            var encounter = _encounters[index];
            Append(builder, encounter.Id);
            Append(builder, encounter.Stage);
            Append(builder, encounter.MaxHealth);
            Append(builder, encounter.AttackPower);
            Append(builder, encounter.GuardAmount);
            Append(builder, encounter.EmpowerAmount);
            Append(builder, encounter.HeavyAttackPercent);
            Append(builder, encounter.AttackWeight);
            Append(builder, encounter.HeavyAttackWeight);
            Append(builder, encounter.GuardWeight);
            Append(builder, encounter.EmpowerWeight);
            Append(builder, encounter.ParTurns);
        }

        for (var index = 0; index < _rewards.Length; index++)
        {
            var reward = _rewards[index];
            Append(builder, reward.Id);
            Append(builder, (int)reward.Category);
            Append(builder, (int)reward.EffectType);
            Append(builder, reward.Value);
            Append(builder, reward.MaxStacks);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private static void Append(StringBuilder builder, int value)
    {
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        builder.Append('|');
    }

    private static T[] Copy<T>(IReadOnlyList<T> source, string parameterName)
    {
        if (source is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var result = new T[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            if (source[index] is null)
            {
                throw new ArgumentException("Collection values cannot be null.", parameterName);
            }

            result[index] = source[index];
        }

        return result;
    }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value;
    }
}
