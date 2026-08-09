using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// The exit criterion of Block 2, made reproducible.
//
// This is NOT a test and it deliberately does not live in a test project: it starts real
// language servers, which golden rule 3 of AI.md forbids inside the suite. It is the manual
// verification the block asks for, wrapped in one command so it can be re-run and shown.
//
//   dotnet run --project src/Orchestrator.LspServer.ManualVerification
//
// Per language server it asserts four things:
//   1. Asked before the workspace has loaded, the server answers "indexing" — not an empty
//      clean verdict. This is the false green the whole contract is built to prevent.
//   2. Once ready, diagnostics reports the error planted in the fixture.
//   3. definition on a healthy call lands on the declaration in the other file, with a signature.
//   4. references, documentSymbol and workspaceSymbol answer — the half of the LSP layer that a
//      'dotnet build' could not give, and the criterion ADR-004 set against itself.

var repositoryRoot = FindRepositoryRoot();
var serverDllPath = Path.Combine(
    repositoryRoot, "src", "Orchestrator.LspServer", "bin", "Debug", "net10.0", "Orchestrator.LspServer.dll");

if (!File.Exists(serverDllPath))
{
    Console.Error.WriteLine($"Build the server first: dotnet build src/Orchestrator.LspServer\nMissing: {serverDllPath}");
    return 2;
}

var basePort = args.FirstOrDefault(argument => int.TryParse(argument, out _)) is { } portArgument
    ? int.Parse(portArgument)
    : 5599;

// --only <text> runs a single scenario. Useful while debugging one language server; the block
// is only verified when both run.
var onlyIndex = Array.IndexOf(args, "--only");
var onlyFilter = onlyIndex >= 0 && onlyIndex + 1 < args.Length ? args[onlyIndex + 1] : null;

// --trace dumps every LSP message. Verbose, and the only way to see a language server that
// goes quiet instead of failing — which is how this integration actually breaks.
var traceProtocol = args.Contains("--trace");

var scenarios = new[]
{
    new Scenario(
        Name: "C# — Roslyn",
        FixtureDirectory: Path.Combine(repositoryRoot, "fixtures", "broken-csharp"),
        ExpectedSource: "roslyn",
        BrokenFile: "Api/TareasController.cs",
        BrokenLine: 27,
        ExpectedCode: "CS1061",
        CallFile: "Api/TareasController.cs",
        CallLine: 17,
        CallColumn: 22,
        DeclarationFile: "Domain/Tarea.cs",
        DeclarationLine: 19,
        DeclarationColumn: 17,
        SymbolQuery: "Tarea",
        OtherServerSetting: "--LspServer:TypeScript:Enabled=false"),

    new Scenario(
        Name: "TypeScript — typescript-language-server",
        FixtureDirectory: Path.Combine(repositoryRoot, "fixtures", "broken-typescript"),
        ExpectedSource: "typescript",
        BrokenFile: "src/tareasView.ts",
        BrokenLine: 13,
        ExpectedCode: "2339",
        CallFile: "src/tareasView.ts",
        CallLine: 8,
        CallColumn: 16,
        DeclarationFile: "src/tarea.ts",
        DeclarationLine: 11,
        DeclarationColumn: 10,
        SymbolQuery: "Tarea",
        OtherServerSetting: "--LspServer:Roslyn:Enabled=false"),
};

var allFailures = new List<string>();
var port = basePort;

