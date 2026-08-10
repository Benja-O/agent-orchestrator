namespace Orchestrator.Agents;

/// <summary>
/// The Claude Code layer failed as infrastructure: the executable is missing, the workspace
/// could not be prepared, or the process answered something unreadable.
/// </summary>
/// <remarks>
/// An agent that writes code which does not compile is <em>not</em> this. That is a state of the
/// graph, carried by <see cref="Orchestrator.Domain.AgentOutcome"/> and decided on by the gate.
/// This type is for the cases where there is no run to speak of (AI.md, errors and results).
/// </remarks>
public sealed class AgentRunnerException : Exception
{
    public AgentRunnerException(string message) : base(message)
    {
    }

    public AgentRunnerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
