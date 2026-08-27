using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SimOps.Game.Core;

public sealed class GameSimulation
{
    private readonly GameConfig _config;
    private readonly ScoreRule _scoreRule;
    private readonly List<GameAction> _actionLog = new List<GameAction>();
    private readonly List<StageSummary> _stageSummaries = new List<StageSummary>();
    private readonly List<RewardChoiceRecord> _rewardChoices = new List<RewardChoiceRecord>();
    private readonly List<string> _acquiredRewardIds = new List<string>();
    private readonly List<string> _offeredRewardIds = new List<string>();
    private readonly Dictionary<string, int> _rewardStacks = new Dictionary<string, int>(StringComparer.Ordinal);

    private RunContext? _context;
    private DeterministicRandom? _intentRandom;
    private DeterministicRandom? _rewardRandom;
    private EncounterDefinition? _encounter;
    private bool _initialized;

    private RunPhase _phase;
    private RunOutcome _outcome;
    private int _stageIndex;
    private int _turn;
    private int _totalTurns;
    private int _clearedStages;

    private int _playerCurrentHealth;
    private int _playerMaxHealth;
    private int _playerAttack;
    private int _playerActionPoints;
    private int _playerBlock;
    private int _playerTechniqueCooldown;
    private int _playerItemCharges;
    private bool _playerItemUsedThisTurn;

    private int _strikeBonus;
    private int _techniqueBonus;
    private int _guardBonus;
    private int _startTurnBlock;
    private int _itemHealBonus;
    private int _techniqueCooldownReduction;
    private int _actionPointBonus;

    private int _enemyCurrentHealth;
    private int _enemyBlock;
    private int _enemyAttackBonus;
    private EnemyIntentType _enemyIntent;

