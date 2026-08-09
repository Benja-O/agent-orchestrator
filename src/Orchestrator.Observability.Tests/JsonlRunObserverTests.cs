using System.Text.Json;
using Orchestrator.Domain;
using Orchestrator.TestSupport;

namespace Orchestrator.Observability.Tests;

/// <summary>
/// The log is the only window into the graph and it is what gets projected during the demo
/// (ADR-007, ADR-015), so its shape is asserted like a deliverable rather than assumed.
/// </summary>
public sealed class JsonlRunObserverTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 12, 9, 30, 15, TimeSpan.Zero);

    private static JsonDocument Write(RunEvent runEvent)
    {
        var buffer = new StringWriter();
        new JsonlRunObserver(buffer).Observe(runEvent);

        var lines = buffer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        return JsonDocument.Parse(Assert.Single(lines));
    }

    [Fact]
    public void Every_line_opens_with_the_three_keys_a_run_is_read_by()
    {
        using var document = Write(new NodeEntered
        {
            RunId = new RunId("abc123"),
            Timestamp = Moment,
            Node = NodeId.ImplementationOf(Layer.Domain),
            Attempt = 2,
            Layer = "domain",
            BusinessRules = ["RN-01"],
        });

        var keys = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();

        Assert.Equal(["timestamp", "run", "event"], keys[..3]);
        Assert.Equal("node-entered", document.RootElement.GetProperty("event").GetString());
        Assert.Equal("abc123", document.RootElement.GetProperty("run").GetString());
    }

    /// <summary>Identifiers are strings on the wire, not objects wrapping a value.</summary>
    [Fact]
    public void Writes_node_identifiers_as_plain_strings()
    {
        using var document = Write(new NodeEntered
        {
            RunId = new RunId("abc123"),
            Timestamp = Moment,
            Node = NodeId.GateOf(Layer.Api),
            Attempt = 1,
        });

        Assert.Equal("api-gate", document.RootElement.GetProperty("node").GetString());
    }

    [Fact]
    public void Writes_enumerations_as_names_and_durations_as_milliseconds()
    {
        using var document = Write(new AgentReturned
        {
            RunId = new RunId("abc123"),
            Timestamp = Moment,
            Node = NodeId.SpecAnalysis,
            AgentName = "spec-analyzer",
            Completion = AgentCompletion.TurnLimitReached,
            Duration = TimeSpan.FromMilliseconds(1500),
        });

        Assert.Equal("turnLimitReached", document.RootElement.GetProperty("completion").GetString());
        Assert.Equal(1500, document.RootElement.GetProperty("durationMs").GetDouble());
    }

    /// <summary>
    /// The console rendering is not duplicated into the machine-readable copy: it is the same
    /// data in another form, and writing both would double the file for nothing.
    /// </summary>
    [Fact]
    public void Does_not_write_the_console_summary()
    {
        using var document = Write(new RunStarted
        {
            RunId = new RunId("abc123"),
            Timestamp = Moment,
            SpecPath = "specs/gestor-tareas.md",
            BusinessRules = ["RN-01"],
            AcceptanceCriteria = ["CA-01"],
        });

        Assert.False(document.RootElement.TryGetProperty("summary", out _));
        Assert.Equal("specs/gestor-tareas.md", document.RootElement.GetProperty("specPath").GetString());
    }

    [Fact]
    public void Omits_the_fields_that_have_nothing_to_say()
    {
        using var document = Write(new GateWaitingForIndex
        {
            RunId = new RunId("abc123"),
            Timestamp = Moment,
            Scope = ".",
            WaitAttempt = 1,
            MaximumWaitAttempts = 10,
        });

        Assert.False(document.RootElement.TryGetProperty("statusDetail", out _));
    }

    [Fact]
    public void Writes_one_line_per_event_and_leaves_them_parseable()
    {
        var buffer = new StringWriter();
        var observer = new JsonlRunObserver(buffer);

        observer.Observe(new RunStarted
        {
            RunId = new RunId("abc123"),
            Timestamp = Moment,
            SpecPath = "specs/gestor-tareas.md",
            BusinessRules = ["RN-01"],
            AcceptanceCriteria = ["CA-01"],
        });

        observer.Observe(new RunTerminated
        {
            RunId = new RunId("abc123"),
            Timestamp = Moment,
            Reason = TerminationReason.NoProgress,
            Detail = "the domain agent returned the same diagnostics twice",
            Duration = TimeSpan.FromSeconds(12),
            Trace = ["spec-analysis", "domain-implementation"],
        });

        var lines = buffer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());

        using var terminated = JsonDocument.Parse(lines[1]);
        Assert.Equal("noProgress", terminated.RootElement.GetProperty("reason").GetString());
        Assert.Equal(2, terminated.RootElement.GetProperty("trace").GetArrayLength());
    }

    [Fact]
    public void Creates_the_directory_of_the_log_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orchestrator-log-" + Guid.NewGuid().ToString("n")[..8]);
        var filePath = Path.Combine(directory, "run.jsonl");

        try
        {
            using (var observer = JsonlRunObserver.AppendingTo(filePath))
            {
                observer.Observe(new RunStarted
                {
                    RunId = new RunId("abc123"),
                    Timestamp = Moment,
                    SpecPath = "specs/gestor-tareas.md",
                    BusinessRules = ["RN-01"],
                    AcceptanceCriteria = ["CA-01"],
                });
            }

            Assert.Single(File.ReadAllLines(filePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
