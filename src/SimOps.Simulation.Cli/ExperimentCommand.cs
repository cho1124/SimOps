using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SimOps.Experiments;

internal static class ExperimentCommand
{
    public static int Execute(string[] args)
    {
        if (args.Length != 4 || args[2] != "--json")
        {
            Console.Error.WriteLine("Usage: --experiment <definition.json> --json <report.json>");
            return 2;
        }
        try
        {
            var definition = ExperimentJson.Parse(File.ReadAllText(args[1]));
            Console.WriteLine($"Experiment {definition.ExperimentId}: {definition.Variants.Count * definition.AgentIds.Count * definition.RunsPerCell} planned runs");
            var timer = Stopwatch.StartNew();
            var report = ExperimentRunner.Execute(definition, Console.WriteLine);
            timer.Stop();
            var output = Path.GetFullPath(args[3]);
            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions(ExperimentJson.Options) { WriteIndented = true }));
            foreach (var comparison in report.Comparisons)
            {
                var primary = comparison.NoviceMaeDifference;
                Console.WriteLine(FormattableString.Invariant(
                    $"{comparison.VariantId}: primary delta={primary.Difference:F6} CI95=[{primary.Lower95:F6}, {primary.Upper95:F6}] candidate={comparison.EligibleForHumanReview}"));
                foreach (var failed in comparison.Checks.Where(check => !check.Passed))
                    Console.WriteLine($"  FAIL {failed.Key}: {failed.Observed?.ToString("F6", CultureInfo.InvariantCulture) ?? "undefined"}; {failed.Requirement}");
            }
            Console.WriteLine($"reviewCandidates={(report.ReviewCandidateIds.Count == 0 ? "none" : string.Join(",", report.ReviewCandidateIds))}; publication=not_published");
            Console.WriteLine($"resultDigest={report.ResultDigest} replayChecked={report.ReplayCheckedRuns} elapsedMs={timer.ElapsedMilliseconds}");
            Console.WriteLine($"json={output}");
            return 0; // A rejected hypothesis is a valid experiment result, not an execution error.
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Experiment failed: {exception.Message}");
            return 1;
        }
    }
}
