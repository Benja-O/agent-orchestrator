using System.Text.Json;
using Orchestrator.Domain;

namespace Orchestrator.Agents;

/// <summary>
/// Builds the exact <c>claude -p</c> invocation for one agent turn.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and separate from the process that runs it, because the argument list <em>is</em> the
/// integration. Almost everything block 4 got wrong the first time was a missing flag, and none
/// of those mistakes announced themselves: the agent ran, answered plausibly, and had no LSP
/// tools. Tests over this function are what turn "we think the flags are right" into something
/// the build checks.
/// </para>
/// </remarks>
internal static class ClaudeCodeCommandLine
{
    /// <summary>The prompt travels on stdin, never as an argument.</summary>
    /// <remarks>
    /// A layer prompt carries the spec excerpt and a list of diagnostics, so it is comfortably
    /// past what a Windows command line accepts — and the truncation would not look like an
    /// error, it would look like an agent that ignored half its instructions.
    /// </remarks>
    public static AgentProcessRequest For(
        AgentInvocation invocation,
        ClaudeCodeSettings settings,
        LayerMap layerMap)
    {
        var isLayerAgent = TryResolveLayer(invocation.AgentName, out var layer);

        var arguments = new List<string>
        {
            "-p",

            // Dispatch straight to the subagent instead of asking a parent session to delegate.
            // Delegation is a decision a model makes, and the graph already decided.
            "--agent", invocation.AgentName,

            // Without this the project's .claude/ is not loaded in print mode, which silently
            // costs the run its agents, its enabledMcpjsonServers and its PreToolUse hook. It is
            // the single flag whose absence caused risk R5 to look like an approval problem.
            "--setting-sources", "project",

            "--output-format", "json",

            "--mcp-config", McpConfigurationJson(settings.McpEndpoint),
        };

        if (isLayerAgent)
        {
            arguments.Add("--settings");
            arguments.Add(HookSettingsJson(settings.HookScriptRelativePath, layerMap.ScopeOf(layer)));
        }

        arguments.Add("--allowedTools");
        arguments.AddRange(isLayerAgent
            ? AgentToolPermissions.ForLayerAgent
            : AgentToolPermissions.ForSpecAnalyzer);

        return new AgentProcessRequest
        {
            ExecutablePath = settings.ExecutablePath,
            Arguments = arguments,
            WorkingDirectory = settings.WorkspaceRoot,
            StandardInput = invocation.Prompt,
        };
    }

    /// <summary>The server declared inline, carrying the port this run happens to be using.</summary>
    private static string McpConfigurationJson(string endpoint) => JsonSerializer.Serialize(new
    {
        mcpServers = new Dictionary<string, object>
        {
            ["lsp"] = new { type = "http", url = endpoint },
        },
    });

    /// <summary>
    /// The file-scope barrier of ADR-011, scoped to the one agent this process will run.
    /// </summary>
    /// <remarks>
    /// ADR-011 designed this as a per-subagent hook and rejected session-scope rules because they
    /// "do not tell the domain agent from the API agent". That objection dissolves here rather
    /// than being overruled: the orchestrator runs exactly one agent per process, so the session
    /// <em>is</em> the agent, and the hook can be handed in per invocation instead of being
    /// written into a file two agents would share.
    /// </remarks>
    private static string HookSettingsJson(string hookScriptRelativePath, string allowedFolder) =>
        JsonSerializer.Serialize(new
        {
            hooks = new Dictionary<string, object>
            {
                ["PreToolUse"] = new[]
                {
                    new
                    {
                        matcher = "Write|Edit",
                        hooks = new[]
                        {
                            new
                            {
                                type = "command",
                                command = $"node {hookScriptRelativePath} {allowedFolder}",
                            },
                        },
                    },
                },
            },
        });

    private static bool TryResolveLayer(string agentName, out Layer layer)
    {
        foreach (var candidate in LayerCatalog.InPipelineOrder)
        {
            if (string.Equals(LayerCatalog.AgentNameOf(candidate), agentName, StringComparison.OrdinalIgnoreCase))
            {
                layer = candidate;
                return true;
            }
        }

        layer = default;
        return false;
    }
}
