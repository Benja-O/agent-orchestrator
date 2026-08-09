using System.Globalization;

namespace Orchestrator.Domain;

/// <summary>
/// One thing that happened during a run.
/// </summary>
/// <remarks>
/// <para>
/// With no UI and no persistence (ADR-007), the log is the only window into the graph and it
/// is what gets projected during the demo. That makes its shape a product decision rather
/// than an infrastructure one (ADR-015), and it has to be two things at once: readable by a
/// person watching it live, and parseable afterwards.
/// </para>
/// <para>
/// Both readings are served from the same object. <see cref="Event"/> and the typed
/// properties are the machine side; <see cref="Summary"/> is the console line. Keeping the
/// rendering on the event itself is what stops the two from drifting apart, which is the way
/// a dual-purpose log usually rots.
/// </para>
/// </remarks>
public abstract record RunEvent
{
    public required RunId RunId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The stable machine name of this event kind.</summary>
    public abstract string Event { get; }

    /// <summary>The single line a person reads while the pipeline runs.</summary>
    public abstract string Summary { get; }
}

/// <summary>The run began, and the spec passed its own invariants.</summary>
public sealed record RunStarted : RunEvent
{
    public required string SpecPath { get; init; }

    public required IReadOnlyList<string> BusinessRules { get; init; }

    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }

    public override string Event => "run-started";

    public override string Summary =>
        $"Run {RunId} started on {SpecPath} — {BusinessRules.Count} business rule(s), {AcceptanceCriteria.Count} acceptance criterion(a).";
}

/// <summary>The spec analyzer produced a plan and it parsed.</summary>
public sealed record PlanProduced : RunEvent
{
    public required int TaskCount { get; init; }

    public required IReadOnlyDictionary<string, int> TaskCountByLayer { get; init; }

    /// <summary>Criteria of the spec that no task claims. Not fatal, but the first thing to look at when the output is incomplete.</summary>
    public required IReadOnlyList<string> CriteriaNotCovered { get; init; }

    public override string Event => "plan-produced";

    public override string Summary
    {
        get
        {
            var byLayer = string.Join(", ", TaskCountByLayer.Select(entry => $"{entry.Key} {entry.Value}"));
            var uncovered = CriteriaNotCovered.Count == 0
                ? "every criterion covered"
                : $"not covered: {string.Join(", ", CriteriaNotCovered)}";

            return $"Plan: {TaskCount} task(s) ({byLayer}) — {uncovered}.";
        }
    }
}

/// <summary>A node of the graph was entered.</summary>
public sealed record NodeEntered : RunEvent
{
    public required NodeId Node { get; init; }

    public required int Attempt { get; init; }

    public string? Layer { get; init; }

    /// <summary>
    /// The <c>RN-nn</c> this layer is on the hook for, so the log says which rule is being
    /// implemented where and not only which node is running (ADR-012, ADR-015).
    /// </summary>
    public IReadOnlyList<string> BusinessRules { get; init; } = [];

    public override string Event => "node-entered";

    public override string Summary
    {
        get
        {
            var attempt = Attempt > 1 ? $" (attempt {Attempt})" : string.Empty;
            var rules = BusinessRules.Count > 0 ? $" — {string.Join(", ", BusinessRules)}" : string.Empty;
            return $"→ {Node}{attempt}{rules}";
        }
    }
}

/// <summary>An agent was asked to work.</summary>
public sealed record AgentInvoked : RunEvent
{
    public required NodeId Node { get; init; }

    public required string AgentName { get; init; }

    public required int Attempt { get; init; }

    /// <summary>How many diagnostics this iteration was handed to fix. Zero on the first pass.</summary>
    public required int DiagnosticsHandedOver { get; init; }

    public override string Event => "agent-invoked";

    public override string Summary => DiagnosticsHandedOver == 0
        ? $"  {AgentName}: first pass."
        : $"  {AgentName}: fixing {DiagnosticsHandedOver} diagnostic(s).";
}

/// <summary>An agent handed control back.</summary>
public sealed record AgentReturned : RunEvent
{
    public required NodeId Node { get; init; }

    public required string AgentName { get; init; }

    public required AgentCompletion Completion { get; init; }

    public required TimeSpan Duration { get; init; }

    public string? FailureDetail { get; init; }

    public override string Event => "agent-returned";

    public override string Summary
    {
        get
        {
            var elapsed = Duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
            return Completion == AgentCompletion.Completed
                ? $"  {AgentName}: done in {elapsed}s."
                : $"  {AgentName}: {Completion} after {elapsed}s — {FailureDetail}";
        }
    }
}

