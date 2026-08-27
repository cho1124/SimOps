using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SimOps.Game.Core;

namespace SimOps.Agent.Core;

public sealed class SyntheticRunRecord
{
    internal SyntheticRunRecord(
        AgentDefinition agent,
        RunContext context,
        ulong seed,
        RunResult result,
        IReadOnlyList<GameAction> actions,
        IReadOnlyDictionary<GameActionType, int> actionCounts,
        IReadOnlyDictionary<string, int> rewardCounts,
        string buildSignature)
    {
        Agent = agent;
        Context = context;
        Seed = seed;
        Result = result;
        Actions = actions;
        ActionCounts = actionCounts;
        RewardCounts = rewardCounts;
        BuildSignature = buildSignature;
    }

    public AgentDefinition Agent { get; }
    public RunContext Context { get; }
    public ulong Seed { get; }
    public RunResult Result { get; }
    public IReadOnlyList<GameAction> Actions { get; }
    public IReadOnlyDictionary<GameActionType, int> ActionCounts { get; }
    public IReadOnlyDictionary<string, int> RewardCounts { get; }
    public string BuildSignature { get; }
    public bool EnteredStage3 => Result.ClearedStages >= 2 || ContainsStage(3);
    public bool PassedStage3 => Result.ClearedStages >= 3;

    private bool ContainsStage(int stage)
    {
        for (var index = 0; index < Result.StageSummaries.Count; index++)
        {
            if (Result.StageSummaries[index].Stage == stage)
            {
                return true;
            }
        }

        return false;
    }
}

public static class SyntheticSimulation
{
    public static SyntheticRunRecord Execute(
        GameConfig config,
        ScoreRule scoreRule,
        AgentDefinition definition,
        ulong seed)
    {
        var agent = AgentFactory.Create(definition);
        agent.Initialize(new AgentContext(definition, seed));
        var context = new RunContext(
            config.GameVersion,
            config.Checksum,
            scoreRule.Version,
            scoreRule.Checksum,
            seed);
        var simulation = new GameSimulation(config, scoreRule);
        var observation = simulation.Reset(context);
        var actionCounts = new Dictionary<GameActionType, int>();
        var rewardCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        while (observation.Phase != RunPhase.Terminal)
        {
            var decision = agent.Decide(observation);
            var step = simulation.Apply(decision.SelectedAction);
            if (!step.Accepted)
            {
                throw new InvalidOperationException(
                    $"Agent {definition.Id} selected an invalid action: {step.RejectionCode}");
            }

            if (decision.SelectedAction.ActionType == GameActionType.ChooseReward)
            {
                var rewardId = decision.SelectedAction.RewardId ?? string.Empty;
                Increment(rewardCounts, rewardId);
            }
            else
            {
                Increment(actionCounts, decision.SelectedAction.ActionType);
            }

            observation = step.Observation;
        }

        var result = simulation.GetCanonicalResult();
        agent.OnRunEnded(result);
        return new SyntheticRunRecord(
            definition,
            context,
            seed,
            result,
            simulation.ActionLog,
            actionCounts,
            rewardCounts,
            CreateBuildSignature(rewardCounts));
    }

    private static string CreateBuildSignature(IReadOnlyDictionary<string, int> rewards)
    {
        var ids = new List<string>(rewards.Keys);
        ids.Sort(StringComparer.Ordinal);
        var builder = new StringBuilder();
        for (var index = 0; index < ids.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('|');
            }

            var id = ids[index];
            builder.Append(id);
            builder.Append(':');
            builder.Append(rewards[id].ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void Increment<TKey>(Dictionary<TKey, int> values, TKey key)
        where TKey : notnull
    {
        values.TryGetValue(key, out var count);
        values[key] = count + 1;
    }
}

public sealed class PersonaMetrics
{
    private readonly Dictionary<GameActionType, int> _actions = new Dictionary<GameActionType, int>();
    private readonly Dictionary<string, int> _rewards = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly HashSet<string> _builds = new HashSet<string>(StringComparer.Ordinal);
    private int _victories;
    private int _stage3Entries;
    private int _stage3Passes;
    private long _totalTurns;
    private long _totalScore;
    private readonly int _availableRewardCount;
    private RunContext? _context;

    public PersonaMetrics(AgentDefinition agent, int availableRewardCount)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        if (availableRewardCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableRewardCount));
        }

