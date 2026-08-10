using Orchestrator.Domain;

namespace Orchestrator.Agents.Tests;

/// <summary>
/// The startup probes, and specifically the fact that they refuse to pass on a silence.
/// </summary>
public sealed class AgentEnvironmentCheckTests
{
    private static AgentEnvironmentCheck CheckWith(IAgentProcessRunner processRunner) =>
        new(processRunner, "F:/run/output");

    private static AgentProcessResult Exited(int exitCode) => new()
    {
        ExitCode = exitCode,
        StandardOutput = string.Empty,
        StandardError = string.Empty,
    };

    [Fact]
    public async Task A_cli_that_answers_passes()
    {
        var check = CheckWith(new ScriptedProcessRunner(Exited(0)));

        await check.VerifyClaudeCodeRespondsAsync("claude", CancellationToken.None);
    }

    [Fact]
    public async Task A_missing_cli_stops_the_run_before_anything_is_spent()
    {
        var check = CheckWith(new ScriptedProcessRunner(Exited(127)));

        await Assert.ThrowsAsync<AgentRunnerException>(
            () => check.VerifyClaudeCodeRespondsAsync("claude", CancellationToken.None));
    }

    [Fact]
    public async Task A_hook_that_blocks_passes()
    {
        var check = CheckWith(new ScriptedProcessRunner(Exited(2)));

        await check.VerifyFileScopeHookBlocksAsync(
            ".claude/hooks/restrict-to-layer.js", LayerMap.Default, CancellationToken.None);
    }

    /// <summary>
    /// The case this probe exists for. Block 4 shipped a first hook invoked through <c>pwsh</c>,
    /// which is not installed on every Windows machine — and Claude Code's answer to a hook it
    /// cannot launch is to log the failure and allow the write. Exit code 1 is what that looks
    /// like, and 0 is what a hook that ran and approved looks like. Neither may pass for a
    /// barrier that is holding.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9009)]
    public async Task A_hook_that_does_not_block_stops_the_run_however_it_failed(int exitCode)
    {
        var check = CheckWith(new ScriptedProcessRunner(Exited(exitCode)));

        var exception = await Assert.ThrowsAsync<AgentRunnerException>(
            () => check.VerifyFileScopeHookBlocksAsync(
                ".claude/hooks/restrict-to-layer.js", LayerMap.Default, CancellationToken.None));

        Assert.Contains("Layer boundaries would not be enforced", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The probe asks the hook about a real violation, or it would be proving nothing.</summary>
    [Fact]
    public async Task The_probe_asks_the_hook_to_reject_a_write_outside_the_layer_it_is_guarding()
    {
        var scripted = new ScriptedProcessRunner(Exited(2));
        var check = CheckWith(scripted);

        await check.VerifyFileScopeHookBlocksAsync(
            ".claude/hooks/restrict-to-layer.js", LayerMap.Default, CancellationToken.None);

        Assert.Contains("src/Domain", scripted.Request!.Arguments);
        Assert.Contains("src/Api", scripted.Request.StandardInput, StringComparison.Ordinal);
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
