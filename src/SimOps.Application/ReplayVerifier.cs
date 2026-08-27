using System.Globalization;
using System.Text.Json;
using SimOps.Game.Core;

namespace SimOps.Application;

public sealed class ReplayVerifier
{
    public VerificationOutput Verify(RunSubmission submission)
    {
        SubmissionValidator.Validate(submission);
        var config = GameConfig.CreateBaseline();
        var scoreRule = ScoreRule.CreateBaseline();
        var context = new RunContext(
            submission.GameVersion,
            submission.ConfigChecksum,
            submission.ScoreRuleVersion,
            submission.ScoreRuleChecksum,
            ulong.Parse(submission.BaseSeed, CultureInfo.InvariantCulture));
        var simulation = new GameSimulation(config, scoreRule);
        var observation = simulation.Reset(context);
        var events = new List<RecordedEvent>();
        AddEvent(events, "run_started", observation, new { submission.BaseSeed, submission.AgentId });
        AddEvent(events, "encounter_started", observation, new { observation.Enemy?.EncounterId });
        AddEvent(events, "turn_started", observation, new { observation.Enemy?.Intent });

        for (var index = 0; index < submission.Actions.Count; index++)
        {
            var action = submission.Actions[index];
            var before = observation;
            if (before.Phase == RunPhase.Terminal)
            {
                return Rejected("ACTION_AFTER_TERMINAL");
            }

            var step = simulation.Apply(new GameAction(action.Sequence, action.ActionType, action.RewardId));
            if (!step.Accepted)
            {
                return Rejected(step.RejectionCode == "REWARD_NOT_OFFERED" ? "REWARD_NOT_OFFERED" : "ACTION_NOT_ALLOWED");
            }

            observation = step.Observation;
            AddEvent(events, "action_selected", before, action);
            if (action.ActionType == GameActionType.ChooseReward)
            {
                AddEvent(events, "reward_selected", before, new { action.RewardId, before.OfferedRewardIds });
            }

            if (observation.Phase == RunPhase.RewardChoice && before.Phase != RunPhase.RewardChoice)
            {
                AddEvent(events, "encounter_ended", before, new { cleared = true });
                AddEvent(events, "reward_offered", observation, new { observation.OfferedRewardIds });
            }

            if (observation.Phase == RunPhase.Terminal)
            {
                var lastStage = simulation.GetCanonicalResult().StageSummaries.Last();
                AddEvent(events, "encounter_ended", before, new { cleared = lastStage.Cleared });
            }

            if (observation.Stage != before.Stage)
            {
                AddEvent(events, "encounter_started", observation, new { observation.Enemy?.EncounterId });
            }

            if (observation.Phase == RunPhase.PlayerTurn &&
                (observation.Turn != before.Turn || observation.Stage != before.Stage))
            {
                AddEvent(events, "turn_started", observation, new { observation.Enemy?.Intent });
            }
        }

        if (observation.Phase != RunPhase.Terminal)
        {
            return Rejected("RUN_NOT_TERMINAL");
        }

        var result = simulation.GetCanonicalResult();
        if (!string.Equals(result.ResultHash, submission.ClientResultHash, StringComparison.Ordinal))
        {
            return Rejected("RESULT_MISMATCH");
        }

        var summary = new VerifiedSummary(
            result.Outcome.ToString().ToLowerInvariant(),
            result.ClearedStages,
            result.TotalTurns,
            result.FinalHealth,
            result.MaxHealth,
            result.FinalScore,
            result.ResultHash);
        AddEvent(events, "run_ended", observation, summary);
        var stages = result.StageSummaries.Select(stage =>
            new VerifiedStage(stage.Stage, stage.EncounterId, stage.Cleared, stage.Turns)).ToArray();
        return new VerificationOutput(true, null, summary, stages, events);
    }

    private static VerificationOutput Rejected(string code)
    {
        return new VerificationOutput(false, code, null, Array.Empty<VerifiedStage>(), Array.Empty<RecordedEvent>());
    }

    private static void AddEvent<T>(List<RecordedEvent> events, string type, GameObservation observation, T payload)
    {
        events.Add(new RecordedEvent(
            events.Count,
            type,
            observation.Stage,
            observation.Turn,
            JsonSerializer.Serialize(payload, ContractJson.Options)));
    }
}
