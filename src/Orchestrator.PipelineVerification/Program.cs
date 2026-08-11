using Orchestrator.Agents;
using Orchestrator.Application.Graph;
using Orchestrator.Application.Spec;
using Orchestrator.Domain;
using Orchestrator.Lsp;
using Orchestrator.Observability;
using Orchestrator.PipelineVerification;

// The exit criterion of Block 4, made reproducible:
//
//   "An error injected on purpose sends the graph back to the agent of that layer, and the next
//    iteration fixes it. Visible in the log."
//
// This is NOT a test and deliberately does not live in a test project: it invokes `claude -p`
// and starts a real language server, both of which golden rule 3 of AI.md forbids inside the
// suite. It is the manual verification the block asks for, wrapped in one command.
//
//   dotnet run --project src/Orchestrator.PipelineVerification
//
// It costs real quota of the Pro plan, which is why everything that could be verified without
// an agent already was, in the 204 tests that run in seconds.

var repositoryRoot = RepositoryRoot.Find();
var workspaceRoot = Path.Combine(repositoryRoot, "output");
var logDirectory = Path.Combine(repositoryRoot, "logs");
Directory.CreateDirectory(logDirectory);

var serverAssembly = Path.Combine(
    repositoryRoot, "src", "Orchestrator.LspServer", "bin", "Debug", "net10.0", "Orchestrator.LspServer.dll");

if (!File.Exists(serverAssembly))
{
    Console.Error.WriteLine($"Build the server first: dotnet build src/Orchestrator.LspServer\nMissing: {serverAssembly}");
    return 2;
}

var specResult = SpecParser.Parse("specs/verification-minimal.md", MinimalSpec.Text);

if (specResult.IsFailure)
{
    Console.Error.WriteLine($"The verification spec does not parse: {specResult.FailureReason}");
    return 2;
}

var spec = specResult.Value;

Console.WriteLine("Preparing the generated workspace from scratch (ADR-008)…");

new GeneratedWorkspacePreparer(new WorkspaceLayout
{
    TemplatesDirectory = Path.Combine(repositoryRoot, "templates"),
    WorkspaceRoot = workspaceRoot,

    // Rewritten below once the port is known. The runner does not depend on this file — it
    // passes --mcp-config on every invocation — so the placeholder is only for a person opening
    // the workspace by hand.
    McpEndpoint = "http://127.0.0.1:0/mcp",
}).Prepare(spec);

// The scaffold the language servers need — solution, projects, frontend package files — now
// comes out of the preparer with everything else (ADR-016). It used to live in this harness as
// CSharpSkeleton, which is exactly the andamio block 5 was supposed to move into the product.
var processRunner = new SystemAgentProcessRunner(TimeSpan.FromMinutes(10), TimeProvider.System);

Console.WriteLine("Restoring the scaffold's dependencies…");
await new GeneratedWorkspaceRestorer(processRunner, workspaceRoot).RestoreAsync(CancellationToken.None);

Console.WriteLine("Starting the MCP server…");

await using var lspServer = await LspServerHost.StartAsync(
    new LspServerSettings
    {
        ServerAssemblyPath = serverAssembly,
        WorkspaceRoot = workspaceRoot,
        LogDirectory = Path.Combine(logDirectory, "language-servers"),

        // There is no node_modules in a workspace that does not exist yet, so the TypeScript
        // server could never become ready — and the contract answers `indexing` for the whole
        // scope while any server is indexing. Block 5's problem, not this one's.
        AnalyzeTypeScript = false,
    },
    TimeProvider.System,
    CancellationToken.None);

await lspServer.VerifyLanguageServersPresentAsync(CancellationToken.None);
Console.WriteLine($"  MCP server up at {lspServer.BaseUrl}");

var settings = new ClaudeCodeSettings
{
    WorkspaceRoot = workspaceRoot,
    McpEndpoint = $"{lspServer.BaseUrl}/mcp",
};

var environmentCheck = new AgentEnvironmentCheck(processRunner, workspaceRoot);

Console.WriteLine("Verifying the environment before spending a single turn…");
await environmentCheck.VerifyClaudeCodeRespondsAsync(settings.ExecutablePath, CancellationToken.None);
await environmentCheck.VerifyFileScopeHookBlocksAsync(
    settings.HookScriptRelativePath, LayerMap.Default, CancellationToken.None);
Console.WriteLine("  claude answers, and the file-scope hook blocks a write outside its layer.");

// The injection. A decorator over the real runner rather than a hand-edit of output/, which
// CLAUDE.md forbids for good reason: an error planted by a person is an error the pipeline never
// had to survive. This one arrives exactly where a real one would — after the domain agent's
// first turn, in a file the gate is about to read.
var faultInjector = new FaultInjectingAgentRunner(
    ClaudeCodeAgentRunner.Create(settings, TimeProvider.System),
    workspaceRoot);

var logPath = Path.Combine(logDirectory, $"pipeline-verification-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
using var jsonlObserver = JsonlRunObserver.AppendingTo(logPath);
var transcript = new VerificationTranscript();
var observer = new FanOutRunObserver([jsonlObserver, new ConsoleRunObserver(), transcript]);

Console.WriteLine();
Console.WriteLine($"Running the graph. Log: {logPath}");
Console.WriteLine();

var runner = new GraphRunner(
    faultInjector,
    lspServer.CreateGateway(),
    observer,
    TimeProvider.System,

    // Two, not the default three, while debugging. Each extra attempt is another paid turn per
    // layer, and the block prompt is explicit that this is where R1 materialises.
    new GraphPolicy { MaximumAttemptsPerNode = 2 });

var finalState = await runner.RunAsync(spec, CancellationToken.None);

Console.WriteLine();
Console.WriteLine(new string('─', 78));

var verdict = transcript.Evaluate(faultInjector.InjectedFilePath);

foreach (var line in verdict.Lines)
{
    Console.WriteLine(line);
}

Console.WriteLine(new string('─', 78));
Console.WriteLine($"Termination: {finalState.Termination?.Reason} — {finalState.Termination?.Detail}");
Console.WriteLine($"Full log: {logPath}");

return verdict.Passed ? 0 : 1;
