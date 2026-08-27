using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using SimOps.Game.Core;

namespace SimOps.Game.Core.Specs;

internal static class Program
{
    private const string Seed42GoldenHash = "c50ea84e374db937ec1dd17ea94428b60afdb169b4d64dd5eeec64128fa2fa78";

    private static int Main()
    {
        var specifications = new (string Name, Action Execute)[]
        {
            ("CORE-001 replay is deterministic across 1,000 seeds", ReplayIsDeterministicAcrossOneThousandSeeds),
            ("CORE-002 reward stream is independent from intent draws", RewardStreamIsIndependentFromIntentDraws),
            ("CORE-003 locale does not affect the result", LocaleDoesNotAffectResult),
            ("CORE-013 terminal runs reject additional actions", TerminalRunRejectsActions),
            ("CORE-022 invalid actions do not mutate state", InvalidActionDoesNotMutateState),
            ("CORE-024 item can be used once per turn", ItemCanBeUsedOncePerTurn),
            ("CORE-028 turn limit terminates the run", TurnLimitTerminatesRun),
            ("CORE-030 reward offers contain three distinct rewards", RewardOfferContainsThreeDistinctRewards),
            ("CORE-031 unoffered rewards are rejected without mutation", UnofferedRewardIsRejected),
            ("CORE-034 exhaustible reward pools fail config validation", ExhaustibleRewardPoolFailsValidation),
            ("CORE-041 config checksum mismatch is rejected", ConfigChecksumMismatchIsRejected),
            ("CORE-043 duplicate reward IDs fail config validation", DuplicateRewardIdsFailValidation),
            ("GOLDEN seed 42 result hash is stable", Seed42ResultMatchesGoldenHash),
        };

        var failed = 0;
        var suiteTimer = Stopwatch.StartNew();
        for (var index = 0; index < specifications.Length; index++)
        {
            var specification = specifications[index];
            var timer = Stopwatch.StartNew();
            try
            {
                specification.Execute();
                timer.Stop();
                Console.WriteLine($"PASS {specification.Name} ({timer.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms)");
            }
            catch (Exception exception)
            {
                timer.Stop();
                failed += 1;
                Console.Error.WriteLine($"FAIL {specification.Name} ({timer.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms)");
                Console.Error.WriteLine(exception.Message);
            }
        }

        suiteTimer.Stop();
        Console.WriteLine(
            $"Specs: {(specifications.Length - failed).ToString(CultureInfo.InvariantCulture)} passed, " +
            $"{failed.ToString(CultureInfo.InvariantCulture)} failed, " +
            $"{suiteTimer.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms total");
        return failed == 0 ? 0 : 1;
    }

    private static void ReplayIsDeterministicAcrossOneThousandSeeds()
    {
        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();

        for (ulong seed = 0; seed < 1_000; seed++)
        {
            var first = ExecutePolicy(config, scoreRule, seed);
            var replay = Replay(config, scoreRule, seed, first.Actions);
            SpecAssert.Equal(first.Result.ResultHash, replay.ResultHash, $"Result hash mismatch for seed {seed}");
            SpecAssert.Equal(first.Result.FinalScore, replay.FinalScore, $"Score mismatch for seed {seed}");
            SpecAssert.Equal(first.Result.Outcome, replay.Outcome, $"Outcome mismatch for seed {seed}");
        }
    }

