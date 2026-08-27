using System;
using System.Globalization;
using SimOps.Game.Core;

var seed = ParseSeed(args);
var config = GameConfig.CreateBaseline();
var scoreRule = ScoreRule.CreateBaseline();
var context = new RunContext(
    config.GameVersion,
    config.Checksum,
    scoreRule.Version,
    scoreRule.Checksum,
    seed);

var simulation = new GameSimulation(config, scoreRule);
var observation = simulation.Reset(context);

while (observation.Phase != RunPhase.Terminal)
{
    var action = SelectAction(observation);
    var step = simulation.Apply(action);
    if (!step.Accepted)
    {
        throw new InvalidOperationException($"Runner selected an invalid action: {step.RejectionCode}");
    }

    observation = step.Observation;
}

var result = simulation.GetCanonicalResult();
Console.WriteLine($"seed={seed.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"gameVersion={config.GameVersion}");
Console.WriteLine($"configChecksum={config.Checksum}");
Console.WriteLine($"scoreRule={scoreRule.Version}");
Console.WriteLine($"scoreChecksum={scoreRule.Checksum}");
Console.WriteLine($"actions={simulation.ActionLog.Count.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"outcome={result.Outcome}");
Console.WriteLine($"clearedStages={result.ClearedStages.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"totalTurns={result.TotalTurns.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"finalHealth={result.FinalHealth.ToString(CultureInfo.InvariantCulture)}/{result.MaxHealth.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"finalScore={result.FinalScore.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"resultHash={result.ResultHash}");

static ulong ParseSeed(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return 42UL;
    }

    if (!ulong.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
    {
        throw new ArgumentException("The first argument must be an unsigned integer seed.");
    }

    return seed;
}

static GameAction SelectAction(GameObservation observation)
{
    if (observation.Phase == RunPhase.RewardChoice)
    {
        return new GameAction(
            observation.NextActionSequence,
            GameActionType.ChooseReward,
            observation.OfferedRewardIds[0]);
    }

    if (observation.Player.CurrentHealth * 3 <= observation.Player.MaxHealth &&
        Contains(observation.ValidActionTypes, GameActionType.UseItem))
    {
        return new GameAction(observation.NextActionSequence, GameActionType.UseItem);
    }

    if (observation.Enemy?.Intent == EnemyIntentType.HeavyAttack &&
        Contains(observation.ValidActionTypes, GameActionType.Guard))
    {
        return new GameAction(observation.NextActionSequence, GameActionType.Guard);
    }

    if (Contains(observation.ValidActionTypes, GameActionType.Technique))
    {
        return new GameAction(observation.NextActionSequence, GameActionType.Technique);
    }

    if (Contains(observation.ValidActionTypes, GameActionType.Strike))
    {
        return new GameAction(observation.NextActionSequence, GameActionType.Strike);
    }

    return new GameAction(observation.NextActionSequence, GameActionType.EndTurn);
}

static bool Contains(System.Collections.Generic.IReadOnlyList<GameActionType> values, GameActionType value)
{
    for (var index = 0; index < values.Count; index++)
    {
        if (values[index] == value)
        {
            return true;
        }
    }

    return false;
}
