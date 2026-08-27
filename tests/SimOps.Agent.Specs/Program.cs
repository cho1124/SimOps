using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using SimOps.Agent.Core;
using SimOps.Game.Core;

namespace SimOps.Agent.Specs;

internal static class Program
{
    private static int Main()
    {
        var specifications = new (string Name, Action Execute)[]
        {
            ("AGENT-001 six personas always select valid actions", SixPersonasSelectValidActions),
            ("AGENT-002 same agent seed produces the same action log", SameSeedProducesSameActionLog),
            ("AGENT-003 persona behavior signals are distinct", PersonaSignalsAreDistinct),
            ("AGENT-004 headless throughput exceeds initial target", ThroughputExceedsInitialTarget),
            ("METRIC-001 empty denominators are null and versions cannot mix", MetricBoundariesAreEnforced),
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
            $"Agent Specs: {(specifications.Length - failed).ToString(CultureInfo.InvariantCulture)} passed, " +
            $"{failed.ToString(CultureInfo.InvariantCulture)} failed, " +
            $"{suiteTimer.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms total");
        return failed == 0 ? 0 : 1;
    }

    private static void SixPersonasSelectValidActions()
    {
        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();
        var definitions = AgentFactory.CreateDefinitions();
        SpecAssert.Equal(6, definitions.Count, "The baseline persona count changed.");

        for (var definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            for (ulong seed = 0; seed < 1_000; seed++)
            {
                var run = SyntheticSimulation.Execute(config, scoreRule, definitions[definitionIndex], seed);
                SpecAssert.True(run.Result.Outcome != RunOutcome.InProgress, "A synthetic run did not terminate.");
            }
        }
    }

    private static void SameSeedProducesSameActionLog()
    {
        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();
        var definitions = AgentFactory.CreateDefinitions();

        for (var definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            for (ulong seed = 0; seed < 100; seed++)
            {
                var first = SyntheticSimulation.Execute(config, scoreRule, definitions[definitionIndex], seed);
                var second = SyntheticSimulation.Execute(config, scoreRule, definitions[definitionIndex], seed);
                SpecAssert.Equal(first.Result.ResultHash, second.Result.ResultHash, "Agent result hash changed.");
                SpecAssert.Equal(first.Actions.Count, second.Actions.Count, "Agent action count changed.");
                for (var actionIndex = 0; actionIndex < first.Actions.Count; actionIndex++)
                {
                    SpecAssert.Equal(first.Actions[actionIndex].ActionType, second.Actions[actionIndex].ActionType, "Action type changed.");
                    SpecAssert.Equal(first.Actions[actionIndex].RewardId, second.Actions[actionIndex].RewardId, "Reward choice changed.");
                }
            }
        }
    }

    private static void PersonaSignalsAreDistinct()
    {
        var metrics = Measure(1_000);
        var random = metrics[AgentPersona.Random];
        var novice = metrics[AgentPersona.Novice];
        var aggressive = metrics[AgentPersona.Aggressive];
        var defensive = metrics[AgentPersona.Defensive];
        var greedy = metrics[AgentPersona.Greedy];
        var explorer = metrics[AgentPersona.Explorer];

        SpecAssert.True(
            aggressive.ActionShare(GameActionType.Technique) > defensive.ActionShare(GameActionType.Technique),
            "Aggressive did not prefer Technique over Defensive.");
        SpecAssert.True(
            aggressive.RewardCategoryShare("offense-") > defensive.RewardCategoryShare("offense-"),
            "Aggressive did not prefer Offense rewards.");
        SpecAssert.True(
            defensive.ActionShare(GameActionType.Guard) > aggressive.ActionShare(GameActionType.Guard),
            "Defensive did not prefer Guard.");
        SpecAssert.True(
            defensive.RewardCategoryShare("defense-") + defensive.RewardCategoryShare("sustain-") >
            aggressive.RewardCategoryShare("defense-") + aggressive.RewardCategoryShare("sustain-"),
            "Defensive did not prefer Defense/Sustain rewards.");
        SpecAssert.True(greedy.ClearRate > novice.ClearRate, "Greedy did not outperform Novice.");
        SpecAssert.True(novice.ClearRate > random.ClearRate, "Novice did not outperform Random.");
        SpecAssert.True(explorer.RewardEntropy > aggressive.RewardEntropy, "Explorer reward entropy was not higher than Aggressive.");
        SpecAssert.True(explorer.UniqueBuildCount > aggressive.UniqueBuildCount, "Explorer did not produce more unique builds.");
    }

    private static void ThroughputExceedsInitialTarget()
    {
        const int runsPerAgent = 500;
        var timer = Stopwatch.StartNew();
        var metrics = Measure(runsPerAgent);
        timer.Stop();
        var totalRuns = metrics.Count * runsPerAgent;
        var runsPerSecond = totalRuns / timer.Elapsed.TotalSeconds;
        Console.WriteLine($"  measuredRunsPerSecond={runsPerSecond.ToString("F1", CultureInfo.InvariantCulture)}");
        SpecAssert.True(runsPerSecond >= 100d, "Headless throughput was below 100 runs/second.");
    }

    private static Dictionary<AgentPersona, PersonaMetrics> Measure(int runsPerAgent)
    {
        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();
        var definitions = AgentFactory.CreateDefinitions();
        var result = new Dictionary<AgentPersona, PersonaMetrics>();

        for (var definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            var metrics = new PersonaMetrics(definition, config.Rewards.Count);
            for (ulong seed = 0; seed < (ulong)runsPerAgent; seed++)
            {
                metrics.Add(SyntheticSimulation.Execute(config, scoreRule, definition, seed));
            }

            result.Add(definition.Persona, metrics);
        }

        return result;
    }

    private static void MetricBoundariesAreEnforced()
    {
        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();
        var definition = AgentFactory.CreateDefinitions()[0];
        var metrics = new PersonaMetrics(definition, config.Rewards.Count);
        SpecAssert.Equal<double?>(null, metrics.ClearRate, "An empty clear-rate denominator should be null.");
        SpecAssert.Equal<double?>(null, metrics.Stage3PassRate, "An empty stage denominator should be null.");
        SpecAssert.Equal<double?>(null, metrics.RewardEntropy, "Empty reward entropy should be null.");
        SpecAssert.Equal(
            "NO_STAGE_ENTRIES",
            metrics.GetUndefinedMetricReasons()["stage_pass_rate.v1:stage=3"],
            "An undefined metric should include a reason.");

        metrics.Add(SyntheticSimulation.Execute(config, scoreRule, definition, 1UL));
        var differentVersion = new AgentDefinition(definition.Id, "2.0.0", definition.Persona);
        var incompatibleRun = SyntheticSimulation.Execute(config, scoreRule, differentVersion, 1UL);
        SpecAssert.Throws<InvalidOperationException>(
            () => metrics.Add(incompatibleRun),
            "Different Agent Versions were mixed in one metric accumulator.");
    }
}
