namespace Orchestrator.Agents;

/// <summary>
/// Restores the scaffold's dependencies before a single language server is started.
/// </summary>
/// <remarks>
/// <para>
/// This runs for the same reason the scaffold exists at all: <strong>a language server tells you
/// about the code it can resolve.</strong> An unrestored project does not merely lack packages —
/// Roslyn reports every type behind a missing reference as an error, so the domain agent's first
/// review iteration would arrive carrying dozens of diagnostics that nothing it wrote caused. It
/// is the false red of block 4 at scale, and it burns paid turns chasing a broken environment.
/// </para>
/// <para>
/// The TypeScript half is the other half of D13. <c>typescript-language-server</c> has to live in
/// the analysed workspace's own <c>node_modules</c> (see
/// <c>TypeScriptLanguageServerSession.CreateProcessStartInfo</c>), and a workspace generated from
/// scratch has none. Without this step the server either fails to start or starts against a
/// project with no <c>tsconfig.json</c> and no React types, which is worse: it answers, and every
/// answer is wrong.
/// </para>
/// <para>
/// Both commands are deterministic — a pinned <c>package-lock.json</c> and pinned package
/// versions — and both are cheap after the first run, because npm and NuGet keep their own
/// caches outside the workspace. That is what makes ADR-008's "delete and regenerate every run"
/// affordable.
/// </para>
/// </remarks>
public sealed class GeneratedWorkspaceRestorer
{
    /// <summary>npm ships as a batch file on Windows, and <c>Process.Start</c> does not guess extensions.</summary>
    private static string NpmExecutable => OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

    private readonly IAgentProcessRunner _processRunner;
    private readonly string _workspaceRoot;

    public GeneratedWorkspaceRestorer(IAgentProcessRunner processRunner, string workspaceRoot)
    {
        _processRunner = processRunner;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>Restores the .NET solution and the frontend's node_modules, in that order.</summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        await RestoreDotnetAsync(cancellationToken).ConfigureAwait(false);
        await RestoreNodeAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task RestoreDotnetAsync(CancellationToken cancellationToken) => RunAsync(
        "dotnet",
        ["restore", "App.slnx"],
        _workspaceRoot,
        "restore the generated solution's NuGet packages",
        cancellationToken);

    /// <summary>
    /// <c>npm ci</c>, not <c>npm install</c>: it installs the lock file exactly and fails instead
    /// of quietly resolving something else when the two files disagree.
    /// </summary>
    private Task RestoreNodeAsync(CancellationToken cancellationToken) => RunAsync(
        NpmExecutable,
        ["ci", "--no-audit", "--no-fund"],
        Path.Combine(_workspaceRoot, "src", "Frontend"),
        "install the frontend's node_modules, which is where typescript-language-server has to live",
        cancellationToken);

    private async Task RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string what,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            new AgentProcessRequest
            {
                ExecutablePath = executablePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardInput = string.Empty,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new AgentRunnerException(
                $"Could not {what}: '{executablePath} {string.Join(' ', arguments)}' exited with code "
                + $"{result.ExitCode}. Nothing would be verifying the generated code correctly, so the run stops "
                + $"here rather than at the third node.{Environment.NewLine}"
                + $"stdout: {Tail(result.StandardOutput)}{Environment.NewLine}"
                + $"stderr: {Tail(result.StandardError)}");
        }
    }

    /// <summary>The end of a restore log, which is where the reason is; the start is progress noise.</summary>
    private static string Tail(string output)
    {
        const int MaximumCharacters = 2000;
        var trimmed = output.Trim();

        return trimmed.Length <= MaximumCharacters ? trimmed : trimmed[^MaximumCharacters..];
    }
}
