using Orchestrator.Domain;

namespace Orchestrator.TestSupport;

/// <summary>Keeps every run event so a test can assert on the log the demo will show.</summary>
/// <remarks>
/// The log is the only window into the graph (ADR-007, ADR-015), which makes it a deliverable
/// rather than a side effect — so it gets asserted like one.
/// </remarks>
public sealed class RecordingRunObserver : IRunObserver
{
    public List<RunEvent> Events { get; } = [];

    public void Observe(RunEvent runEvent) => Events.Add(runEvent);

    public IReadOnlyList<TEvent> Of<TEvent>() where TEvent : RunEvent => Events.OfType<TEvent>().ToList();

    public TEvent Single<TEvent>() where TEvent : RunEvent => Events.OfType<TEvent>().Single();

    public IReadOnlyList<string> EventNames => Events.Select(runEvent => runEvent.Event).ToList();

    /// <summary>The console rendering of the whole run, which is what gets projected during the demo.</summary>
    public string Transcript => string.Join(Environment.NewLine, Events.Select(runEvent => runEvent.Summary));
}
