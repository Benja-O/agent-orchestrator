using Orchestrator.Agents;
using Orchestrator.Application.Graph;
using Orchestrator.Application.Spec;
using Orchestrator.Cli;
using Orchestrator.Domain;
using Orchestrator.Lsp;
using Orchestrator.Observability;

// The orchestrator's only Main.
//
//   dotnet run --project src/Orchestrator.Cli -- --spec specs/gestor-tareas.md --output output/
//
// It is a host and nothing else: parse the arguments, build the graph's collaborators in the one
// order that works, run it, turn how it ended into an exit code. There is no orchestration logic
// here — that lives in Orchestrator.Application, where it is tested against fakes and costs
// nothing to exercise (AI.md, golden rule 3).
//
// The order below is load-bearing, and each step is a way the run could otherwise fail late and
// expensively:
//
//   1. Parse the spec, before deleting anything.
//   2. Prepare the workspace from scratch, scaffold included (ADR-008, ADR-016).
//   3. Restore its dependencies, so the language servers analyse resolvable code.
//   4. Start the MCP server and check it actually has language servers behind it.
//   5. Probe Claude Code and the file-scope hook, because every one of those mechanisms fails
//      open and silently (block 4).
//   6. Only then spend the first paid turn.

if (CommandLineParser.IsHelpRequest(args))
{
    Console.WriteLine(CommandLineParser.Usage);
    return ExitCodes.CouldNotStart;
}

var parsed = CommandLineParser.Parse(args);

if (parsed.IsFailure)
{
    Console.Error.WriteLine(parsed.FailureReason);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLineParser.Usage);
    return ExitCodes.CouldNotStart;
}

var options = parsed.Value;

using var cancellation = new CancellationTokenSource();

// A cancelled run has to take its subprocesses with it (AI.md, async conventions). Language
// servers left alive hold handles on the output directory, which ADR-008 requires to be
// deletable — an orphan does not just linger, it breaks the next run.
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine("Cancelling: shutting the language servers and the agent down…");
    cancellation.Cancel();
};

try
{
    return await RunAsync(options, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("The run was cancelled.");
    return ExitCodes.CouldNotStart;
}
catch (Exception exception) when (exception is AgentRunnerException or LspGatewayException or InvalidOperationException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"The run could not start: {exception.Message}");
    return ExitCodes.CouldNotStart;
}

static async Task<int> RunAsync(OrchestratorOptions options, CancellationToken cancellationToken)
{
    var repository = RepositoryLayout.Find();
    repository.RequireLspServerBuilt();

    var workspaceRoot = Path.GetFullPath(options.OutputDirectory);
    var logDirectory = Path.GetFullPath(options.LogDirectory);
    Directory.CreateDirectory(logDirectory);

    if (!File.Exists(options.SpecPath))
    {
        Console.Error.WriteLine($"There is no spec at '{options.SpecPath}'.");
        return ExitCodes.CouldNotStart;
    }

    var specResult = SpecParser.Parse(options.SpecPath, await File.ReadAllTextAsync(options.SpecPath, cancellationToken));

    if (specResult.IsFailure)
    {
        Console.Error.WriteLine($"The spec does not parse: {specResult.FailureReason}");
        return ExitCodes.CouldNotStart;
    }

    var spec = specResult.Value;
    Console.WriteLine($"Spec: {options.SpecPath} — {spec.BusinessRules.Count} business rule(s), {spec.AcceptanceCriteria.Count} acceptance criteria.");

    Console.WriteLine($"Preparing '{workspaceRoot}' from scratch (ADR-008)…");

    new GeneratedWorkspacePreparer(new WorkspaceLayout
    {
        TemplatesDirectory = repository.TemplatesDirectory,
        WorkspaceRoot = workspaceRoot,

        // Rewritten with the real port below. The runner never reads this file — it passes
        // --mcp-config on every invocation — so it only serves a person opening the workspace by
        // hand, and the port is not known until the server is up.
        McpEndpoint = "http://127.0.0.1:0/mcp",
    }).Prepare(spec);

    var processRunner = new SystemAgentProcessRunner(TimeSpan.FromMinutes(10), TimeProvider.System);

    Console.WriteLine("Restoring the scaffold's dependencies (NuGet, then node_modules)…");
    await new GeneratedWorkspaceRestorer(processRunner, workspaceRoot).RestoreAsync(cancellationToken);

    Console.WriteLine("Starting the MCP server…");

    await using var lspServer = await LspServerHost.StartAsync(
        new LspServerSettings
        {
            ServerAssemblyPath = repository.LspServerAssemblyPath,
            WorkspaceRoot = workspaceRoot,
            LogDirectory = Path.Combine(logDirectory, "language-servers"),
            AnalyzeTypeScript = options.AnalyzeTypeScript,
            TypeScriptProjectPath = "src/Frontend",
            TraceProtocol = options.TraceProtocol,
        },
        TimeProvider.System,
        cancellationToken);

    await lspServer.VerifyLanguageServersPresentAsync(cancellationToken);
    Console.WriteLine($"  MCP server up at {lspServer.BaseUrl}");

    var settings = new ClaudeCodeSettings
    {
        WorkspaceRoot = workspaceRoot,
        McpEndpoint = $"{lspServer.BaseUrl}/mcp",
    };

    Console.WriteLine("Verifying the environment before spending a single turn…");
    var environmentCheck = new AgentEnvironmentCheck(processRunner, workspaceRoot);
    await environmentCheck.VerifyClaudeCodeRespondsAsync(settings.ExecutablePath, cancellationToken);
    await environmentCheck.VerifyFileScopeHookBlocksAsync(settings.HookScriptRelativePath, LayerMap.Default, cancellationToken);
    Console.WriteLine("  claude answers, and the file-scope hook blocks a write outside its layer.");

    var logPath = Path.Combine(logDirectory, $"run-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
    using var jsonlObserver = JsonlRunObserver.AppendingTo(logPath);
    var observer = new FanOutRunObserver([jsonlObserver, new ConsoleRunObserver()]);

    Console.WriteLine();
    Console.WriteLine($"Running the graph. Log: {logPath}");
    Console.WriteLine();

    var runner = new GraphRunner(
        ClaudeCodeAgentRunner.Create(settings, TimeProvider.System),
        lspServer.CreateGateway(),
        observer,
        TimeProvider.System,
        new GraphPolicy { MaximumAttemptsPerNode = options.MaximumAttemptsPerNode });

    var finalState = await runner.RunAsync(spec, cancellationToken);
    var termination = finalState.Termination!;

    Console.WriteLine();
    Console.WriteLine(new string('─', 78));
    Console.WriteLine($"Termination: {termination.Reason} — {termination.Detail}");
    Console.WriteLine($"Workspace:   {workspaceRoot}");
    Console.WriteLine($"Log:         {logPath}");

    return ExitCodes.For(termination.Reason);
}

/// <summary>
/// What the shell learns from a run.
/// </summary>
/// <remarks>
/// Three outcomes rather than the usual two, because "the graph ran and gave up on a ceiling" and
/// "the run never got off the ground" are different things to a person and to a script. A ceiling
/// is the pipeline working as designed (ADR-003); a failure to start is an environment problem.
/// </remarks>
internal static class ExitCodes
{
    public const int Completed = 0;
    public const int StoppedOnACeiling = 1;
    public const int CouldNotStart = 2;

    public static int For(TerminationReason reason) => reason switch
    {
        TerminationReason.Completed => Completed,
        TerminationReason.IterationLimitReached or TerminationReason.NoProgress => StoppedOnACeiling,
        _ => CouldNotStart,
    };
}
