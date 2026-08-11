using Orchestrator.Domain;

namespace Orchestrator.Observability;

/// <summary>
/// Sends one event stream to several observers.
/// </summary>
/// <remarks>
/// The composition ADR-015 assumes: the console view and the JSONL file are two readings of the
/// <em>same</em> events, not two independent records. Fanning out here rather than letting each
/// caller loop is what keeps them from drifting into describing different runs.
/// </remarks>
public sealed class FanOutRunObserver : IRunObserver
{
    private readonly IReadOnlyList<IRunObserver> _observers;

    public FanOutRunObserver(IReadOnlyList<IRunObserver> observers) => _observers = observers;

    public void Observe(RunEvent runEvent)
    {
        foreach (var observer in _observers)
        {
            observer.Observe(runEvent);
        }
    }
}
