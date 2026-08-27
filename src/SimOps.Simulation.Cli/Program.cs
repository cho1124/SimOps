using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using SimOps.Agent.Core;
using SimOps.Game.Core;

var options = CliOptions.Parse(args);
var config = GameConfig.CreateBaseline();
var scoreRule = ScoreRule.CreateBaseline();
var definitions = AgentFactory.CreateDefinitions();
var summaries = new List<PersonaSummary>();
var timer = Stopwatch.StartNew();

Console.WriteLine($"SimOps synthetic baseline · {options.RunsPerAgent} runs/persona");
Console.WriteLine("persona     clear   stage3   turns    score    strike   guard    item   offense  def+sustain  entropy  builds");

for (var definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
{
    var definition = definitions[definitionIndex];
    var metrics = new PersonaMetrics(definition, config.Rewards.Count);
    for (ulong seed = 0; seed < (ulong)options.RunsPerAgent; seed++)
    {
        metrics.Add(SyntheticSimulation.Execute(config, scoreRule, definition, seed));
    }

    var summary = PersonaSummary.From(metrics);
    summaries.Add(summary);
    Console.WriteLine(summary.ToDisplayLine());
}

timer.Stop();
var totalRuns = options.RunsPerAgent * definitions.Count;
var runsPerSecond = totalRuns / timer.Elapsed.TotalSeconds;
Console.WriteLine(
    $"totalRuns={totalRuns} elapsedMs={timer.ElapsedMilliseconds} " +
    $"runsPerSecond={runsPerSecond.ToString("F1", CultureInfo.InvariantCulture)}");

if (options.JsonPath is not null)
{
    var fullPath = Path.GetFullPath(options.JsonPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
    var report = new SimulationReport(
        config.GameVersion,
        config.ConfigVersion,
        config.Checksum,
        scoreRule.Version,
        options.RunsPerAgent,
        totalRuns,
        timer.ElapsedMilliseconds,
        runsPerSecond,
        summaries);
    File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"json={fullPath}");
}

internal sealed record CliOptions(int RunsPerAgent, string? JsonPath)
{
    public static CliOptions Parse(string[] arguments)
    {
        var runs = 1_000;
        string? jsonPath = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] == "--runs" && index + 1 < arguments.Length)
            {
                if (!int.TryParse(arguments[++index], NumberStyles.None, CultureInfo.InvariantCulture, out runs) || runs <= 0)
                {
                    throw new ArgumentException("--runs must be a positive integer.");
                }
            }
            else if (arguments[index] == "--json" && index + 1 < arguments.Length)
            {
                jsonPath = arguments[++index];
            }
            else
            {
                throw new ArgumentException($"Unknown or incomplete argument: {arguments[index]}");
            }
        }

        return new CliOptions(runs, jsonPath);
    }
}

internal sealed record PersonaSummary(
    string Persona,
    double? ClearRate,
    double? Stage3PassRate,
    double? AverageTurns,
    double? AverageScore,
    double? StrikeShare,
    double? GuardShare,
    double? ItemShare,
    double? OffenseRewardShare,
    double? DefenseSustainRewardShare,
    double? RewardEntropy,
    int UniqueBuildCount,
    IReadOnlyDictionary<string, string> UndefinedMetricReasons)
{
    public static PersonaSummary From(PersonaMetrics metrics)
    {
        return new PersonaSummary(
            metrics.Agent.Id,
            metrics.ClearRate,
            metrics.Stage3PassRate,
            metrics.AverageTurns,
            metrics.AverageScore,
            metrics.ActionShare(GameActionType.Strike),
            metrics.ActionShare(GameActionType.Guard),
            metrics.ActionShare(GameActionType.UseItem),
            metrics.RewardCategoryShare("offense-"),
            metrics.RewardCategoryShare("defense-") + metrics.RewardCategoryShare("sustain-"),
            metrics.RewardEntropy,
            metrics.UniqueBuildCount,
            metrics.GetUndefinedMetricReasons());
    }

    public string ToDisplayLine()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0,-10} {1,6:P1} {2,7:P1} {3,7:F1} {4,8:F0} {5,8:P1} {6,8:P1} {7,7:P1} {8,9:P1} {9,12:P1} {10,8:F3} {11,7}",
            Persona,
            ClearRate,
            Stage3PassRate,
            AverageTurns,
            AverageScore,
            StrikeShare,
            GuardShare,
            ItemShare,
            OffenseRewardShare,
            DefenseSustainRewardShare,
            RewardEntropy,
            UniqueBuildCount);
    }
}

internal sealed record SimulationReport(
    string GameVersion,
    string ConfigVersion,
    string ConfigChecksum,
    string ScoreRuleVersion,
    int RunsPerAgent,
    int TotalRuns,
    long ElapsedMilliseconds,
    double RunsPerSecond,
    IReadOnlyList<PersonaSummary> Personas);
