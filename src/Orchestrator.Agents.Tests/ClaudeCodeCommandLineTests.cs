using Orchestrator.Domain;

namespace Orchestrator.Agents.Tests;

/// <summary>
/// The flags, asserted one by one.
/// </summary>
/// <remarks>
/// This looks like testing a string, and it is not. Every one of these arguments was missing at
/// some point during block 4, and not one of the resulting failures raised an error: the agent
/// ran, sounded confident, and had no access to the language server. These assertions are the
/// difference between believing the integration is wired and knowing it.
/// </remarks>
public sealed class ClaudeCodeCommandLineTests
{
    private static readonly ClaudeCodeSettings Settings = new()
    {
        WorkspaceRoot = "F:/run/output",
        McpEndpoint = "http://127.0.0.1:5610/mcp",
    };

    private static AgentProcessRequest RequestFor(string agentName) =>
        ClaudeCodeCommandLine.For(
            new AgentInvocation
            {
                AgentName = agentName,
                Node = NodeId.SpecAnalysis,
                Attempt = 1,
                Prompt = "hacé lo tuyo",
            },
            Settings,
            LayerMap.Default);

    [Fact]
    public void The_invocation_dispatches_straight_to_the_named_subagent()
    {
        var request = RequestFor("domain");

        Assert.Equal("-p", request.Arguments[0]);
        AssertFlagValue(request, "--agent", "domain");
    }

    /// <summary>
    /// The single flag whose absence made risk R5 look like an approval problem: without it,
    /// print mode does not load the project's <c>.claude/</c> at all.
    /// </summary>
    [Fact]
    public void Project_settings_are_loaded_explicitly()
    {
        AssertFlagValue(RequestFor("domain"), "--setting-sources", "project");
    }

    /// <summary>
    /// The MCP server travels on the command line, carrying the port this run picked. A
    /// project-scope <c>.mcp.json</c> on its own waits for an approval headless cannot give.
    /// </summary>
    [Fact]
    public void The_language_server_is_declared_in_the_invocation_with_this_runs_endpoint()
    {
        var configuration = FlagValue(RequestFor("domain"), "--mcp-config");

        Assert.Contains("\"lsp\"", configuration, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:5610/mcp", configuration, StringComparison.Ordinal);
    }

    /// <summary>
    /// Being able to see a tool and being allowed to run it are different switches, and only the
    /// second one has anybody to ask in an interactive session.
    /// </summary>
    [Fact]
    public void A_layer_agent_may_run_the_lsp_tools_without_asking_anyone()
    {
        var request = RequestFor("domain");

        foreach (var tool in AgentToolPermissions.LspTools)
        {
            Assert.Contains(tool, request.Arguments);
        }
    }

    [Theory]
    [InlineData("domain", "src/Domain")]
    [InlineData("api", "src/Api")]
    [InlineData("frontend", "src/Frontend")]
    public void Each_layer_agent_gets_the_hook_scoped_to_its_own_folder(string agentName, string expectedFolder)
    {
        var settings = FlagValue(RequestFor(agentName), "--settings");

        Assert.Contains("PreToolUse", settings, StringComparison.Ordinal);
        Assert.Contains($"restrict-to-layer.js {expectedFolder}", settings, StringComparison.Ordinal);
    }

    /// <summary>
    /// The analyzer produces a plan and has no business writing code, so there is nothing to
    /// fence in — and no write tool to permit either (ADR-011).
    /// </summary>
    [Fact]
    public void The_spec_analyzer_gets_no_hook_and_no_write_tools()
    {
        var request = RequestFor("spec-analyzer");

        Assert.DoesNotContain("--settings", request.Arguments);
        Assert.DoesNotContain("Write", request.Arguments);
        Assert.DoesNotContain("Edit", request.Arguments);
        Assert.Contains("Read", request.Arguments);
    }

    /// <summary>
    /// A layer prompt carries the spec and a list of diagnostics. On a command line it would be
    /// truncated, and a truncated prompt does not fail — it produces an agent that ignored half
    /// of what it was told.
    /// </summary>
    [Fact]
    public void The_prompt_travels_on_standard_input_and_not_as_an_argument()
    {
        var request = RequestFor("domain");

        Assert.Equal("hacé lo tuyo", request.StandardInput);
        Assert.DoesNotContain("hacé lo tuyo", request.Arguments);
    }

    [Fact]
    public void The_agent_runs_inside_the_generated_workspace()
    {
        Assert.Equal("F:/run/output", RequestFor("domain").WorkingDirectory);
    }

    [Fact]
    public void The_answer_is_asked_for_as_json_so_the_runner_never_parses_prose()
    {
        AssertFlagValue(RequestFor("domain"), "--output-format", "json");
    }

    private static string FlagValue(AgentProcessRequest request, string flag)
    {
        var index = request.Arguments.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"The invocation carries no {flag}.");
        return request.Arguments[index + 1];
    }

    private static void AssertFlagValue(AgentProcessRequest request, string flag, string expected) =>
        Assert.Equal(expected, FlagValue(request, flag));
}