    private static void RewardStreamIsIndependentFromIntentDraws()
    {
        var config = CreateFirstEncounterConfig(firstEncounterHealth: 1, firstEncounterAttack: 0, maximumTurns: 5);
        var scoreRule = ScoreRule.CreateBaseline();
        var context = CreateContext(config, scoreRule, 77UL);

        var immediate = new GameSimulation(config, scoreRule);
        var immediateObservation = immediate.Reset(context);
        immediateObservation = Accepted(immediate.Apply(new GameAction(0, GameActionType.Strike)));

        var delayed = new GameSimulation(config, scoreRule);
        var delayedObservation = delayed.Reset(context);
        delayedObservation = Accepted(delayed.Apply(new GameAction(0, GameActionType.EndTurn)));
        delayedObservation = Accepted(delayed.Apply(new GameAction(1, GameActionType.Strike)));

        SpecAssert.Equal(RunPhase.RewardChoice, immediateObservation.Phase, "Immediate run did not reach reward choice.");
        SpecAssert.Equal(RunPhase.RewardChoice, delayedObservation.Phase, "Delayed run did not reach reward choice.");
        SpecAssert.Equal(
            string.Join("|", immediateObservation.OfferedRewardIds),
            string.Join("|", delayedObservation.OfferedRewardIds),
            "Reward offers changed when only intent draw count changed.");
    }

    private static void LocaleDoesNotAffectResult()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ko-KR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ko-KR");
            var korean = ExecutePolicy(GameConfig.CreateBaseline(), ScoreRule.CreateBaseline(), 42UL).Result;

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = ExecutePolicy(GameConfig.CreateBaseline(), ScoreRule.CreateBaseline(), 42UL).Result;

            SpecAssert.Equal(korean.ResultHash, turkish.ResultHash, "Locale changed the result hash.");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static void TerminalRunRejectsActions()
    {
        var execution = ExecutePolicy(GameConfig.CreateBaseline(), ScoreRule.CreateBaseline(), 3UL);
        var before = execution.Simulation.GetStateHash();
        var result = execution.Simulation.Apply(
            new GameAction(execution.Simulation.ActionLog.Count, GameActionType.EndTurn));

        SpecAssert.False(result.Accepted, "Terminal action was accepted.");
        SpecAssert.Equal("RUN_TERMINAL", result.RejectionCode, "Wrong rejection code.");
        SpecAssert.Equal(before, execution.Simulation.GetStateHash(), "Terminal rejection mutated state.");
    }

    private static void InvalidActionDoesNotMutateState()
    {
        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();
        var simulation = new GameSimulation(config, scoreRule);
        simulation.Reset(CreateContext(config, scoreRule, 5UL));
        var before = simulation.GetStateHash();

        var result = simulation.Apply(new GameAction(1, GameActionType.Strike));

        SpecAssert.False(result.Accepted, "Out-of-order action was accepted.");
        SpecAssert.Equal("ACTION_SEQUENCE_INVALID", result.RejectionCode, "Wrong rejection code.");
        SpecAssert.Equal(before, simulation.GetStateHash(), "Rejected action mutated state.");
    }

    private static void ItemCanBeUsedOncePerTurn()
    {
        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();
        var simulation = new GameSimulation(config, scoreRule);
        simulation.Reset(CreateContext(config, scoreRule, 9UL));

        var first = simulation.Apply(new GameAction(0, GameActionType.UseItem));
        SpecAssert.True(first.Accepted, "First item use was rejected.");
        var beforeSecond = simulation.GetStateHash();
        var second = simulation.Apply(new GameAction(1, GameActionType.UseItem));

        SpecAssert.False(second.Accepted, "Second item use in the same turn was accepted.");
        SpecAssert.Equal("ITEM_ALREADY_USED_THIS_TURN", second.RejectionCode, "Wrong rejection code.");
        SpecAssert.Equal(beforeSecond, simulation.GetStateHash(), "Rejected item action mutated state.");
    }

    private static void TurnLimitTerminatesRun()
    {
        var config = CreateFirstEncounterConfig(firstEncounterHealth: 999, firstEncounterAttack: 0, maximumTurns: 1);
        var scoreRule = ScoreRule.CreateBaseline();
        var simulation = new GameSimulation(config, scoreRule);
        simulation.Reset(CreateContext(config, scoreRule, 11UL));

        var observation = Accepted(simulation.Apply(new GameAction(0, GameActionType.EndTurn)));
        var result = simulation.GetCanonicalResult();

        SpecAssert.Equal(RunPhase.Terminal, observation.Phase, "Turn limit did not terminate the run.");
        SpecAssert.Equal(RunOutcome.Defeat, result.Outcome, "Turn limit did not use the configured defeat rule.");
    }

