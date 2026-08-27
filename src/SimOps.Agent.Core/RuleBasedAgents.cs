using System;
using System.Collections.Generic;
using SimOps.Game.Core;

namespace SimOps.Agent.Core;

public static class AgentFactory
{
    private const string Version = "1.0.0";

    public static IReadOnlyList<AgentDefinition> CreateDefinitions()
    {
        return new[]
        {
            new AgentDefinition("random", Version, AgentPersona.Random),
            new AgentDefinition("novice", Version, AgentPersona.Novice),
            new AgentDefinition("aggressive", Version, AgentPersona.Aggressive),
            new AgentDefinition("defensive", Version, AgentPersona.Defensive),
            new AgentDefinition("greedy", Version, AgentPersona.Greedy),
            new AgentDefinition("explorer", Version, AgentPersona.Explorer),
        };
    }

    public static IGameAgent Create(AgentDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        return definition.Persona switch
        {
            AgentPersona.Random => new RandomAgent(definition),
            AgentPersona.Novice => new UtilityAgent(definition, AgentPolicy.Novice),
            AgentPersona.Aggressive => new UtilityAgent(definition, AgentPolicy.Aggressive),
            AgentPersona.Defensive => new UtilityAgent(definition, AgentPolicy.Defensive),
            AgentPersona.Greedy => new UtilityAgent(definition, AgentPolicy.Greedy),
            AgentPersona.Explorer => new UtilityAgent(definition, AgentPolicy.Explorer),
            _ => throw new ArgumentOutOfRangeException(nameof(definition)),
        };
    }
}

internal abstract class GameAgentBase : IGameAgent
{
    private DeterministicRandom? _random;

    protected GameAgentBase(AgentDefinition definition)
    {
        Definition = definition;
    }

    public AgentDefinition Definition { get; }

    protected DeterministicRandom Random =>
        _random ?? throw new InvalidOperationException("Agent must be initialized before deciding.");

    public virtual void Initialize(AgentContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!string.Equals(context.Definition.Id, Definition.Id, StringComparison.Ordinal) ||
            !string.Equals(context.Definition.Version, Definition.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("AGENT_CONTEXT_MISMATCH");
        }

        _random = new DeterministicRandom(
            DeterministicRandom.DeriveSeed(context.BaseSeed, RandomStream.Agent, (int)Definition.Persona));
        ResetPolicyState();
    }

    public abstract AgentDecision Decide(GameObservation observation);

    public virtual void OnRunEnded(RunResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }
    }

    protected virtual void ResetPolicyState()
    {
    }

    protected AgentDecision CreateDecision(GameAction action, ulong decisionSeed, string reason)
    {
        return new AgentDecision(action, Definition.Version, decisionSeed, reason);
    }
}

internal sealed class RandomAgent : GameAgentBase
{
    public RandomAgent(AgentDefinition definition)
        : base(definition)
    {
    }

    public override AgentDecision Decide(GameObservation observation)
    {
        if (observation is null)
        {
            throw new ArgumentNullException(nameof(observation));
        }

        var decisionSeed = Random.State;
        if (observation.Phase == RunPhase.RewardChoice)
        {
            var rewardIndex = Random.NextInt(observation.OfferedRewardIds.Count);
            return CreateDecision(
                new GameAction(
                    observation.NextActionSequence,
                    GameActionType.ChooseReward,
                    observation.OfferedRewardIds[rewardIndex]),
                decisionSeed,
                "uniform-reward");
        }

        var actionIndex = Random.NextInt(observation.ValidActionTypes.Count);
        return CreateDecision(
            new GameAction(observation.NextActionSequence, observation.ValidActionTypes[actionIndex]),
            decisionSeed,
            "uniform-action");
    }
}

internal sealed class AgentPolicy
{
    public static readonly AgentPolicy Novice = new AgentPolicy(
        strike: 55,
        technique: 62,
        guard: 28,
        item: 18,
        heavyGuardBonus: 15,
        healWeight: 1,
        endTurnPenalty: 10,
        noise: 45,
        mistakePercent: 35,
        novelty: 0,
        offenseReward: 35,
        defenseReward: 25,
        sustainReward: 30,
        tacticsReward: 20);