foreach (var scenario in scenarios)
{
    if (onlyFilter is not null && !scenario.Name.Contains(onlyFilter, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    Console.WriteLine();
    Console.WriteLine(new string('=', 78));
    Console.WriteLine($"  {scenario.Name}");
    Console.WriteLine($"  {scenario.FixtureDirectory}");
    Console.WriteLine(new string('=', 78));
    Console.WriteLine();

    var failures = await RunScenarioAsync(scenario, serverDllPath, repositoryRoot, port++, traceProtocol);
    allFailures.AddRange(failures.Select(failure => $"[{scenario.Name}] {failure}"));
}

Console.WriteLine();
if (allFailures.Count == 0)
{
    Console.WriteLine("VERIFICATION PASSED — real diagnostics and correct definitions, from both real language servers.");
    return 0;
}

Console.WriteLine("VERIFICATION FAILED:");
allFailures.ForEach(failure => Console.WriteLine($"  - {failure}"));
return 1;

static async Task<List<string>> RunScenarioAsync(
    Scenario scenario, string serverDllPath, string repositoryRoot, int port, bool traceProtocol)
{
    var failures = new List<string>();
    var baseUrl = $"http://127.0.0.1:{port}";

    PrepareFixture(scenario);

    Step($"Starting the LSP MCP server on {baseUrl}");
    using var server = StartServer(serverDllPath, scenario, baseUrl, repositoryRoot, traceProtocol);

    // The server owns a language server of its own. Leaving either behind would keep handles
    // open on the workspace, which is exactly the orphan this project calls a bug.
    using var registration = new ProcessKiller(server);

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    await WaitForHttpAsync(httpClient, $"{baseUrl}/health", TimeSpan.FromSeconds(60));

    var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri($"{baseUrl}/mcp") });
    await using var client = await McpClient.CreateAsync(transport);

    var tools = await client.ListToolsAsync();
    Console.WriteLine($"Tools exposed   : {string.Join(", ", tools.Select(tool => tool.Name))}");
    Console.WriteLine();

    Step("1. Asking for diagnostics while the workspace is still loading");
    var earlyDiagnostics = await CallAsync(client, "diagnostics", new() { ["scope"] = "." });
    Print(earlyDiagnostics);

    var earlyStatus = earlyDiagnostics.GetProperty("status").GetString();
    Console.WriteLine(earlyStatus == "indexing"
        ? "   OK — answered 'indexing' instead of an empty clean verdict.\n"
        : $"   Inconclusive — already loaded (status '{earlyStatus}'); this check only means something on a cold start.\n");

    Step("2. Waiting for the workspace to load, then asking again");
    var diagnostics = await PollUntilReadyAsync(client, TimeSpan.FromMinutes(4));
    Print(diagnostics);

    var brokenCall = diagnostics.GetProperty("items").EnumerateArray().FirstOrDefault(item =>
        item.GetProperty("filePath").GetString() == scenario.BrokenFile &&
        item.GetProperty("severity").GetString() == "error" &&
        item.GetProperty("range").GetProperty("startLine").GetInt32() == scenario.BrokenLine);

    if (brokenCall.ValueKind == JsonValueKind.Object)
    {
        var code = brokenCall.GetProperty("code").GetString();
        var source = brokenCall.GetProperty("source").GetString();
        Console.WriteLine($"   OK — {code} at {scenario.BrokenFile}:{scenario.BrokenLine} from source '{source}'.\n");

        if (code != scenario.ExpectedCode)
        {
            failures.Add($"Expected code {scenario.ExpectedCode}, got {code}.");
        }

        if (source != scenario.ExpectedSource)
        {
            failures.Add($"Expected source {scenario.ExpectedSource}, got {source}.");
        }
    }
    else
    {
        failures.Add($"No error reported at {scenario.BrokenFile}:{scenario.BrokenLine}.");
    }

    Step($"3. definition at {scenario.CallFile}:{scenario.CallLine}:{scenario.CallColumn}");
    var definition = await CallAsync(client, "definition", new()
    {
        ["filePath"] = scenario.CallFile,
        ["line"] = scenario.CallLine,
        ["column"] = scenario.CallColumn,
    });
    Print(definition);

    var definitionFile = definition.TryGetProperty("filePath", out var filePathElement) ? filePathElement.GetString() : null;
    var definitionLine = definition.TryGetProperty("range", out var rangeElement)
        ? rangeElement.GetProperty("startLine").GetInt32()
        : -1;

    if (definitionFile == scenario.DeclarationFile && definitionLine == scenario.DeclarationLine)
    {
        var signature = definition.TryGetProperty("signature", out var signatureElement) ? signatureElement.GetString() : null;
        Console.WriteLine($"   OK — landed on {definitionFile}:{definitionLine}.");
        Console.WriteLine($"   Signature: {signature ?? "(none)"}\n");

        if (string.IsNullOrWhiteSpace(signature))
        {
            failures.Add("definition returned no signature, which is the field that makes the tool worth having.");
        }
    }
    else
    {
        failures.Add($"definition landed on {definitionFile}:{definitionLine}, " +
                     $"expected {scenario.DeclarationFile}:{scenario.DeclarationLine}.");
    }

    Step("4. The navigation half of the contract");
    var documentSymbols = await CallAsync(client, "documentSymbol", new() { ["filePath"] = scenario.DeclarationFile });
    Print(documentSymbols);
    if (documentSymbols.GetProperty("items").GetArrayLength() == 0)
    {
        failures.Add($"documentSymbol returned nothing for {scenario.DeclarationFile}.");
    }

    var references = await CallAsync(client, "references", new()
    {
        ["filePath"] = scenario.DeclarationFile,
        ["line"] = scenario.DeclarationLine,
        ["column"] = scenario.DeclarationColumn,
    });
    Print(references);
    if (references.GetProperty("total").GetInt32() == 0)
    {
        failures.Add("references returned nothing for a symbol that is used elsewhere.");
    }

    // workspaceSymbol is polled on purpose. A language server can report the workspace as loaded
    // while its symbol index is still warming, and an early query then comes back empty — which
    // in the contract is indistinguishable from "that symbol does not exist".
    var symbolAttempts = 0;
    JsonElement workspaceSymbols;
    do
    {
        workspaceSymbols = await CallAsync(client, "workspaceSymbol", new() { ["query"] = scenario.SymbolQuery });
        symbolAttempts++;

        if (workspaceSymbols.GetProperty("items").GetArrayLength() > 0)
        {
            break;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }
    while (symbolAttempts < 15);

    Console.WriteLine($"   workspaceSymbol answered on attempt {symbolAttempts}.");
    Print(workspaceSymbols);

    if (workspaceSymbols.GetProperty("items").GetArrayLength() == 0)
    {
        failures.Add($"workspaceSymbol never returned a symbol for '{scenario.SymbolQuery}'.");
    }

    return failures;
}

static void Step(string message) => Console.WriteLine($"── {message}");

/// <summary>
/// Brings the fixture to the state a language server needs. Unresolved references make Roslyn
/// report hundreds of diagnostics that say nothing about the error we planted.
/// </summary>
static void PrepareFixture(Scenario scenario)
{
    var solution = Directory.EnumerateFiles(scenario.FixtureDirectory, "*.slnx").FirstOrDefault();
    if (solution is not null)
    {
        Step("Restoring the fixture so Roslyn sees resolved references");
        var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(solution);

        using var restore = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet restore.");
        restore.WaitForExit();
    }

    var packageManifest = Path.Combine(scenario.FixtureDirectory, "package.json");
    if (File.Exists(packageManifest) && !Directory.Exists(Path.Combine(scenario.FixtureDirectory, "node_modules")))
    {
        throw new InvalidOperationException(
            $"Run 'npm install' in {scenario.FixtureDirectory} first: the TypeScript language server is " +
            "installed per workspace, not globally.");
    }
}

static void Print(JsonElement payload) =>
    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
}

