namespace Orchestrator.TestSupport;

/// <summary>
/// A clock that moves a fixed amount every time it is read.
/// </summary>
/// <remarks>
/// <para>
/// Golden rule 4 of AI.md exists for two concrete things — subprocess timeouts and a review
/// loop that retries — and both become untestable the moment real wall-clock time is involved.
/// This gives every run deterministic, strictly increasing timestamps and non-zero durations
/// in the log, with no waiting.
/// </para>
/// <para>
/// It deliberately does not fake timers: <see cref="TimeProvider.CreateTimer"/> stays the base
/// implementation. The graph only creates a timer for the agent timeout, which no test is
/// meant to trip, and the one place that actually waits — the gate re-asking while a server
/// indexes — is exercised with the delay set to zero, where the ceiling on attempts is what
/// the test is about. The delay itself is covered separately against the real clock with a
/// millisecond budget.
/// </para>
/// </remarks>
public sealed class SteppingTimeProvider : TimeProvider
{
    private readonly TimeSpan _step;
    private DateTimeOffset _now;

    public SteppingTimeProvider(TimeSpan? step = null, DateTimeOffset? start = null)
    {
        _step = step ?? TimeSpan.FromMilliseconds(250);
        _now = start ?? new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow()
    {
        var current = _now;
        _now = _now.Add(_step);
        return current;
    }

    public override long GetTimestamp() => _now.UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
}