    public static readonly AgentPolicy Aggressive = new AgentPolicy(
        strike: 100,
        technique: 145,
        guard: 5,
        item: 5,
        heavyGuardBonus: 5,
        healWeight: 1,
        endTurnPenalty: 100,
        noise: 12,
        mistakePercent: 0,
        novelty: 0,
        offenseReward: 130,
        defenseReward: 5,
        sustainReward: 10,
        tacticsReward: 70);

    public static readonly AgentPolicy Defensive = new AgentPolicy(
        strike: 52,
        technique: 70,
        guard: 62,
        item: 30,
        heavyGuardBonus: 120,
        healWeight: 5,
        endTurnPenalty: 70,
        noise: 10,
        mistakePercent: 0,
        novelty: 0,
        offenseReward: 15,
        defenseReward: 120,
        sustainReward: 105,
        tacticsReward: 35);

    public static readonly AgentPolicy Greedy = new AgentPolicy(
        strike: 95,
        technique: 150,
        guard: 25,
        item: 15,
        heavyGuardBonus: 100,
        healWeight: 4,
        endTurnPenalty: 120,
        noise: 2,
        mistakePercent: 0,
        novelty: 0,
        offenseReward: 90,
        defenseReward: 35,
        sustainReward: 45,
        tacticsReward: 130);

    public static readonly AgentPolicy Explorer = new AgentPolicy(
        strike: 55,
        technique: 65,
        guard: 40,
        item: 20,
        heavyGuardBonus: 65,
        healWeight: 3,
        endTurnPenalty: 45,
        noise: 20,
        mistakePercent: 0,
        novelty: 80,
        offenseReward: 40,
        defenseReward: 40,
        sustainReward: 40,
        tacticsReward: 40);

    private AgentPolicy(
        int strike,
        int technique,
        int guard,
        int item,
        int heavyGuardBonus,
        int healWeight,
        int endTurnPenalty,
        int noise,
        int mistakePercent,
        int novelty,
        int offenseReward,
        int defenseReward,
        int sustainReward,
        int tacticsReward)
    {
        Strike = strike;
        Technique = technique;
        Guard = guard;
        Item = item;
        HeavyGuardBonus = heavyGuardBonus;
        HealWeight = healWeight;
        EndTurnPenalty = endTurnPenalty;
        Noise = noise;
        MistakePercent = mistakePercent;
        Novelty = novelty;
        OffenseReward = offenseReward;
        DefenseReward = defenseReward;
        SustainReward = sustainReward;
        TacticsReward = tacticsReward;
    }

    public int Strike { get; }
    public int Technique { get; }
    public int Guard { get; }
    public int Item { get; }
    public int HeavyGuardBonus { get; }
    public int HealWeight { get; }
    public int EndTurnPenalty { get; }
    public int Noise { get; }
    public int MistakePercent { get; }
    public int Novelty { get; }
    public int OffenseReward { get; }
    public int DefenseReward { get; }
    public int SustainReward { get; }
    public int TacticsReward { get; }
}

internal sealed class UtilityAgent : GameAgentBase
{
    private readonly AgentPolicy _policy;
    private readonly Dictionary<GameActionType, int> _actionCounts =
        new Dictionary<GameActionType, int>();
    private readonly Dictionary<string, int> _rewardCounts =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public UtilityAgent(AgentDefinition definition, AgentPolicy policy)
        : base(definition)
    {
        _policy = policy;
    }

    protected override void ResetPolicyState()
    {
        _actionCounts.Clear();
        _rewardCounts.Clear();
    }

