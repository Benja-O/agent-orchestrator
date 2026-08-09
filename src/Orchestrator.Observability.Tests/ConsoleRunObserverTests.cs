using Orchestrator.Domain;

namespace Orchestrator.Observability.Tests;

public sealed class ConsoleRunObserverTests
{
    private static GateWaitingForIndex Waiting(int attempt) => new()
    {
        RunId = new RunId("abc123"),
        Timestamp = DateTimeOffset.UnixEpoch,
        Scope = ".",
        WaitAttempt = attempt,
        MaximumWaitAttempts = 10,
        StatusDetail = "Roslyn is loading the solution 'App.slnx'",
    };

    /// <summary>Two waits while a solution loads are normal. The fifth is the run telling you something.</summary>
    [Fact]
    public void Stays_quiet_about_the_first_index_waits_and_speaks_up_about_the_later_ones()
    {
        var buffer = new StringWriter();
        var observer = new ConsoleRunObserver(buffer);

        observer.Observe(Waiting(1));
        observer.Observe(Waiting(2));
        observer.Observe(Waiting(5));

        var lines = buffer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines);
        Assert.Contains("5/10", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Says_everything_when_asked_to()
    {
        var buffer = new StringWriter();
        var observer = new ConsoleRunObserver(buffer, verbose: true);

        observer.Observe(Waiting(1));
        observer.Observe(Waiting(2));

        Assert.Equal(2, buffer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Renders_the_reason_a_run_stopped()
    {
        var buffer = new StringWriter();

        new ConsoleRunObserver(buffer).Observe(new RunTerminated
        {
            RunId = new RunId("abc123"),
            Timestamp = DateTimeOffset.UnixEpoch,
            Reason = TerminationReason.NoProgress,
            Detail = "the domain agent returned the same 2 diagnostic(s) twice in a row",
            Duration = TimeSpan.FromSeconds(41.5),
            Trace = ["spec-analysis"],
        });

        Assert.Contains("NoProgress", buffer.ToString(), StringComparison.Ordinal);
        Assert.Contains("41.5s", buffer.ToString(), StringComparison.Ordinal);
    }
}
