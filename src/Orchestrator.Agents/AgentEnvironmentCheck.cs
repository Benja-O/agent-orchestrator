using System.Text.Json;
using Orchestrator.Domain;

namespace Orchestrator.Agents;

/// <summary>
/// The checks that run before the first agent is invoked.
/// </summary>
/// <remarks>
/// <para>
/// AI.md asks for failing fast at startup, and block 4 turned that from good practice into the
/// central lesson of the block. Three separate things had to be true for a headless agent to
/// actually reach the language server, none of them announced itself when false, and all three
/// produced the same symptom: an agent that works, sounds confident, and is generating blind.
/// </para>
/// <para>
/// The generalisation worth keeping: <strong>every safety mechanism here fails open.</strong> An
/// MCP server that was never approved, a tool that is available but not permitted, a hook whose
/// interpreter is missing — each one degrades silently into "no protection". So each one gets
/// probed on the way up rather than trusted.
/// </para>
/// </remarks>
public sealed class AgentEnvironmentCheck
{
    private readonly IAgentProcessRunner _processRunner;
    private readonly string _workspaceRoot;

    public AgentEnvironmentCheck(IAgentProcessRunner processRunner, string workspaceRoot)
    {
        _processRunner = processRunner;
        _workspaceRoot = workspaceRoot;
    }

    /// <summary>The CLI is on the PATH and answers.</summary>
    public async Task VerifyClaudeCodeRespondsAsync(string executablePath, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            new AgentProcessRequest
            {
                ExecutablePath = executablePath,
                Arguments = ["--version"],
                WorkingDirectory = _workspaceRoot,
                StandardInput = string.Empty,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new AgentRunnerException(
                $"'{executablePath} --version' exited with code {result.ExitCode}. Claude Code has to be on the PATH "
                + $"before a run starts. stderr: {result.StandardError}");
        }
    }

    /// <summary>
    /// The file-scope hook actually rejects a write outside the layer.
    /// </summary>
    /// <remarks>
    /// Not a formality. The first version of this hook was a PowerShell script invoked through
    /// <c>pwsh</c>, which is not installed on every Windows machine — and Claude Code's response
    /// to a hook it cannot launch is to log it and allow the write. The barrier was absent and
    /// everything looked normal, which is the worst configuration a barrier can be in, because
    /// from then on it is believed. This probe is what makes its absence loud.
    /// </remarks>
    public async Task VerifyFileScopeHookBlocksAsync(
        string hookScriptRelativePath,
        LayerMap layerMap,
        CancellationToken cancellationToken)
    {
        var domainFolder = layerMap.ScopeOf(Layer.Domain);
        var somewhereElse = layerMap.ScopeOf(Layer.Api) + "/Trespass.cs";

        var payload = JsonSerializer.Serialize(new
        {
            cwd = _workspaceRoot,
            tool_name = "Write",
            tool_input = new { file_path = somewhereElse },
        });

        var result = await _processRunner.RunAsync(
            new AgentProcessRequest
            {
                ExecutablePath = "node",
                Arguments = [hookScriptRelativePath, domainFolder],
                WorkingDirectory = _workspaceRoot,
                StandardInput = payload,
            },
            cancellationToken).ConfigureAwait(false);

        // 2 is the exit code Claude Code reads as "block this call". Anything else — including a
        // clean 0, and including the 1 that a missing interpreter produces — means writes outside
        // the layer would go through.
        if (result.ExitCode != 2)
        {
            throw new AgentRunnerException(
                $"The file-scope hook did not block a write to '{somewhereElse}' while scoped to '{domainFolder}': it "
                + $"exited with {result.ExitCode} instead of 2. Layer boundaries would not be enforced. "
                + $"stderr: {result.StandardError}");
        }
    }
}
