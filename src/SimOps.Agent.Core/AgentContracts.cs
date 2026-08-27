using System;
using SimOps.Game.Core;

namespace SimOps.Agent.Core;

public enum AgentPersona
{
    Random = 0,
    Novice = 1,
    Aggressive = 2,
    Defensive = 3,
    Greedy = 4,
    Explorer = 5,
}

public sealed class AgentDefinition
{
    public AgentDefinition(string id, string version, AgentPersona persona)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Agent ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Agent version cannot be empty.", nameof(version));
        }

        Id = id;
        Version = version;
        Persona = persona;
    }

    public string Id { get; }

    public string Version { get; }

    public AgentPersona Persona { get; }
}

public sealed class AgentContext
{
    public AgentContext(AgentDefinition definition, ulong baseSeed)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        BaseSeed = baseSeed;
    }

    public AgentDefinition Definition { get; }

    public ulong BaseSeed { get; }
}

public sealed class AgentDecision
{
    public AgentDecision(
        GameAction selectedAction,
        string policyVersion,
        ulong decisionSeed,
        string reason)
    {
        SelectedAction = selectedAction ?? throw new ArgumentNullException(nameof(selectedAction));
        PolicyVersion = string.IsNullOrWhiteSpace(policyVersion)
            ? throw new ArgumentException("Policy version cannot be empty.", nameof(policyVersion))
            : policyVersion;
        DecisionSeed = decisionSeed;
        Reason = reason ?? string.Empty;
    }

    public GameAction SelectedAction { get; }

    public string PolicyVersion { get; }

    public ulong DecisionSeed { get; }

    public string Reason { get; }
}

public interface IGameAgent
{
    AgentDefinition Definition { get; }

    void Initialize(AgentContext context);

    AgentDecision Decide(GameObservation observation);

    void OnRunEnded(RunResult result);
}
