namespace Orchestrator.Domain;

/// <summary>
/// The ceilings that make every cycle of the graph terminate.
/// </summary>
/// <remarks>
/// This type is a cost control before it is a design one. A review loop without a ceiling
/// running against <c>claude -p</c> burns the five-hour Pro plan window in a single run
/// (ADR-001, ADR-003), and it burns it exactly when it is most needed: while debugging.
/// </remarks>
public sealed record GraphPolicy
{
    public static GraphPolicy Default { get; } = new();

    /// <summary>
    /// How many times one node may be entered before the run gives up on it.
    /// </summary>
    /// <remarks>
    /// Three is the orchestrator's own ceiling and it sits above the <c>maxTurns</c> that
    /// Claude Code enforces inside a single agent turn (ADR-011). Two independent ceilings,
    /// because an agent hung inside its own turn and a cycle that will not converge are
    /// different failures and only one of them is ours to catch.
    /// </remarks>
    public int MaximumAttemptsPerNode { get; init; } = 3;

    /// <summary>
    /// How many times the gate may be re-asked while it answers <c>indexing</c>.
    /// </summary>
    /// <remarks>
    /// <strong>This is a fourth cycle, and ADR-003 does not enumerate it.</strong> Treating
    /// <c>indexing</c> as "wait and ask again" is mandatory — it is never an approval — but
    /// "wait and ask again" with no ceiling is a hang: block 2 produced exactly that failure,
    /// a Roslyn session that answered <c>indexing</c> forever because a JSON-RPC call had been
    /// silently rejected (ADR-013). Running out of waits is a terminal failure with the
    /// server's own <c>statusDetail</c> in the trace, which is the difference between a run
    /// that stops and a run that hangs.
    /// </remarks>
    public int MaximumIndexWaitAttempts { get; init; } = 10;

    /// <summary>
    /// How long to wait between two <c>indexing</c> answers.
    /// </summary>
    /// <remarks>
    /// Zero is allowed and means "ask again immediately". It is what the suite uses: the
    /// behaviour worth testing is the ceiling on <see cref="MaximumIndexWaitAttempts"/>, not
    /// the sleep, and a test that actually waits is a test that stops being run.
    /// </remarks>
    public TimeSpan IndexWaitDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a single agent invocation may take before the runner is asked to stop it.</summary>
    public TimeSpan AgentTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public void Validate()
    {
        if (MaximumAttemptsPerNode < 1)
        {
            throw new InvalidOperationException("MaximumAttemptsPerNode has to be at least 1.");
        }

        if (MaximumIndexWaitAttempts < 1)
        {
            throw new InvalidOperationException("MaximumIndexWaitAttempts has to be at least 1.");
        }

        if (IndexWaitDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("IndexWaitDelay cannot be negative.");
        }

        if (AgentTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("AgentTimeout has to be positive.");
        }
    }
}

/// <summary>What the graph does with a gate verdict.</summary>
public enum ReviewAction
{
    /// <summary>No blocking diagnostics: move on to the next layer.</summary>
    Advance = 0,

    /// <summary>Blocking diagnostics, and it is still worth another iteration.</summary>
    SendBackToAgent = 1,

    /// <summary>Blocking diagnostics, and the run has to stop.</summary>
    Terminate = 2,
}

/// <summary>The decision itself, with the delta that justifies it so the log can show the reasoning.</summary>
public readonly record struct ReviewDecision(ReviewAction Action, DiagnosticDelta Delta, RunTermination? Termination);

/// <summary>
/// The conditional edge of the graph, as a pure function: given what the gate just said and
/// what it said last time, does the run advance, loop, or stop?
/// </summary>
/// <remarks>
/// Kept out of the runner so it can be exercised on its own, with no agent, no gateway and no
/// clock. Two of the three mandatory termination paths of ADR-003 are decided here; the third,
/// terminal failure, is raised by whatever component discovers it.
/// </remarks>
public static class ReviewPolicy
{
    /// <param name="state">The state <em>before</em> this verdict is recorded, so the previous verdict is still the last one.</param>
    /// <param name="layer">The layer whose gate just answered.</param>
    /// <param name="verdict">What the gate found. Must come from a <see cref="GateStatus.Ready"/> answer.</param>
    /// <param name="policy">The ceilings.</param>
    public static ReviewDecision Decide(GraphState state, Layer layer, DiagnosticSet verdict, GraphPolicy policy)
    {
        var previous = state.LastVerdictOf(layer);
        var delta = verdict.CompareWith(previous ?? DiagnosticSet.Empty);

        if (!verdict.HasBlockingItems)
        {
            return new ReviewDecision(ReviewAction.Advance, delta, null);
        }

        // Non-progress before the attempt limit, on purpose: an agent handing back the same
        // errors twice is not going to fix them on the third try, and the whole point of the
        // check is to stop paying for that turn.
        if (previous is not null && previous.Fingerprint() == verdict.Fingerprint())
        {
            return new ReviewDecision(
                ReviewAction.Terminate,
                delta,
                RunTermination.Failure(
                    TerminationReason.NoProgress,
                    $"The {LayerCatalog.AgentNameOf(layer)} agent returned the same {verdict.Total} diagnostic(s) twice in a row.",
                    NodeId.GateOf(layer),
                    layer));
        }

        var implementationNode = NodeId.ImplementationOf(layer);

        if (state.AttemptsOf(implementationNode) >= policy.MaximumAttemptsPerNode)
        {
            return new ReviewDecision(
                ReviewAction.Terminate,
                delta,
                RunTermination.Failure(
                    TerminationReason.IterationLimitReached,
                    $"The {LayerCatalog.AgentNameOf(layer)} agent ran {policy.MaximumAttemptsPerNode} time(s) and the gate still reports "
                    + $"{verdict.BlockingItems.Count} blocking diagnostic(s).",
                    implementationNode,
                    layer));
        }

        return new ReviewDecision(ReviewAction.SendBackToAgent, delta, null);
    }
}
