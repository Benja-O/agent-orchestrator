using Orchestrator.Domain;

namespace Orchestrator.Observability;

/// <summary>
/// The live view: one line per event, indented so the shape of the run is visible at a glance.
/// </summary>
/// <remarks>
/// <para>
/// This is what gets projected during the demo, which is what makes the log a product decision
/// rather than an infrastructure one (ADR-015). It renders <see cref="RunEvent.Summary"/> and
/// nothing else — the event owns its own wording, so the console view and the JSONL file
/// cannot drift into describing different runs.
/// </para>
/// <para>
/// The verbose events are filtered by default. Waiting for an index is worth logging every
/// time and worth showing on screen only when it goes on long enough to matter.
/// </para>
/// </remarks>
public sealed class ConsoleRunObserver : IRunObserver
{
    private readonly TextWriter _writer;
    private readonly bool _verbose;

    public ConsoleRunObserver(TextWriter? writer = null, bool verbose = false)
    {
        _writer = writer ?? Console.Out;
        _verbose = verbose;
    }

    public void Observe(RunEvent runEvent)
    {
        if (!_verbose && ShouldStayQuiet(runEvent))
        {
            return;
        }

        _writer.WriteLine(runEvent.Summary);
    }

    /// <summary>The first couple of index waits are normal; the fifth one is news.</summary>
    private static bool ShouldStayQuiet(RunEvent runEvent) =>
        runEvent is GateWaitingForIndex waiting && waiting.WaitAttempt < 3;
}
