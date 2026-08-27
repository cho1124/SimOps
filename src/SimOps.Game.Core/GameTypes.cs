using System;
using System.Collections.Generic;

namespace SimOps.Game.Core;

public enum RunPhase
{
    PlayerTurn = 0,
    RewardChoice = 1,
    Terminal = 2,
}

public enum RunOutcome
{
    InProgress = 0,
    Victory = 1,
    Defeat = 2,
    Aborted = 3,
    Error = 4,
}

public enum GameActionType
{
    Strike = 0,
    Guard = 1,
    Technique = 2,
    UseItem = 3,
    EndTurn = 4,
    ChooseReward = 5,
}

public enum EnemyIntentType
{
    Attack = 0,
    HeavyAttack = 1,
    Guard = 2,
    Empower = 3,
}

public enum RewardCategory
{
    Offense = 0,
    Defense = 1,
    Sustain = 2,
    Tactics = 3,
}

public enum RewardEffectType
{
    Attack = 0,
    StrikeBonus = 1,
    TechniqueBonus = 2,
    GuardBonus = 3,
    MaxHealth = 4,
    StartTurnBlock = 5,
    ItemHealBonus = 6,
    ItemCharges = 7,
    TechniqueCooldownReduction = 8,
    ActionPoints = 9,
}

public sealed class RunContext
{
    public RunContext(
        string gameVersion,
        string configChecksum,
        string scoreRuleVersion,
        string scoreRuleChecksum,
        ulong baseSeed)
    {
        GameVersion = RequireValue(gameVersion, nameof(gameVersion));
        ConfigChecksum = RequireValue(configChecksum, nameof(configChecksum));
        ScoreRuleVersion = RequireValue(scoreRuleVersion, nameof(scoreRuleVersion));
        ScoreRuleChecksum = RequireValue(scoreRuleChecksum, nameof(scoreRuleChecksum));
        BaseSeed = baseSeed;
    }

    public string GameVersion { get; }

    public string ConfigChecksum { get; }

    public string ScoreRuleVersion { get; }

    public string ScoreRuleChecksum { get; }

    public ulong BaseSeed { get; }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value;
    }
}

public sealed class GameAction
{
    public GameAction(int sequence, GameActionType actionType, string? rewardId = null)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        Sequence = sequence;
        ActionType = actionType;
        RewardId = rewardId;
    }

    public int Sequence { get; }

    public GameActionType ActionType { get; }

    public string? RewardId { get; }
}

public sealed class PlayerSnapshot
{
    internal PlayerSnapshot(
        int currentHealth,
        int maxHealth,
        int attack,
        int actionPoints,
        int block,
        int techniqueCooldown,
        int itemCharges,
        bool itemUsedThisTurn)
    {
        CurrentHealth = currentHealth;
        MaxHealth = maxHealth;
        Attack = attack;
        ActionPoints = actionPoints;
        Block = block;
        TechniqueCooldown = techniqueCooldown;
        ItemCharges = itemCharges;
        ItemUsedThisTurn = itemUsedThisTurn;
    }

    public int CurrentHealth { get; }

    public int MaxHealth { get; }

    public int Attack { get; }

    public int ActionPoints { get; }

    public int Block { get; }

    public int TechniqueCooldown { get; }

    public int ItemCharges { get; }

    public bool ItemUsedThisTurn { get; }
}

public sealed class EnemySnapshot
{
    internal EnemySnapshot(
        string encounterId,
        int currentHealth,
        int maxHealth,
        int block,
        int attackPower,
        int attackBonus,
        EnemyIntentType intent)
    {
        EncounterId = encounterId;
        CurrentHealth = currentHealth;
        MaxHealth = maxHealth;
        Block = block;
        AttackPower = attackPower;
        AttackBonus = attackBonus;
        Intent = intent;
    }

    public string EncounterId { get; }

    public int CurrentHealth { get; }

    public int MaxHealth { get; }

    public int Block { get; }

    public int AttackPower { get; }

    public int AttackBonus { get; }

    public EnemyIntentType Intent { get; }
}

public sealed class GameObservation
{
    internal GameObservation(
        RunPhase phase,
        RunOutcome outcome,
        int stage,
        int turn,
        int totalTurns,
        int nextActionSequence,
        PlayerSnapshot player,
        EnemySnapshot? enemy,
        IReadOnlyList<GameActionType> validActionTypes,
        IReadOnlyList<string> offeredRewardIds)
    {
        Phase = phase;
        Outcome = outcome;
        Stage = stage;
        Turn = turn;
        TotalTurns = totalTurns;
        NextActionSequence = nextActionSequence;
        Player = player;
        Enemy = enemy;
        ValidActionTypes = validActionTypes;
        OfferedRewardIds = offeredRewardIds;
    }

    public RunPhase Phase { get; }

    public RunOutcome Outcome { get; }

    public int Stage { get; }

    public int Turn { get; }

    public int TotalTurns { get; }

    public int NextActionSequence { get; }

    public PlayerSnapshot Player { get; }

    public EnemySnapshot? Enemy { get; }

    public IReadOnlyList<GameActionType> ValidActionTypes { get; }

    public IReadOnlyList<string> OfferedRewardIds { get; }
}

public sealed class StepResult
{
    internal StepResult(
        bool accepted,
        string? rejectionCode,
        GameObservation observation,
        IReadOnlyList<string> domainEvents)
    {
        Accepted = accepted;
        RejectionCode = rejectionCode;
        Observation = observation;
        DomainEvents = domainEvents;
    }

    public bool Accepted { get; }

    public string? RejectionCode { get; }

    public GameObservation Observation { get; }

    public IReadOnlyList<string> DomainEvents { get; }
}

public sealed class StageSummary
{
    internal StageSummary(int stage, string encounterId, bool cleared, int turns)
    {
        Stage = stage;
        EncounterId = encounterId;
        Cleared = cleared;
        Turns = turns;
    }

    public int Stage { get; }

    public string EncounterId { get; }

    public bool Cleared { get; }

    public int Turns { get; }
}

public sealed class RewardChoiceRecord
{
    internal RewardChoiceRecord(int afterStage, IReadOnlyList<string> offeredRewardIds, string selectedRewardId)
    {
        AfterStage = afterStage;
        OfferedRewardIds = offeredRewardIds;
        SelectedRewardId = selectedRewardId;
    }

    public int AfterStage { get; }

    public IReadOnlyList<string> OfferedRewardIds { get; }

    public string SelectedRewardId { get; }
}

public sealed class RunResult
{
    internal RunResult(
        RunOutcome outcome,
        int clearedStages,
        int totalTurns,
        int finalHealth,
        int maxHealth,
        int finalScore,
        IReadOnlyList<StageSummary> stageSummaries,
        IReadOnlyList<RewardChoiceRecord> rewardChoices,
        string resultHash)
    {
        Outcome = outcome;
        ClearedStages = clearedStages;
        TotalTurns = totalTurns;
        FinalHealth = finalHealth;
        MaxHealth = maxHealth;
        FinalScore = finalScore;
        StageSummaries = stageSummaries;
        RewardChoices = rewardChoices;
        ResultHash = resultHash;
    }

    public RunOutcome Outcome { get; }

    public int ClearedStages { get; }

    public int TotalTurns { get; }

    public int FinalHealth { get; }

    public int MaxHealth { get; }

    public int FinalScore { get; }

    public IReadOnlyList<StageSummary> StageSummaries { get; }

    public IReadOnlyList<RewardChoiceRecord> RewardChoices { get; }

    public string ResultHash { get; }
}
