namespace Orchestrator.Domain;

/// <summary>How an agent invocation ended, from the runner's point of view.</summary>
/// <remarks>
/// Note what is <em>not</em> here: whether the code is correct. The runner does not know and
/// must not claim to. Trusting an agent's own report that it compiled is the problem this
/// project exists to solve (ADR-004); the verdict comes from the gate, afterwards.
/// </remarks>
public enum AgentCompletion
{
    /// <summary>The agent finished its turn on its own.</summary>
    Completed = 0,

    /// <summary>Claude Code stopped it at the <c>maxTurns</c> of its frontmatter (ADR-011).</summary>
    TurnLimitReached = 1,

    /// <summary>The orchestrator stopped it: it was taking longer than the run allows.</summary>
    TimedOut = 2,

    /// <summary>The invocation itself failed — a non-zero exit, unreadable output.</summary>
    Errored = 3,
}

/// <summary>What the graph asks an agent to do.</summary>
public sealed record AgentInvocation
{
    /// <summary>Matches the <c>name</c> in the frontmatter of a template under <c>templates/agents/</c>.</summary>
    public required string AgentName { get; init; }

    public required NodeId Node { get; init; }

    /// <summary>1 on the first pass; higher means this is a review iteration.</summary>
    public required int Attempt { get; init; }

    public required string Prompt { get; init; }

    /// <summary>
    /// The diagnostics this iteration has to fix. Empty on the first pass.
    /// </summary>
    /// <remarks>
    /// Carried structurally as well as inside <see cref="Prompt"/> so the adapter can render
    /// them however the prompt budget requires, and so the observer can log which errors an
    /// iteration was handed without re-parsing prose.
    /// </remarks>
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
}

/// <summary>What came back.</summary>
public sealed record AgentOutcome
{
    public required AgentCompletion Completion { get; init; }

    /// <summary>
    /// What the agent said, kept for the log and for a human reading a failed run.
    /// </summary>
    /// <remarks>
    /// <strong>The graph never branches on this text.</strong> That is the design decision
    /// that makes the whole pipeline testable (ADR-014): a layer agent does not report to the
    /// graph, it writes files, and the gate is what speaks. Free-form text crosses into a
    /// structure at exactly one node — the spec analyzer, whose output <em>is</em> the plan —
    /// and that one parse has recorded fixtures of its own.
    /// </remarks>
    public required string Transcript { get; init; }

    /// <summary>Why the invocation failed, when <see cref="Completion"/> is not <see cref="AgentCompletion.Completed"/>.</summary>
    public string? FailureDetail { get; init; }

    public bool IsUsable => Completion == AgentCompletion.Completed;

    public static AgentOutcome Completed(string transcript) => new()
    {
        Completion = AgentCompletion.Completed,
        Transcript = transcript,
    };

    public static AgentOutcome Failed(AgentCompletion completion, string failureDetail, string transcript = "") => new()
    {
        Completion = completion,
        Transcript = transcript,
        FailureDetail = failureDetail,
    };
}

/// <summary>
/// The graph's window into Claude Code. Implemented by <c>Orchestrator.Agents</c> over
/// <c>claude -p</c>, and by <c>FakeAgentRunner</c> in the suite.
/// </summary>
/// <remarks>
/// If the graph cannot tell a real agent from a fake, golden rule 3 of AI.md holds by
/// construction rather than by discipline. There could be a human on the other side.
/// </remarks>
public interface IAgentRunner
{
    Task<AgentOutcome> RunAsync(AgentInvocation invocation, CancellationToken cancellationToken);
}