    public GameSimulation(GameConfig config, ScoreRule scoreRule)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _scoreRule = scoreRule ?? throw new ArgumentNullException(nameof(scoreRule));
    }

    public IReadOnlyList<GameAction> ActionLog => _actionLog.ToArray();

    public GameObservation Reset(RunContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        ValidateContext(context);
        _context = context;
        _actionLog.Clear();
        _stageSummaries.Clear();
        _rewardChoices.Clear();
        _acquiredRewardIds.Clear();
        _offeredRewardIds.Clear();
        _rewardStacks.Clear();

        _phase = RunPhase.PlayerTurn;
        _outcome = RunOutcome.InProgress;
        _stageIndex = 0;
        _turn = 0;
        _totalTurns = 0;
        _clearedStages = 0;

        _playerMaxHealth = _config.InitialMaxHealth;
        _playerCurrentHealth = _playerMaxHealth;
        _playerAttack = _config.InitialAttack;
        _playerActionPoints = 0;
        _playerBlock = 0;
        _playerTechniqueCooldown = 0;
        _playerItemCharges = _config.InitialItemCharges;
        _playerItemUsedThisTurn = false;

        _strikeBonus = _config.StrikeBonus;
        _techniqueBonus = 0;
        _guardBonus = 0;
        _startTurnBlock = 0;
        _itemHealBonus = 0;
        _techniqueCooldownReduction = 0;
        _actionPointBonus = 0;

        _enemyCurrentHealth = 0;
        _enemyBlock = 0;
        _enemyAttackBonus = 0;
        _enemyIntent = EnemyIntentType.Attack;

        _initialized = true;
        StartEncounter(0);
        return CreateObservation();
    }

    public StepResult Apply(GameAction action)
    {
        EnsureInitialized();
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var rejectionCode = ValidateAction(action);
        if (rejectionCode is not null)
        {
            return new StepResult(false, rejectionCode, CreateObservation(), Array.Empty<string>());
        }

        var events = new List<string>();
        switch (action.ActionType)
        {
            case GameActionType.Strike:
                ApplyStrike(events);
                break;
            case GameActionType.Guard:
                ApplyGuard(events);
                break;
            case GameActionType.Technique:
                ApplyTechnique(events);
                break;
            case GameActionType.UseItem:
                ApplyItem(events);
                break;
            case GameActionType.EndTurn:
                _playerActionPoints = 0;
                events.Add("player.turn-ended");
                ResolveEnemyPhase(events);
                break;
            case GameActionType.ChooseReward:
                ApplyReward(action.RewardId!, events);
                break;
            default:
                throw new InvalidOperationException("Unsupported action type.");
        }

        _actionLog.Add(new GameAction(action.Sequence, action.ActionType, action.RewardId));
        return new StepResult(true, null, CreateObservation(), events.ToArray());
    }

    public string GetStateHash()
    {
        EnsureInitialized();
        return StableHash.Sha256Hex(BuildCanonicalState());
    }

    public RunResult GetCanonicalResult()
    {
        EnsureInitialized();
        if (_phase != RunPhase.Terminal)
        {
            throw new InvalidOperationException("A canonical result is available only for a terminal run.");
        }

        var score = CalculateScore();
        return new RunResult(
            _outcome,
            _clearedStages,
            _totalTurns,
            _playerCurrentHealth,
            _playerMaxHealth,
            score,
            _stageSummaries.ToArray(),
            _rewardChoices.ToArray(),
            StableHash.Sha256Hex(BuildCanonicalState()));
    }

    private void ValidateContext(RunContext context)
    {
        if (!string.Equals(context.GameVersion, _config.GameVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("GAME_VERSION_MISMATCH");
        }

        if (!string.Equals(context.ConfigChecksum, _config.Checksum, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CONFIG_CHECKSUM_MISMATCH");
        }

        if (!string.Equals(context.ScoreRuleVersion, _scoreRule.Version, StringComparison.Ordinal) ||
            !string.Equals(context.ScoreRuleChecksum, _scoreRule.Checksum, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SCORE_RULE_MISMATCH");
        }
    }

    private string? ValidateAction(GameAction action)
    {
        if (_phase == RunPhase.Terminal)
        {
            return "RUN_TERMINAL";
        }

        if (action.Sequence != _actionLog.Count)
        {
            return "ACTION_SEQUENCE_INVALID";
        }

        if (_phase == RunPhase.RewardChoice)
        {
            if (action.ActionType != GameActionType.ChooseReward)
            {
                return "ACTION_PHASE_INVALID";
            }

            if (action.RewardId is null || !ContainsOrdinal(_offeredRewardIds, action.RewardId))
            {
                return "REWARD_NOT_OFFERED";
            }

            return null;
        }

        if (action.ActionType == GameActionType.ChooseReward)
        {
            return "ACTION_PHASE_INVALID";
        }

        switch (action.ActionType)
        {
            case GameActionType.Strike:
            case GameActionType.Guard:
                return _playerActionPoints >= 1 ? null : "INSUFFICIENT_ACTION_POINTS";
            case GameActionType.Technique:
                if (_playerTechniqueCooldown > 0)
                {
                    return "TECHNIQUE_ON_COOLDOWN";
                }

                return _playerActionPoints >= 2 ? null : "INSUFFICIENT_ACTION_POINTS";
            case GameActionType.UseItem:
                if (_playerItemCharges <= 0)
                {
                    return "ITEM_CHARGE_REQUIRED";
                }

                return _playerItemUsedThisTurn ? "ITEM_ALREADY_USED_THIS_TURN" : null;
            case GameActionType.EndTurn:
                return null;
            default:
                return "ACTION_UNKNOWN";
        }
    }

    private void ApplyStrike(List<string> events)
    {
        _playerActionPoints -= 1;
        DealDamageToEnemy(_playerAttack + _strikeBonus);
        events.Add("player.strike");
        ResolveAfterPlayerAction(events);
    }

    private void ApplyGuard(List<string> events)
    {
        _playerActionPoints -= 1;
        _playerBlock += _config.GuardAmount + _guardBonus;
        events.Add("player.guard");
        ResolveAfterPlayerAction(events);
    }

    private void ApplyTechnique(List<string> events)
    {
        _playerActionPoints -= 2;
        DealDamageToEnemy(_config.TechniqueDamage + _techniqueBonus);
        _playerTechniqueCooldown = Maximum(0, _config.TechniqueCooldownTurns - _techniqueCooldownReduction);
        events.Add("player.technique");
        ResolveAfterPlayerAction(events);
    }

    private void ApplyItem(List<string> events)
    {
        _playerItemCharges -= 1;
        _playerItemUsedThisTurn = true;
        _playerCurrentHealth = Minimum(
            _playerMaxHealth,
            _playerCurrentHealth + _config.ItemHealAmount + _itemHealBonus);
        events.Add("player.item-used");
    }

    private void ResolveAfterPlayerAction(List<string> events)
    {
        if (_enemyCurrentHealth <= 0)
        {
            CompleteEncounter(events);
            return;
        }

        if (_playerActionPoints == 0)
        {
            ResolveEnemyPhase(events);
        }
    }

    private void ResolveEnemyPhase(List<string> events)
    {
        if (_phase != RunPhase.PlayerTurn || _encounter is null)
        {
            return;
        }

        _enemyBlock = 0;

        switch (_enemyIntent)
        {
            case EnemyIntentType.Attack:
                DealDamageToPlayer(_encounter.AttackPower + _enemyAttackBonus);
                events.Add("enemy.attack");
                break;
            case EnemyIntentType.HeavyAttack:
                var heavyDamage = ((_encounter.AttackPower + _enemyAttackBonus) * _encounter.HeavyAttackPercent) / 100;
                DealDamageToPlayer(heavyDamage);
                events.Add("enemy.heavy-attack");
                break;
            case EnemyIntentType.Guard:
                _enemyBlock = _encounter.GuardAmount;
                events.Add("enemy.guard");
                break;
            case EnemyIntentType.Empower:
                _enemyAttackBonus += _encounter.EmpowerAmount;
                events.Add("enemy.empower");
                break;
            default:
                throw new InvalidOperationException("Unknown enemy intent.");
        }

        _playerBlock = 0;

        if (_playerCurrentHealth <= 0)
        {
            _playerCurrentHealth = 0;
            EndRun(RunOutcome.Defeat, false, events, "run.defeated");
            return;
        }

        if (_turn >= _config.MaximumTurnsPerEncounter)
        {
            EndRun(RunOutcome.Defeat, false, events, "run.turn-limit-defeat");
            return;
        }

        BeginTurn();
    }

    private void CompleteEncounter(List<string> events)
    {
        if (_encounter is null)
        {
            throw new InvalidOperationException("No active encounter.");
        }

        _enemyCurrentHealth = 0;
        _clearedStages += 1;
        _stageSummaries.Add(new StageSummary(_encounter.Stage, _encounter.Id, true, _turn));
        events.Add("encounter.cleared");

        if (_encounter.Stage == 6)
        {
            _outcome = RunOutcome.Victory;
            _phase = RunPhase.Terminal;
            events.Add("run.victory");
            return;
        }

        GenerateRewardOffer();
        _phase = RunPhase.RewardChoice;
        events.Add("reward.offered");
    }

    private void ApplyReward(string rewardId, List<string> events)
    {
        var reward = FindReward(rewardId);
        var offeredSnapshot = _offeredRewardIds.ToArray();

        _rewardStacks.TryGetValue(reward.Id, out var currentStacks);
        if (currentStacks >= reward.MaxStacks)
        {
            throw new InvalidOperationException("Reward stack validation drifted after offer generation.");
        }

        _rewardStacks[reward.Id] = currentStacks + 1;
        _acquiredRewardIds.Add(reward.Id);
        _rewardChoices.Add(new RewardChoiceRecord(_clearedStages, offeredSnapshot, reward.Id));

        switch (reward.EffectType)
        {
            case RewardEffectType.Attack:
                _playerAttack += reward.Value;
                break;
            case RewardEffectType.StrikeBonus:
                _strikeBonus += reward.Value;
                break;
            case RewardEffectType.TechniqueBonus:
                _techniqueBonus += reward.Value;
                break;
            case RewardEffectType.GuardBonus:
                _guardBonus += reward.Value;
                break;
            case RewardEffectType.MaxHealth:
                _playerMaxHealth += reward.Value;
                _playerCurrentHealth += reward.Value;
                break;
            case RewardEffectType.StartTurnBlock:
                _startTurnBlock += reward.Value;
                break;
            case RewardEffectType.ItemHealBonus:
                _itemHealBonus += reward.Value;
                break;
            case RewardEffectType.ItemCharges:
                _playerItemCharges += reward.Value;
                break;
            case RewardEffectType.TechniqueCooldownReduction:
                _techniqueCooldownReduction += reward.Value;
                break;
            case RewardEffectType.ActionPoints:
                _actionPointBonus += reward.Value;
                break;
            default:
                throw new InvalidOperationException("Unknown reward effect.");
        }

        _offeredRewardIds.Clear();
        events.Add("reward.selected");
        StartEncounter(_stageIndex + 1);
    }

    private void StartEncounter(int stageIndex)
    {
        if (_context is null)
        {
            throw new InvalidOperationException("Run context is missing.");
        }

        _stageIndex = stageIndex;
        _encounter = _config.Encounters[stageIndex];
        _intentRandom = new DeterministicRandom(
            DeterministicRandom.DeriveSeed(_context.BaseSeed, RandomStream.Intent, stageIndex));
        _rewardRandom = new DeterministicRandom(
            DeterministicRandom.DeriveSeed(_context.BaseSeed, RandomStream.Reward, stageIndex));
        _enemyCurrentHealth = _encounter.MaxHealth;
        _enemyBlock = 0;
        _enemyAttackBonus = 0;
        _turn = 0;
        _playerTechniqueCooldown = 0;
        _phase = RunPhase.PlayerTurn;
        BeginTurn();
    }

    private void BeginTurn()
    {
        if (_encounter is null || _intentRandom is null)
        {
            throw new InvalidOperationException("Encounter RNG is missing.");
        }

        _turn += 1;
        _totalTurns += 1;
        _playerActionPoints = _config.BaseActionPoints + _actionPointBonus;
        _playerBlock = _startTurnBlock;
        _playerItemUsedThisTurn = false;
        if (_playerTechniqueCooldown > 0)
        {
            _playerTechniqueCooldown -= 1;
        }

        var roll = _intentRandom.NextInt(_encounter.TotalIntentWeight);
        if (roll < _encounter.AttackWeight)
        {
            _enemyIntent = EnemyIntentType.Attack;
            return;
        }

        roll -= _encounter.AttackWeight;
        if (roll < _encounter.HeavyAttackWeight)
        {
            _enemyIntent = EnemyIntentType.HeavyAttack;
            return;
        }

        roll -= _encounter.HeavyAttackWeight;
        if (roll < _encounter.GuardWeight)
        {
            _enemyIntent = EnemyIntentType.Guard;
            return;
        }

        _enemyIntent = EnemyIntentType.Empower;
    }

    private void GenerateRewardOffer()
    {
        if (_rewardRandom is null)
        {
            throw new InvalidOperationException("Reward RNG is missing.");
        }

        var eligible = new List<RewardDefinition>();
        for (var index = 0; index < _config.Rewards.Count; index++)
        {
            var reward = _config.Rewards[index];
            _rewardStacks.TryGetValue(reward.Id, out var stacks);
            if (stacks < reward.MaxStacks)
            {
                eligible.Add(reward);
            }
        }

        if (eligible.Count < 3)
        {
            throw new InvalidOperationException("REWARD_POOL_EXHAUSTED");
        }

        for (var index = eligible.Count - 1; index > 0; index--)
        {
            var swapIndex = _rewardRandom.NextInt(index + 1);
            var temporary = eligible[index];
            eligible[index] = eligible[swapIndex];
            eligible[swapIndex] = temporary;
        }

        _offeredRewardIds.Clear();
        for (var index = 0; index < 3; index++)
        {
            _offeredRewardIds.Add(eligible[index].Id);
        }
    }

    private void EndRun(RunOutcome outcome, bool cleared, List<string> events, string terminalEvent)
    {
        if (_encounter is null)
        {
            throw new InvalidOperationException("No active encounter.");
        }

        _stageSummaries.Add(new StageSummary(_encounter.Stage, _encounter.Id, cleared, _turn));
        _outcome = outcome;
        _phase = RunPhase.Terminal;
        events.Add(terminalEvent);
    }

    private GameObservation CreateObservation()
    {
        var player = new PlayerSnapshot(
            _playerCurrentHealth,
            _playerMaxHealth,
            _playerAttack,
            _playerActionPoints,
            _playerBlock,
            _playerTechniqueCooldown,
            _playerItemCharges,
            _playerItemUsedThisTurn);

        EnemySnapshot? enemy = null;
        if (_encounter is not null)
        {
            enemy = new EnemySnapshot(
                _encounter.Id,
                _enemyCurrentHealth,
                _encounter.MaxHealth,
                _enemyBlock,
                _encounter.AttackPower,
                _enemyAttackBonus,
                _enemyIntent);
        }

        return new GameObservation(
            _phase,
            _outcome,
            _stageIndex + 1,
            _turn,
            _totalTurns,
            _actionLog.Count,
            player,
            enemy,
            CreateValidActionTypes(),
            _offeredRewardIds.ToArray());
    }

    private IReadOnlyList<GameActionType> CreateValidActionTypes()
    {
        if (_phase == RunPhase.Terminal)
        {
            return Array.Empty<GameActionType>();
        }

        if (_phase == RunPhase.RewardChoice)
        {
            return new[] { GameActionType.ChooseReward };
        }

        var result = new List<GameActionType>();
        if (_playerActionPoints >= 1)
        {
            result.Add(GameActionType.Strike);
            result.Add(GameActionType.Guard);
        }

        if (_playerActionPoints >= 2 && _playerTechniqueCooldown == 0)
        {
            result.Add(GameActionType.Technique);
        }

        if (_playerItemCharges > 0 && !_playerItemUsedThisTurn)
        {
            result.Add(GameActionType.UseItem);
        }

        result.Add(GameActionType.EndTurn);
        return result.ToArray();
    }

    private int CalculateScore()
    {
        var score = _clearedStages * _scoreRule.ProgressPerStage;
        if (_outcome == RunOutcome.Victory)
        {
            score += _scoreRule.BossBonus;
        }

        if (_playerMaxHealth > 0)
        {
            score += (_playerCurrentHealth * _scoreRule.MaximumSurvivalBonus) / _playerMaxHealth;
        }

        for (var index = 0; index < _stageSummaries.Count; index++)
        {
            var summary = _stageSummaries[index];
            if (!summary.Cleared)
            {
                continue;
            }

            var parTurns = _config.Encounters[summary.Stage - 1].ParTurns;
            score += Maximum(0, parTurns - summary.Turns) * _scoreRule.TempoPerTurn;
        }

        return score;
    }

    private string BuildCanonicalState()
    {
        if (_context is null)
        {
            throw new InvalidOperationException("Run context is missing.");
        }

        var builder = new StringBuilder();
        Append(builder, _context.GameVersion);
        Append(builder, _context.ConfigChecksum);
        Append(builder, _context.ScoreRuleVersion);
        Append(builder, _context.ScoreRuleChecksum);
        Append(builder, _context.BaseSeed);
        Append(builder, (int)_phase);
        Append(builder, (int)_outcome);
        Append(builder, _stageIndex);
        Append(builder, _turn);
        Append(builder, _totalTurns);
        Append(builder, _clearedStages);
        Append(builder, _playerCurrentHealth);
        Append(builder, _playerMaxHealth);
        Append(builder, _playerAttack);
        Append(builder, _playerActionPoints);
        Append(builder, _playerBlock);
        Append(builder, _playerTechniqueCooldown);
        Append(builder, _playerItemCharges);
        Append(builder, _playerItemUsedThisTurn ? 1 : 0);
        Append(builder, _strikeBonus);
        Append(builder, _techniqueBonus);
        Append(builder, _guardBonus);
        Append(builder, _startTurnBlock);
        Append(builder, _itemHealBonus);
        Append(builder, _techniqueCooldownReduction);
        Append(builder, _actionPointBonus);
        Append(builder, _enemyCurrentHealth);
        Append(builder, _enemyBlock);
        Append(builder, _enemyAttackBonus);
        Append(builder, (int)_enemyIntent);
        Append(builder, _intentRandom?.State ?? 0UL);
        Append(builder, _rewardRandom?.State ?? 0UL);

        Append(builder, _actionLog.Count);
        for (var index = 0; index < _actionLog.Count; index++)
        {
            var action = _actionLog[index];
            Append(builder, action.Sequence);
            Append(builder, (int)action.ActionType);
            Append(builder, action.RewardId ?? string.Empty);
        }

        Append(builder, _stageSummaries.Count);
        for (var index = 0; index < _stageSummaries.Count; index++)
        {
            var summary = _stageSummaries[index];
            Append(builder, summary.Stage);
            Append(builder, summary.EncounterId);
            Append(builder, summary.Cleared ? 1 : 0);
            Append(builder, summary.Turns);
        }

        Append(builder, _rewardChoices.Count);
        for (var index = 0; index < _rewardChoices.Count; index++)
        {
            var choice = _rewardChoices[index];
            Append(builder, choice.AfterStage);
            Append(builder, choice.OfferedRewardIds.Count);
            for (var offeredIndex = 0; offeredIndex < choice.OfferedRewardIds.Count; offeredIndex++)
            {
                Append(builder, choice.OfferedRewardIds[offeredIndex]);
            }

            Append(builder, choice.SelectedRewardId);
        }

        Append(builder, _offeredRewardIds.Count);
        for (var index = 0; index < _offeredRewardIds.Count; index++)
        {
            Append(builder, _offeredRewardIds[index]);
        }

        Append(builder, _acquiredRewardIds.Count);
        for (var index = 0; index < _acquiredRewardIds.Count; index++)
        {
            Append(builder, _acquiredRewardIds[index]);
        }

        Append(builder, _phase == RunPhase.Terminal ? CalculateScore() : 0);
        return builder.ToString();
    }

    private void DealDamageToEnemy(int amount)
    {
        var absorbed = Minimum(_enemyBlock, amount);
        _enemyBlock -= absorbed;
        _enemyCurrentHealth = Maximum(0, _enemyCurrentHealth - (amount - absorbed));
    }

    private void DealDamageToPlayer(int amount)
    {
        var absorbed = Minimum(_playerBlock, amount);
        _playerBlock -= absorbed;
        _playerCurrentHealth = Maximum(0, _playerCurrentHealth - (amount - absorbed));
    }

    private RewardDefinition FindReward(string rewardId)
    {
        for (var index = 0; index < _config.Rewards.Count; index++)
        {
            var reward = _config.Rewards[index];
            if (string.Equals(reward.Id, rewardId, StringComparison.Ordinal))
            {
                return reward;
            }
        }

        throw new InvalidOperationException("Unknown reward ID.");
    }

    private static bool ContainsOrdinal(IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int Minimum(int left, int right)
    {
        return left < right ? left : right;
    }

    private static int Maximum(int left, int right)
    {
        return left > right ? left : right;
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

    private static void Append(StringBuilder builder, ulong value)
    {
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        builder.Append('|');
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Reset must be called before using the simulation.");
        }
    }
}