        _availableRewardCount = availableRewardCount;
    }

    public AgentDefinition Agent { get; }
    public int Runs { get; private set; }
    public double? ClearRate => Ratio(_victories, Runs);
    public double? Stage3PassRate => Ratio(_stage3Passes, _stage3Entries);
    public double? AverageTurns => Ratio(_totalTurns, Runs);
    public double? AverageScore => Ratio(_totalScore, Runs);
    public int UniqueBuildCount => _builds.Count;
    public double? RewardEntropy => CalculateNormalizedEntropy(_rewards, _availableRewardCount);

    public IReadOnlyDictionary<string, string> GetUndefinedMetricReasons()
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Runs == 0)
        {
            reasons["run_clear_rate.v1"] = "NO_VALID_RUNS";
            reasons["total_turns.v1"] = "NO_VALID_RUNS";
            reasons["average_score.v1"] = "NO_VALID_RUNS";
        }

        if (_stage3Entries == 0)
        {
            reasons["stage_pass_rate.v1:stage=3"] = "NO_STAGE_ENTRIES";
        }

        if (_actions.Count == 0)
        {
            reasons["action_share.v1"] = "NO_PLAYER_ACTIONS";
        }

        if (_rewards.Count == 0)
        {
            reasons["reward_pick_share.v1"] = "NO_REWARD_SELECTIONS";
            reasons["normalized_reward_entropy.v1"] = "NO_REWARD_SELECTIONS";
        }
        else if (_availableRewardCount <= 1)
        {
            reasons["normalized_reward_entropy.v1"] = "INSUFFICIENT_AVAILABLE_REWARDS";
        }

        return reasons;
    }

    public void Add(SyntheticRunRecord run)
    {
        if (!string.Equals(run.Agent.Id, Agent.Id, StringComparison.Ordinal) ||
            !string.Equals(run.Agent.Version, Agent.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot mix agent definitions in one metric accumulator.");
        }

        if (_context is not null &&
            (!string.Equals(_context.GameVersion, run.Context.GameVersion, StringComparison.Ordinal) ||
             !string.Equals(_context.ConfigChecksum, run.Context.ConfigChecksum, StringComparison.Ordinal) ||
             !string.Equals(_context.ScoreRuleChecksum, run.Context.ScoreRuleChecksum, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Cannot mix game, config, or score versions in one metric accumulator.");
        }

        _context = run.Context;

        Runs += 1;
        if (run.Result.Outcome == RunOutcome.Victory)
        {
            _victories += 1;
        }

        if (run.EnteredStage3)
        {
            _stage3Entries += 1;
        }

        if (run.PassedStage3)
        {
            _stage3Passes += 1;
        }

        _totalTurns += run.Result.TotalTurns;
        _totalScore += run.Result.FinalScore;
        _builds.Add(run.BuildSignature);
        Merge(_actions, run.ActionCounts);
        Merge(_rewards, run.RewardCounts);
    }

    public double? ActionShare(GameActionType actionType)
    {
        _actions.TryGetValue(actionType, out var selected);
        var total = 0;
        foreach (var pair in _actions)
        {
            total += pair.Value;
        }

        return Ratio(selected, total);
    }

    public double? RewardCategoryShare(string categoryPrefix)
    {
        var selected = 0;
        var total = 0;
        foreach (var pair in _rewards)
        {
            total += pair.Value;
            if (pair.Key.StartsWith(categoryPrefix, StringComparison.Ordinal))
            {
                selected += pair.Value;
            }
        }

        return Ratio(selected, total);
    }

    private static void Merge<TKey>(Dictionary<TKey, int> target, IReadOnlyDictionary<TKey, int> source)
        where TKey : notnull
    {
        foreach (var pair in source)
        {
            target.TryGetValue(pair.Key, out var current);
            target[pair.Key] = current + pair.Value;
        }
    }

    private static double? CalculateNormalizedEntropy(IReadOnlyDictionary<string, int> values, int availableCount)
    {
        var total = 0;
        foreach (var pair in values)
        {
            total += pair.Value;
        }

        if (total == 0 || availableCount <= 1)
        {
            return null;
        }

        var entropy = 0d;
        foreach (var pair in values)
        {
            if (pair.Value == 0)
            {
                continue;
            }

            var probability = pair.Value / (double)total;
            entropy -= probability * Math.Log(probability);
        }

        return entropy / Math.Log(availableCount);
    }

    private static double? Ratio(long numerator, long denominator)
    {
        return denominator == 0 ? (double?)null : numerator / (double)denominator;
    }
}