    private static void RewardOfferContainsThreeDistinctRewards()
    {
        var config = CreateFirstEncounterConfig(firstEncounterHealth: 1, firstEncounterAttack: 0, maximumTurns: 5);
        var scoreRule = ScoreRule.CreateBaseline();
        var simulation = new GameSimulation(config, scoreRule);
        simulation.Reset(CreateContext(config, scoreRule, 13UL));

        var observation = Accepted(simulation.Apply(new GameAction(0, GameActionType.Strike)));
        var distinct = new HashSet<string>(observation.OfferedRewardIds, StringComparer.Ordinal);

        SpecAssert.Equal(3, observation.OfferedRewardIds.Count, "Reward offer count is not three.");
        SpecAssert.Equal(3, distinct.Count, "Reward offers are not distinct.");
    }

    private static void UnofferedRewardIsRejected()
    {
        var config = CreateFirstEncounterConfig(firstEncounterHealth: 1, firstEncounterAttack: 0, maximumTurns: 5);
        var scoreRule = ScoreRule.CreateBaseline();
        var simulation = new GameSimulation(config, scoreRule);
        simulation.Reset(CreateContext(config, scoreRule, 17UL));
        var rewardObservation = Accepted(simulation.Apply(new GameAction(0, GameActionType.Strike)));
        SpecAssert.Equal(RunPhase.RewardChoice, rewardObservation.Phase, "Run did not reach reward choice.");
        var before = simulation.GetStateHash();

        var rejected = simulation.Apply(new GameAction(1, GameActionType.ChooseReward, "not-offered"));

        SpecAssert.False(rejected.Accepted, "Unoffered reward was accepted.");
        SpecAssert.Equal("REWARD_NOT_OFFERED", rejected.RejectionCode, "Wrong rejection code.");
        SpecAssert.Equal(before, simulation.GetStateHash(), "Rejected reward mutated state.");
    }

    private static void ConfigChecksumMismatchIsRejected()
    {
        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();
        var simulation = new GameSimulation(config, scoreRule);
        var context = new RunContext(
            config.GameVersion,
            "invalid-checksum",
            scoreRule.Version,
            scoreRule.Checksum,
            19UL);

        SpecAssert.Throws<InvalidOperationException>(
            () => simulation.Reset(context),
            "Config checksum mismatch was accepted.");
    }

    private static void ExhaustibleRewardPoolFailsValidation()
    {
        var baseline = GameConfig.CreateBaseline();
        var rewards = new[]
        {
            new RewardDefinition("one", RewardCategory.Offense, RewardEffectType.Attack, 1, 1),
            new RewardDefinition("two", RewardCategory.Defense, RewardEffectType.GuardBonus, 1, 1),
            new RewardDefinition("three", RewardCategory.Sustain, RewardEffectType.ItemHealBonus, 1, 1),
        };

        SpecAssert.Throws<ArgumentException>(
            () => CreateConfig(baseline, baseline.Encounters, rewards, baseline.MaximumTurnsPerEncounter),
            "A reward pool that cannot sustain five offers passed validation.");
    }

    private static void DuplicateRewardIdsFailValidation()
    {
        var baseline = GameConfig.CreateBaseline();
        var rewards = new List<RewardDefinition>();
        for (var index = 0; index < baseline.Rewards.Count; index++)
        {
            rewards.Add(baseline.Rewards[index]);
        }

        rewards[1] = new RewardDefinition(
            rewards[0].Id,
            RewardCategory.Offense,
            RewardEffectType.Attack,
            1,
            1);

        SpecAssert.Throws<ArgumentException>(
            () => CreateConfig(baseline, baseline.Encounters, rewards, baseline.MaximumTurnsPerEncounter),
            "Duplicate reward IDs passed validation.");
    }

