using Orchestrator.Domain;

namespace Orchestrator.Agents.Tests;

/// <summary>
/// The runner against a scripted process: no <c>claude</c>, no quota, no network.
/// </summary>
/// <remarks>
/// Golden rule 3 of AI.md is usually stated about the graph. It applies here too, and this is
/// where it is least obvious and most valuable: the ways an invocation can end — a turn limit,
/// a timeout, an unreadable answer — are exactly the ones that would be most expensive and most
/// unreliable to reproduce by actually running an agent.
/// </remarks>
public sealed class ClaudeCodeAgentRunnerTests
{
    private static AgentInvocation Invocation(string agentName = "domain") => new()
    {
        AgentName = agentName,
        Node = NodeId.ImplementationOf(Layer.Domain),
        Attempt = 1,
        Prompt = "implementá RN-01",
    };

    private static ClaudeCodeAgentRunner RunnerAnswering(AgentProcessResult result) =>
        new(new ScriptedProcessRunner(result), new ClaudeCodeSettings
        {
            WorkspaceRoot = "F:/run/output",
            McpEndpoint = "http://127.0.0.1:5610/mcp",
        });

    private static AgentProcessResult Printed(string standardOutput, int exitCode = 0) => new()
    {
        ExitCode = exitCode,
        StandardOutput = standardOutput,
        StandardError = string.Empty,
    };

    [Fact]
    public async Task A_finished_turn_comes_back_as_completed_with_its_text_for_the_log()
    {
        var runner = RunnerAnswering(Printed(
            """{"type":"result","subtype":"success","is_error":false,"num_turns":4,"result":"Listo, implementé RN-01."}"""));

        var outcome = await runner.RunAsync(Invocation(), CancellationToken.None);

        Assert.Equal(AgentCompletion.Completed, outcome.Completion);
        Assert.True(outcome.IsUsable);
        Assert.Equal("Listo, implementé RN-01.", outcome.Transcript);
    }

    /// <summary>
    /// Claude Code's own ceiling, applied inside the turn (ADR-011). Distinct from a failure
    /// because whatever the agent wrote before running out is on disk, and the gate is next.
    /// </summary>
    [Fact]
    public async Task Running_out_of_turns_is_its_own_outcome()
    {
        var runner = RunnerAnswering(Printed(
            """{"type":"result","subtype":"error_max_turns","is_error":true,"num_turns":40,"result":""}"""));

        var outcome = await runner.RunAsync(Invocation(), CancellationToken.None);

        Assert.Equal(AgentCompletion.TurnLimitReached, outcome.Completion);
        Assert.Contains("40", outcome.FailureDetail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_error_reported_by_the_cli_is_not_usable_work()
    {
        var runner = RunnerAnswering(Printed(
            """{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":2,"result":"boom"}"""));

        var outcome = await runner.RunAsync(Invocation(), CancellationToken.None);

        Assert.Equal(AgentCompletion.Errored, outcome.Completion);
        Assert.False(outcome.IsUsable);
    }

    /// <summary>
    /// The case that matters most for a night-time run: whatever went wrong, it has to arrive as
    /// a failure the graph can terminate on, not as an empty success it would build on.
    /// </summary>
    [Theory]
    [InlineData("", 1)]
    [InlineData("command not found", 127)]
    [InlineData("{ this is not json", 0)]
    public async Task An_unreadable_answer_is_a_failure_and_never_an_empty_success(string standardOutput, int exitCode)
    {
        var runner = RunnerAnswering(Printed(standardOutput, exitCode));

        var outcome = await runner.RunAsync(Invocation(), CancellationToken.None);

        Assert.Equal(AgentCompletion.Errored, outcome.Completion);
        Assert.False(outcome.IsUsable);
    }

    [Fact]
    public async Task A_process_stopped_for_taking_too_long_reports_a_timeout()
    {
        var runner = RunnerAnswering(new AgentProcessResult
        {
            ExitCode = -1,
            StandardOutput = string.Empty,
            StandardError = string.Empty,
            TimedOut = true,
        });

        var outcome = await runner.RunAsync(Invocation(), CancellationToken.None);

        Assert.Equal(AgentCompletion.TimedOut, outcome.Completion);
    }

    /// <summary>
    /// A zero exit with a success subtype is the only thing that counts as usable — and even
    /// then, "usable" means the turn finished, never that the code is right (ADR-004).
    /// </summary>
    [Fact]
    public async Task A_success_subtype_with_a_non_zero_exit_is_still_a_failure()
    {
        var runner = RunnerAnswering(Printed(
            """{"type":"result","subtype":"success","is_error":false,"num_turns":1,"result":"ok"}""",
            exitCode: 3));

        var outcome = await runner.RunAsync(Invocation(), CancellationToken.None);

        Assert.Equal(AgentCompletion.Errored, outcome.Completion);
    }

    [Fact]
    public async Task The_invocation_reaching_the_process_is_the_one_the_graph_asked_for()
    {
        var scripted = new ScriptedProcessRunner(Printed(
            """{"type":"result","subtype":"success","is_error":false,"num_turns":1,"result":"ok"}"""));

        var runner = new ClaudeCodeAgentRunner(scripted, new ClaudeCodeSettings
        {
            WorkspaceRoot = "F:/run/output",
            McpEndpoint = "http://127.0.0.1:5610/mcp",
        });

        await runner.RunAsync(Invocation("api"), CancellationToken.None);

        Assert.Contains("--agent", scripted.Request!.Arguments);
        Assert.Contains("api", scripted.Request.Arguments);
        Assert.Equal("implementá RN-01", scripted.Request.StandardInput);
    }

    private sealed class ScriptedProcessRunner : IAgentProcessRunner
    {
        private readonly AgentProcessResult _result;

        public ScriptedProcessRunner(AgentProcessResult result) => _result = result;

        public AgentProcessRequest? Request { get; private set; }

        public Task<AgentProcessResult> RunAsync(AgentProcessRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(_result);
        }
    }
}