    public override AgentDecision Decide(GameObservation observation)
    {
        if (observation is null)
        {
            throw new ArgumentNullException(nameof(observation));
        }

        var decisionSeed = Random.State;
        if (observation.Phase == RunPhase.RewardChoice)
        {
            return DecideReward(observation, decisionSeed);
        }

        if (_policy.MistakePercent > 0 && Random.NextInt(100) < _policy.MistakePercent)
        {
            var mistakeIndex = Random.NextInt(observation.ValidActionTypes.Count);
            var mistake = observation.ValidActionTypes[mistakeIndex];
            Increment(_actionCounts, mistake);
            return CreateDecision(
                new GameAction(observation.NextActionSequence, mistake),
                decisionSeed,
                "seeded-mistake");
        }

        var selected = observation.ValidActionTypes[0];
        var selectedScore = int.MinValue;
        for (var index = 0; index < observation.ValidActionTypes.Count; index++)
        {
            var candidate = observation.ValidActionTypes[index];
            var score = ScoreAction(observation, candidate);
            if (score > selectedScore)
            {
                selected = candidate;
                selectedScore = score;
            }
        }

        Increment(_actionCounts, selected);
        return CreateDecision(
            new GameAction(observation.NextActionSequence, selected),
            decisionSeed,
            $"utility={selectedScore}");
    }

    private AgentDecision DecideReward(GameObservation observation, ulong decisionSeed)
    {
        var selected = observation.OfferedRewardIds[0];
        var selectedScore = int.MinValue;
        for (var index = 0; index < observation.OfferedRewardIds.Count; index++)
        {
            var rewardId = observation.OfferedRewardIds[index];
            var score = ScoreReward(rewardId);
            if (score > selectedScore)
            {
                selected = rewardId;
                selectedScore = score;
            }
        }

        Increment(_rewardCounts, selected);
        return CreateDecision(
            new GameAction(observation.NextActionSequence, GameActionType.ChooseReward, selected),
            decisionSeed,
            $"reward-utility={selectedScore}");
    }

    private int ScoreAction(GameObservation observation, GameActionType actionType)
    {
        var score = actionType switch
        {
            GameActionType.Strike => _policy.Strike,
            GameActionType.Technique => _policy.Technique,
            GameActionType.Guard => _policy.Guard,
            GameActionType.UseItem => _policy.Item,
            GameActionType.EndTurn => -_policy.EndTurnPenalty,
            _ => int.MinValue / 2,
        };

        if (actionType == GameActionType.Guard && observation.Enemy?.Intent == EnemyIntentType.HeavyAttack)
        {
            score += _policy.HeavyGuardBonus;
        }

        if (actionType == GameActionType.UseItem)
        {
            var missingHealth = observation.Player.MaxHealth - observation.Player.CurrentHealth;
            score += missingHealth * _policy.HealWeight;
            if (missingHealth == 0)
            {
                score -= 200;
            }
        }

        if ((actionType == GameActionType.Strike || actionType == GameActionType.Technique) &&
            observation.Enemy is not null &&
            observation.Enemy.CurrentHealth <= observation.Player.Attack)
        {
            score += 100;
        }

        _actionCounts.TryGetValue(actionType, out var count);
        score += _policy.Novelty / (count + 1);
        if (_policy.Noise > 0)
        {
            score += Random.NextInt(_policy.Noise + 1);
        }

        return score;
    }

    private int ScoreReward(string rewardId)
    {
        var score = rewardId.StartsWith("offense-", StringComparison.Ordinal)
            ? _policy.OffenseReward
            : rewardId.StartsWith("defense-", StringComparison.Ordinal)
                ? _policy.DefenseReward
                : rewardId.StartsWith("sustain-", StringComparison.Ordinal)
                    ? _policy.SustainReward
                    : _policy.TacticsReward;

        if (Definition.Persona == AgentPersona.Greedy)
        {
            if (string.Equals(rewardId, "tactics-battle-rhythm", StringComparison.Ordinal))
            {
                score += 120;
            }
            else if (string.Equals(rewardId, "tactics-quick-cycle", StringComparison.Ordinal))
            {
                score += 80;
            }
        }

        _rewardCounts.TryGetValue(rewardId, out var count);
        score += _policy.Novelty / (count + 1);
        if (_policy.Noise > 0)
        {
            score += Random.NextInt(_policy.Noise + 1);
        }

        return score;
    }

    private static void Increment<TKey>(Dictionary<TKey, int> values, TKey key)
        where TKey : notnull
    {
        values.TryGetValue(key, out var count);
        values[key] = count + 1;
    }
}