    private static void Seed42ResultMatchesGoldenHash()
    {
        var result = ExecutePolicy(GameConfig.CreateBaseline(), ScoreRule.CreateBaseline(), 42UL).Result;
        SpecAssert.Equal(Seed42GoldenHash, result.ResultHash, "Seed 42 golden hash changed.");
    }

    private static PolicyExecution ExecutePolicy(GameConfig config, ScoreRule scoreRule, ulong seed)
    {
        var simulation = new GameSimulation(config, scoreRule);
        var observation = simulation.Reset(CreateContext(config, scoreRule, seed));

        while (observation.Phase != RunPhase.Terminal)
        {
            var action = SelectAction(observation);
            observation = Accepted(simulation.Apply(action));
        }

        return new PolicyExecution(simulation, simulation.ActionLog, simulation.GetCanonicalResult());
    }

    private static RunResult Replay(
        GameConfig config,
        ScoreRule scoreRule,
        ulong seed,
        IReadOnlyList<GameAction> actions)
    {
        var simulation = new GameSimulation(config, scoreRule);
        simulation.Reset(CreateContext(config, scoreRule, seed));
        for (var index = 0; index < actions.Count; index++)
        {
            var step = simulation.Apply(actions[index]);
            SpecAssert.True(step.Accepted, $"Replay rejected action {index}: {step.RejectionCode}");
        }

        return simulation.GetCanonicalResult();
    }

    private static GameAction SelectAction(GameObservation observation)
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

    private static GameObservation Accepted(StepResult result)
    {
        SpecAssert.True(result.Accepted, $"Expected accepted action, got {result.RejectionCode}.");
        return result.Observation;
    }

    private static RunContext CreateContext(GameConfig config, ScoreRule scoreRule, ulong seed)
    {
        return new RunContext(
            config.GameVersion,
            config.Checksum,
            scoreRule.Version,
            scoreRule.Checksum,
            seed);
    }

    private static GameConfig CreateFirstEncounterConfig(
        int firstEncounterHealth,
        int firstEncounterAttack,
        int maximumTurns)
    {
        var baseline = GameConfig.CreateBaseline();
        var encounters = new EncounterDefinition[baseline.Encounters.Count];
        encounters[0] = new EncounterDefinition(
            "test-first",
            1,
            firstEncounterHealth,
            firstEncounterAttack,
            0,
            0,
            150,
            1,
            0,
            0,
            0,
            3);

        for (var index = 1; index < encounters.Length; index++)
        {
            encounters[index] = baseline.Encounters[index];
        }

        return CreateConfig(baseline, encounters, baseline.Rewards, maximumTurns);
    }

    private static GameConfig CreateConfig(
        GameConfig baseline,
        IReadOnlyList<EncounterDefinition> encounters,
        IReadOnlyList<RewardDefinition> rewards,
        int maximumTurns)
    {
        return new GameConfig(
            baseline.GameVersion,
            baseline.ConfigVersion + "-test",
            baseline.InitialMaxHealth,
            baseline.InitialAttack,
            baseline.BaseActionPoints,
            baseline.StrikeBonus,
            baseline.GuardAmount,
            baseline.TechniqueDamage,
            baseline.TechniqueCooldownTurns,
            baseline.InitialItemCharges,
            baseline.ItemHealAmount,
            maximumTurns,
            encounters,
            rewards);
    }

    private static bool Contains(IReadOnlyList<GameActionType> values, GameActionType value)
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

    private sealed class PolicyExecution
    {
        public PolicyExecution(
            GameSimulation simulation,
            IReadOnlyList<GameAction> actions,
            RunResult result)
        {
            Simulation = simulation;
            Actions = actions;
            Result = result;
        }

        public GameSimulation Simulation { get; }

        public IReadOnlyList<GameAction> Actions { get; }

        public RunResult Result { get; }
    }
}
