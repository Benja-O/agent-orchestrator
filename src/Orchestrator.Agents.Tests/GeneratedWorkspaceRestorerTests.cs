namespace Orchestrator.Agents.Tests;

/// <summary>
/// The restore that has to happen between writing the scaffold and starting a language server.
/// </summary>
/// <remarks>
/// Tested against a scripted process runner rather than by actually restoring, because what is
/// worth pinning down is <em>which</em> commands run, <em>where</em>, and what happens when one
/// fails. Running a real <c>npm ci</c> in the suite would take longer than every other test put
/// together and would need the network — golden rule 3 territory, for no extra confidence.
/// </remarks>
public sealed class GeneratedWorkspaceRestorerTests
{
    private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), "orchestrator-restore-tests");

    [Fact]
    public async Task It_restores_the_solution_and_then_the_frontends_node_modules()
    {
        var recorder = new RecordingProcessRunner(exitCode: 0);

        await new GeneratedWorkspaceRestorer(recorder, WorkspaceRoot).RestoreAsync(CancellationToken.None);

        Assert.Equal(2, recorder.Requests.Count);

        var dotnet = recorder.Requests[0];
        Assert.Equal("dotnet", dotnet.ExecutablePath);
        Assert.Equal(["restore", "App.slnx"], dotnet.Arguments);
        Assert.Equal(Path.GetFullPath(WorkspaceRoot), dotnet.WorkingDirectory);

        var npm = recorder.Requests[1];
        Assert.Contains("npm", npm.ExecutablePath, StringComparison.Ordinal);
        Assert.Contains("ci", npm.Arguments);
    }

    /// <summary>
    /// npm is launched by full path, never by name.
    /// </summary>
    /// <remarks>
    /// The regression guard for the failure that stopped block 5's first real run. <c>npm.cmd</c>
    /// derives its own location from <c>%~dp0</c>, which for a bare name expands to the caller's
    /// working directory — so npm went looking for <c>npm-cli.js</c> inside
    /// <c>output/src/Frontend/node_modules</c> and reported a missing module. The message names
    /// the workspace, so it reads as a broken install rather than as a launch that lost its own
    /// path.
    /// </remarks>
    [Fact]
    public async Task Npm_is_launched_by_full_path_because_a_shim_started_by_name_loses_its_own_location()
    {
        var recorder = new RecordingProcessRunner(exitCode: 0);

        await new GeneratedWorkspaceRestorer(recorder, WorkspaceRoot).RestoreAsync(CancellationToken.None);

        // Skipped where npm is genuinely absent: the locator falls back to the bare name, and
        // asserting otherwise would fail for the environment rather than for the behaviour.
        if (ExecutableLocator.Resolve(OperatingSystem.IsWindows() ? "npm.cmd" : "npm") is var resolved
            && !Path.IsPathRooted(resolved))
        {
            return;
        }

        Assert.True(
            Path.IsPathRooted(recorder.Requests[1].ExecutablePath),
            $"npm was launched as '{recorder.Requests[1].ExecutablePath}' rather than by full path.");
    }

    /// <summary>
    /// npm runs inside the frontend folder, which is the whole point of running it at all.
    /// </summary>
    /// <remarks>
    /// <c>TypeScriptLanguageServerSession</c> looks for its executable in the analysed project's
    /// own <c>node_modules</c> and falls back to the PATH, where a generated workspace has
    /// nothing. Installing one directory too high leaves that fallback as the normal case, and
    /// the failure it produces arrives much later, as a language server that will not start.
    /// </remarks>
    [Fact]
    public async Task The_node_modules_land_in_the_folder_the_typescript_server_looks_in()
    {
        var recorder = new RecordingProcessRunner(exitCode: 0);

        await new GeneratedWorkspaceRestorer(recorder, WorkspaceRoot).RestoreAsync(CancellationToken.None);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(WorkspaceRoot), "src", "Frontend"),
            recorder.Requests[1].WorkingDirectory);
    }

    /// <summary>
    /// A failed restore stops the run before the first paid turn.
    /// </summary>
    /// <remarks>
    /// Continuing would be worse than stopping, and not because of the missing packages: Roslyn
    /// reports every type behind an unresolved reference as an error, so the domain agent's first
    /// review iteration would arrive carrying dozens of diagnostics that nothing it wrote caused.
    /// That is quota spent debugging the environment through an agent.
    /// </remarks>
    [Fact]
    public async Task A_restore_that_fails_stops_the_run_instead_of_handing_an_agent_a_broken_workspace()
    {
        var recorder = new RecordingProcessRunner(exitCode: 1, standardError: "NU1101: package not found");

        var exception = await Assert.ThrowsAsync<AgentRunnerException>(() =>
            new GeneratedWorkspaceRestorer(recorder, WorkspaceRoot).RestoreAsync(CancellationToken.None));

        Assert.Contains("dotnet restore", exception.Message, StringComparison.Ordinal);
        Assert.Contains("NU1101", exception.Message, StringComparison.Ordinal);
        Assert.Single(recorder.Requests);
    }

    private sealed class RecordingProcessRunner : IAgentProcessRunner
    {
        private readonly int _exitCode;
        private readonly string _standardError;

        public RecordingProcessRunner(int exitCode, string standardError = "")
        {
            _exitCode = exitCode;
            _standardError = standardError;
        }

        public List<AgentProcessRequest> Requests { get; } = [];

        public Task<AgentProcessResult> RunAsync(AgentProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new AgentProcessResult
            {
                ExitCode = _exitCode,
                StandardOutput = string.Empty,
                StandardError = _standardError,
            });
        }
    }
}
