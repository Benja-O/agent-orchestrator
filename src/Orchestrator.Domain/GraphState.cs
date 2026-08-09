using System.Collections.Immutable;

namespace Orchestrator.Domain;

/// <summary>
/// The whole state of a run, replaced rather than mutated on every transition.
/// </summary>
/// <remarks>
/// Immutability is not decoration here. It is what lets the observer log the full trace of a
/// run without defensive copies, and what makes a terminal failure able to say where the run
/// got stuck instead of only that it did (AI.md, graph model).
/// </remarks>
public sealed record GraphState
{
    public required RunId RunId { get; init; }

    public required SpecDocument Spec { get; init; }

    public required NodeId CurrentNode { get; init; }

    /// <summary>Null until the spec analyzer produced one.</summary>
    public TaskPlan? Plan { get; init; }

    /// <summary>How many times each node has been entered. The per-node attempt limit reads this.</summary>
    public ImmutableDictionary<NodeId, int> AttemptsByNode { get; init; } = ImmutableDictionary<NodeId, int>.Empty;

    /// <summary>The last verdict the gate gave for each layer. Non-progress detection compares against this.</summary>
    public ImmutableDictionary<Layer, DiagnosticSet> LastVerdictByLayer { get; init; } =
        ImmutableDictionary<Layer, DiagnosticSet>.Empty;

    /// <summary>Every node entered, in order. The trace a failed run is read with.</summary>
    public ImmutableList<NodeId> Trace { get; init; } = [];

    /// <summary>Set exactly once, when the run stops. Null while it is still running.</summary>
    public RunTermination? Termination { get; init; }

    public bool HasTerminated => Termination is not null;

    public static GraphState Start(RunId runId, SpecDocument spec) => new()
    {
        RunId = runId,
        Spec = spec,
        CurrentNode = NodeId.SpecAnalysis,
    };

    public int AttemptsOf(NodeId node) => AttemptsByNode.TryGetValue(node, out var attempts) ? attempts : 0;

    public DiagnosticSet? LastVerdictOf(Layer layer) =>
        LastVerdictByLayer.TryGetValue(layer, out var verdict) ? verdict : null;

    /// <summary>Moves to <paramref name="node"/> and counts the visit as an attempt of that node.</summary>
    public GraphState Entering(NodeId node) => this with
    {
        CurrentNode = node,
        AttemptsByNode = AttemptsByNode.SetItem(node, AttemptsOf(node) + 1),
        Trace = Trace.Add(node),
    };

    public GraphState WithPlan(TaskPlan plan) => this with { Plan = plan };

    public GraphState WithVerdict(Layer layer, DiagnosticSet diagnostics) => this with
    {
        LastVerdictByLayer = LastVerdictByLayer.SetItem(layer, diagnostics),
    };

    public GraphState TerminatedWith(RunTermination termination) => this with
    {
        CurrentNode = termination.Reason == TerminationReason.Completed ? NodeId.Completed : NodeId.Failed,
        Termination = termination,
    };
}

/// <summary>
/// Why a run stopped. Three of the four are the mandatory termination paths of ADR-003.
/// </summary>
/// <remarks>
/// No framework provides these, so they are written by hand and enumerated here so that a run
/// that stops for a reason nobody named is impossible to express.
/// </remarks>
public enum TerminationReason
{
    /// <summary>Every layer passed its gate.</summary>
    Completed = 0,

    /// <summary>A node was entered more times than the policy allows.</summary>
    IterationLimitReached = 1,

    /// <summary>The agent handed back the same set of diagnostics twice in a row.</summary>
    NoProgress = 2,

    /// <summary>Something the graph cannot route around: an unusable agent, a gate that never settled, a spec that cannot be decomposed.</summary>
    TerminalFailure = 3,
}

/// <summary>The end of a run, with enough context to explain it without reading the log.</summary>
public sealed record RunTermination
{
    public required TerminationReason Reason { get; init; }

    public required string Detail { get; init; }

    public NodeId? Node { get; init; }

    public Layer? Layer { get; init; }

    public bool IsSuccess => Reason == TerminationReason.Completed;

    public static RunTermination Completed() => new()
    {
        Reason = TerminationReason.Completed,
        Detail = "Every layer passed its gate with no blocking diagnostics.",
    };

    public static RunTermination Failure(TerminationReason reason, string detail, NodeId? node = null, Layer? layer = null)
    {
        if (reason == TerminationReason.Completed)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A failure cannot be a completion.");
        }

        return new RunTermination { Reason = reason, Detail = detail, Node = node, Layer = layer };
    }
}
