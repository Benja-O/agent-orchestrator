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

    /// <summary>
    /// The scaffold of ADR-016, checked as what it is: the gate's own apparatus.
    /// </summary>
    /// <remarks>
    /// Roslyn opens a solution, not a folder of loose files. Without these files the C# language
    /// server loads nothing, reports nothing, and the gate reads that as clean code — the false
    /// green through the door neither ADR-006 nor block 2 had covered.
    /// </remarks>
    [Theory]
    [InlineData("App.slnx")]
    [InlineData("src/Domain/Domain.csproj")]
    [InlineData("src/Api/Api.csproj")]
    [InlineData("src/Frontend/package.json")]
    [InlineData("src/Frontend/package-lock.json")]
    [InlineData("src/Frontend/tsconfig.json")]
    public void The_scaffold_the_language_servers_need_is_there_before_any_agent_runs(string relativePath)
    {
        Preparer().Prepare(Spec);

        var expected = Path.Combine(_workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(expected), $"The scaffold did not produce '{relativePath}'.");
    }

    /// <summary>
    /// The solution names both C# projects, so Roslyn has both to load.
    /// </summary>
    /// <remarks>
    /// A solution that lists only one project is the subtlest version of the same failure: the
    /// gate works, answers confidently, and is blind to a whole layer. The API agent calling a
    /// domain method that does not exist — the case this project is built to catch — would go
    /// through.
    /// </remarks>
    [Fact]
    public void The_solution_names_every_layer_that_compiles()
    {
        Preparer().Prepare(Spec);

        var solution = File.ReadAllText(Path.Combine(_workspaceRoot, "App.slnx"));

        Assert.Contains("src/Domain/Domain.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("src/Api/Api.csproj", solution, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scaffold puts its projects exactly where <see cref="LayerMap"/> says each layer lives.
    /// </summary>
    /// <remarks>
    /// These are two independent statements of the same layout — one in
    /// <c>templates/scaffold/</c>, one in <c>LayerMap.Default</c> — and they only stay in step
    /// because this test fails when they do not. Drift is quiet and expensive: a diagnostic in a
    /// directory no layer owns stops the run, and one in a directory the wrong layer owns goes
    /// back to an agent that cannot fix it.
    /// </remarks>
    [Fact]
    public void The_scaffold_puts_each_project_inside_the_folder_its_layer_owns()
    {
        Preparer().Prepare(Spec);

        var layerMap = LayerMap.Default;

        Assert.True(layerMap.TryResolve("src/Domain/Domain.csproj", out var domain));
        Assert.Equal(Layer.Domain, domain);

        Assert.True(layerMap.TryResolve("src/Api/Api.csproj", out var api));
        Assert.Equal(Layer.Api, api);

        Assert.True(layerMap.TryResolve("src/Frontend/package.json", out var frontend));
        Assert.Equal(Layer.Frontend, frontend);
    }

    /// <summary>
    /// The frontend's language server has to come from the workspace's own node_modules, so the
    /// package the scaffold installs is the one <c>TypeScriptLanguageServerSession</c> looks for.
    /// </summary>
    [Fact]
    public void The_frontend_scaffold_brings_its_own_language_server()
    {
        Preparer().Prepare(Spec);

        var packageJson = File.ReadAllText(Path.Combine(_workspaceRoot, "src", "Frontend", "package.json"));

        Assert.Contains("typescript-language-server", packageJson, StringComparison.Ordinal);
        Assert.Contains("react", packageJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// A left-over <c>node_modules</c> beside the templates must not be copied into the workspace.
    /// </summary>
    /// <remarks>
    /// Cheap insurance against a slow, confusing failure: a stray install in
    /// <c>templates/scaffold/</c> would otherwise make every run copy thousands of files, and
    /// then hand the language server a source tree it was never meant to analyse.
    /// </remarks>
    [Fact]
    public void A_stray_node_modules_beside_the_templates_does_not_travel()
    {
        var strayDirectory = Path.Combine(TemplatesDirectory, "scaffold", "src", "Frontend", "node_modules", "stray");
        Directory.CreateDirectory(strayDirectory);
        File.WriteAllText(Path.Combine(strayDirectory, "index.js"), "// should not be copied");

        try
        {
            Preparer().Prepare(Spec);

            Assert.False(Directory.Exists(Path.Combine(_workspaceRoot, "src", "Frontend", "node_modules")));
        }
        finally
        {
            Directory.Delete(Path.Combine(TemplatesDirectory, "scaffold", "src", "Frontend", "node_modules"), recursive: true);
        }
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