/// <summary>
/// The gate said <c>indexing</c>, so the run waited instead of reading it as approval.
/// </summary>
/// <remarks>
/// Logged rather than swallowed because an <c>indexing</c> that never ends is the most
/// expensive silent failure this project has (ADR-013). If the run stalls, these lines are
/// what say so.
/// </remarks>
public sealed record GateWaitingForIndex : RunEvent
{
    public required string Scope { get; init; }

    public required int WaitAttempt { get; init; }

    public required int MaximumWaitAttempts { get; init; }

    public string? StatusDetail { get; init; }

    public override string Event => "gate-waiting-for-index";

    public override string Summary =>
        $"  gate: indexing ({WaitAttempt}/{MaximumWaitAttempts}) — {StatusDetail ?? "no detail given"}";
}

/// <summary>The gate gave a usable verdict.</summary>
public sealed record GateEvaluated : RunEvent
{
    public required string Layer { get; init; }

    public required string Scope { get; init; }

    public required int Total { get; init; }

    public required bool Truncated { get; init; }

    public required int ErrorCount { get; init; }

    public required int WarningCount { get; init; }

    /// <summary>The fingerprint non-progress detection compares. In the log so a stuck run is readable after the fact.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>A few blocking diagnostics, formatted, so the console shows what broke without opening the JSONL.</summary>
    public required IReadOnlyList<string> BlockingSample { get; init; }

    public override string Event => "gate-evaluated";

    public override string Summary
    {
        get
        {
            if (ErrorCount == 0)
            {
                return $"  gate [{Scope}]: clean{(WarningCount > 0 ? $", {WarningCount} warning(s)" : string.Empty)}.";
            }

            var truncated = Truncated ? $" of {Total}, truncated" : string.Empty;
            var sample = BlockingSample.Count == 0
                ? string.Empty
                : Environment.NewLine + string.Join(Environment.NewLine, BlockingSample.Select(line => "      " + line));

            return $"  gate [{Scope}]: {ErrorCount} error(s){truncated}.{sample}";
        }
    }
}

/// <summary>What one iteration of the review loop actually changed.</summary>
/// <remarks>
/// The event the demo is really about. "The agent ran again" says nothing; "the agent removed
/// four errors and introduced one" is the pipeline visibly working — or visibly not.
/// </remarks>
public sealed record ReviewIterationEvaluated : RunEvent
{
    public required string Layer { get; init; }

    public required int Iteration { get; init; }

    public required int Resolved { get; init; }

    public required int Introduced { get; init; }

    public required int Persisting { get; init; }

    public required ReviewAction Action { get; init; }

    public override string Event => "review-iteration";

    public override string Summary =>
        $"  review {Layer} #{Iteration}: -{Resolved} resolved, +{Introduced} new, {Persisting} still there → {Action}.";
}

/// <summary>The run stopped, one way or another.</summary>
public sealed record RunTerminated : RunEvent
{
    public required TerminationReason Reason { get; init; }

    public required string Detail { get; init; }

    public required TimeSpan Duration { get; init; }

    public string? Node { get; init; }

    public string? Layer { get; init; }

    /// <summary>Every node entered, in order. The trace that says where a failed run got stuck.</summary>
    public required IReadOnlyList<string> Trace { get; init; }

    public override string Event => "run-terminated";

    public override string Summary
    {
        get
        {
            var elapsed = Duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
            return Reason == TerminationReason.Completed
                ? $"✓ Run {RunId} completed in {elapsed}s."
                : $"✗ Run {RunId} stopped after {elapsed}s — {Reason}: {Detail}";
        }
    }
}

/// <summary>Where run events go.</summary>
/// <remarks>
/// Synchronous on purpose: writing a line must never be a reason for the graph to await, and
/// an observer that needs to do slow work is an observer that should be buffering.
/// </remarks>
public interface IRunObserver
{
    void Observe(RunEvent runEvent);
}

/// <summary>An observer that drops everything, for callers that do not want a log.</summary>
public sealed class NullRunObserver : IRunObserver
{
    public static NullRunObserver Instance { get; } = new();

    public void Observe(RunEvent runEvent)
    {
    }
}

/// <summary>Fans one event out to several observers, so the console view and the JSONL file coexist.</summary>
public sealed class CompositeRunObserver : IRunObserver
{
    private readonly IReadOnlyList<IRunObserver> _observers;

    public CompositeRunObserver(params IRunObserver[] observers) => _observers = observers;

    public void Observe(RunEvent runEvent)
    {
        foreach (var observer in _observers)
        {
            observer.Observe(runEvent);
        }
    }
}
