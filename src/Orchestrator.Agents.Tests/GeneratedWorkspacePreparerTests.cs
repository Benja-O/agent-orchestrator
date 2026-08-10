using Orchestrator.Domain;

namespace Orchestrator.Agents.Tests;

/// <summary>
/// The workspace the agents open, built from the real templates of this repository.
/// </summary>
public sealed class GeneratedWorkspacePreparerTests : IDisposable
{
    private readonly string _workspaceRoot =
        Path.Combine(Path.GetTempPath(), "orchestrator-workspace-tests", Guid.NewGuid().ToString("n"));

    private static string TemplatesDirectory => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static readonly SpecDocument Spec = new()
    {
        SourcePath = "specs/gestor-tareas.md",
        Text = "# Gestor de tareas\n\nRN-01: una tarea no se completa con prerrequisitos pendientes.\n",
        BusinessRules = ["RN-01"],
        AcceptanceCriteria = ["CA-01"],
        RulesCitedByCriterion = new Dictionary<string, IReadOnlyList<string>> { ["CA-01"] = ["RN-01"] },
    };

    private GeneratedWorkspacePreparer Preparer() => new(new WorkspaceLayout
    {
        TemplatesDirectory = TemplatesDirectory,
        WorkspaceRoot = _workspaceRoot,
        McpEndpoint = "http://127.0.0.1:5610/mcp",
    });

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void The_four_subagent_definitions_land_where_claude_code_looks_for_them()
    {
        Preparer().Prepare(Spec);

        var agentsDirectory = Path.Combine(_workspaceRoot, ".claude", "agents");

        Assert.True(File.Exists(Path.Combine(agentsDirectory, "spec-analyzer.md")));
        Assert.True(File.Exists(Path.Combine(agentsDirectory, "domain.md")));
        Assert.True(File.Exists(Path.Combine(agentsDirectory, "api.md")));
        Assert.True(File.Exists(Path.Combine(agentsDirectory, "frontend.md")));
    }

    /// <summary>
    /// The regression guard for the second silent failure block 4 found: a subagent's
    /// <c>tools</c> list is an allowlist that filters MCP tools too, so a layer template that
    /// forgets to name them produces an agent with no language server and no complaint.
    /// </summary>
    [Theory]
    [InlineData("domain.md")]
    [InlineData("api.md")]
    [InlineData("frontend.md")]
    public void Every_layer_template_names_the_lsp_tools_in_its_allowlist(string templateFileName)
    {
        Preparer().Prepare(Spec);

        var definition = File.ReadAllText(Path.Combine(_workspaceRoot, ".claude", "agents", templateFileName));

        foreach (var tool in AgentToolPermissions.LspTools)
        {
            Assert.Contains(tool, definition, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_hook_script_travels_with_the_workspace()
    {
        Preparer().Prepare(Spec);

        Assert.True(File.Exists(Path.Combine(_workspaceRoot, ".claude", "hooks", "restrict-to-layer.js")));
    }

    [Fact]
    public void The_spec_and_the_generated_applications_instructions_are_there_for_the_agents_to_read()
    {
        Preparer().Prepare(Spec);

        Assert.Contains("RN-01", File.ReadAllText(Path.Combine(_workspaceRoot, "spec.md")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_workspaceRoot, "CLAUDE.md")));
    }

    [Fact]
    public void The_three_layer_folders_exist_before_any_agent_runs()
    {
        Preparer().Prepare(Spec);

        Assert.True(Directory.Exists(Path.Combine(_workspaceRoot, "src", "Domain")));
        Assert.True(Directory.Exists(Path.Combine(_workspaceRoot, "src", "Api")));
        Assert.True(Directory.Exists(Path.Combine(_workspaceRoot, "src", "Frontend")));
    }

    [Fact]
    public void The_server_is_declared_and_pre_enabled_for_anyone_opening_the_workspace_by_hand()
    {
        Preparer().Prepare(Spec);

        var mcpConfiguration = File.ReadAllText(Path.Combine(_workspaceRoot, ".mcp.json"));
        var settings = File.ReadAllText(Path.Combine(_workspaceRoot, ".claude", "settings.json"));

        Assert.Contains("http://127.0.0.1:5610/mcp", mcpConfiguration, StringComparison.Ordinal);
        Assert.Contains("enabledMcpjsonServers", settings, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR-008: every run starts from nothing. A workspace that carries over makes the second
    /// run look better than it is, because it begins from the first one's output.
    /// </summary>
    [Fact]
    public void A_previous_runs_output_does_not_survive_into_the_next_one()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src", "Domain"));
        File.WriteAllText(Path.Combine(_workspaceRoot, "src", "Domain", "Leftover.cs"), "// from a previous run");

        Preparer().Prepare(Spec);

        Assert.False(File.Exists(Path.Combine(_workspaceRoot, "src", "Domain", "Leftover.cs")));
    }

    /// <summary>
    /// The guard on the recursive delete. One wrong configuration value away from eating a
    /// source tree, and that is not a mistake anyone gets to make twice.
    /// </summary>
    [Fact]
    public void It_refuses_to_wipe_a_git_repository()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".git"));

        var exception = Assert.Throws<AgentRunnerException>(() => Preparer().Prepare(Spec));

        Assert.Contains("git repository", exception.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(_workspaceRoot, ".git")));
    }

    [Fact]
    public void A_missing_template_directory_fails_at_preparation_rather_than_mid_run()
    {
        var preparer = new GeneratedWorkspacePreparer(new WorkspaceLayout
        {
            TemplatesDirectory = Path.Combine(AppContext.BaseDirectory, "no-such-templates"),
            WorkspaceRoot = _workspaceRoot,
            McpEndpoint = "http://127.0.0.1:5610/mcp",
        });

        Assert.Throws<AgentRunnerException>(() => preparer.Prepare(Spec));
    }
}