static Process StartServer(
    string serverDllPath, Scenario scenario, string baseUrl, string repositoryRoot, bool traceProtocol)
{
    var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = repositoryRoot };
    startInfo.Environment["Logging__LogLevel__Microsoft"] = "Warning";
    startInfo.Environment["Logging__LogLevel__ModelContextProtocol"] = "Warning";
    startInfo.ArgumentList.Add(serverDllPath);
    startInfo.ArgumentList.Add($"--urls={baseUrl}");
    startInfo.ArgumentList.Add($"--LspServer:WorkspaceRoot={scenario.FixtureDirectory}");
    startInfo.ArgumentList.Add($"--LspServer:LogDirectory={Path.Combine(repositoryRoot, "logs", "language-servers")}");
    startInfo.ArgumentList.Add(scenario.OtherServerSetting);

    if (traceProtocol)
    {
        startInfo.ArgumentList.Add("--LspServer:TraceProtocol=true");
    }

    return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the LSP server.");
}

static async Task WaitForHttpAsync(HttpClient httpClient, string url, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (HttpRequestException)
        {
            // The host is not listening yet.
        }

        await Task.Delay(250);
    }

    throw new TimeoutException($"{url} never came up.");
}

static async Task<JsonElement> CallAsync(McpClient client, string toolName, Dictionary<string, object?> arguments)
{
    var result = await client.CallToolAsync(toolName, arguments);
    var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    if (result.IsError == true)
    {
        throw new InvalidOperationException($"The tool '{toolName}' returned an error: {text}");
    }

    return JsonDocument.Parse(text).RootElement.Clone();
}

/// <summary>
/// Polls until the server stops saying "indexing". Polling is what the orchestrator's gate has
/// to do too: 'indexing' means wait and ask again, never approve.
/// </summary>
static async Task<JsonElement> PollUntilReadyAsync(McpClient client, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    var attempt = 0;

    while (DateTime.UtcNow < deadline)
    {
        var response = await CallAsync(client, "diagnostics", new() { ["scope"] = "." });
        attempt++;

        if (response.GetProperty("status").GetString() == "ready")
        {
            Console.WriteLine($"   Ready after {attempt} poll(s).");
            return response;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    throw new TimeoutException("The language server never left 'indexing'.");
}

internal sealed record Scenario(
    string Name,
    string FixtureDirectory,
    string ExpectedSource,
    string BrokenFile,
    int BrokenLine,
    string ExpectedCode,
    string CallFile,
    int CallLine,
    int CallColumn,
    string DeclarationFile,
    int DeclarationLine,
    int DeclarationColumn,
    string SymbolQuery,
    string OtherServerSetting);

/// <summary>Kills the server and everything it started, however this scenario ends.</summary>
internal sealed class ProcessKiller : IDisposable
{
    private readonly Process _process;

    public ProcessKiller(Process process) => _process = process;

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
