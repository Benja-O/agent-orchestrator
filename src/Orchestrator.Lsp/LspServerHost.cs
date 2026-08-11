using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ModelContextProtocol.Client;

namespace Orchestrator.Lsp;

/// <summary>Where the MCP server lives and what workspace it should analyse.</summary>
public sealed record LspServerSettings
{
    /// <summary>Path to <c>Orchestrator.LspServer.dll</c>, launched through <c>dotnet</c>.</summary>
    public required string ServerAssemblyPath { get; init; }

    /// <summary>The generated application's root. The server refuses to start if it does not exist.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>Where the language servers write their own logs.</summary>
    public required string LogDirectory { get; init; }

    /// <summary>0 asks the operating system for a free one.</summary>
    public int Port { get; init; }

    /// <summary>How long to wait for the HTTP host to answer at all. Not how long to wait for indexing.</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Dumps every LSP message. The only way to see a language server that goes quiet instead of failing.</summary>
    public bool TraceProtocol { get; init; }

    /// <summary>
    /// Whether to analyse TypeScript as well as C#.
    /// </summary>
    /// <remarks>
    /// <c>typescript-language-server</c> has to be installed in the analysed workspace's own
    /// <c>node_modules</c>, so it has nothing to run against until the frontend exists. Turning
    /// it off matters more than it looks: the contract answers <c>indexing</c> for the whole
    /// scope while <em>any</em> server is still indexing, so a server that can never become ready
    /// would keep the gate from ever giving a verdict about the C# that is ready.
    /// </remarks>
    public bool AnalyzeTypeScript { get; init; } = true;

    /// <summary>
    /// Where the TypeScript project lives inside the workspace, relative to its root.
    /// </summary>
    /// <remarks>
    /// It is not cosmetic and it is the other half of what makes TypeScript work at all: the
    /// session prefers the copy of <c>typescript-language-server</c> in the analysed project's own
    /// <c>node_modules</c>, and falls back to the PATH — where, in a generated workspace, there is
    /// nothing. Pointing at the folder the scaffold actually installs into (ADR-016) is what turns
    /// that fallback from the normal case into the failure it is meant to be.
    /// </remarks>
    public string TypeScriptProjectPath { get; init; } = string.Empty;
}

/// <summary>
/// Owns the MCP server process for one run, and the MCP client session onto it.
/// </summary>
/// <remarks>
/// <para>
/// The middle level of the three-process hierarchy ADR-013 describes: the orchestrator launches
/// this server, and the server owns the two language servers. Flattening it was rejected there —
/// whoever holds the LSP connections has to be whoever answers the tool calls, or the gate and
/// the layer agents end up looking at two different realities.
/// </para>
/// <para>
/// <strong>Shutdown kills the whole tree, and that is not tidiness.</strong> A language server
/// that survives a failed run keeps handles open on <c>output/</c>, which ADR-008 requires to be
/// deletable and regenerable from scratch — so an orphan does not merely linger, it breaks the
/// next run.
/// </para>
/// </remarks>
public sealed class LspServerHost : IAsyncDisposable
{
    private readonly Process _process;
    private readonly McpClient _client;

    public string BaseUrl { get; }

    private LspServerHost(Process process, McpClient client, string baseUrl)
    {
        _process = process;
        _client = client;
        BaseUrl = baseUrl;
    }

    /// <summary>
    /// Starts the server and waits until it answers, then opens the MCP session.
    /// </summary>
    /// <remarks>
    /// Waiting for <c>/health</c> to answer is <em>not</em> waiting for the workspace to be
    /// indexed, and conflating the two would be the false green all over again. The endpoint
    /// answering means the layer is alive; whether its answers can be trusted yet is what the
    /// contract's <c>status</c> field says, run after run, to the gate.
    /// </remarks>
    public static async Task<LspServerHost> StartAsync(
        LspServerSettings settings,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.ServerAssemblyPath))
        {
            throw new LspGatewayException(
                $"The MCP server assembly is missing: {settings.ServerAssemblyPath}. Build Orchestrator.LspServer first.");
        }

        var port = settings.Port == 0 ? FindFreePort() : settings.Port;
        var baseUrl = $"http://127.0.0.1:{port}";
        var process = Launch(settings, baseUrl);

        try
        {
            await WaitUntilAnsweringAsync(process, $"{baseUrl}/health", settings.StartupTimeout, timeProvider, cancellationToken)
                .ConfigureAwait(false);

            var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri($"{baseUrl}/mcp") });
            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);

            return new LspServerHost(process, client, baseUrl);
        }
        catch
        {
            KillTree(process);
            process.Dispose();
            throw;
        }
    }

    public McpLanguageServerGateway CreateGateway() => McpLanguageServerGateway.Over(_client);

    /// <summary>
    /// Fails fast if the server is up but has no language server behind it.
    /// </summary>
    /// <remarks>
    /// The startup check AI.md asks for, applied to this layer: discovering at the third graph
    /// node that nothing is analysing the code wastes everything spent up to there — and, worse,
    /// the run would look like it was working.
    /// </remarks>
    public async Task VerifyLanguageServersPresentAsync(CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var health = await httpClient.GetStringAsync($"{BaseUrl}/health", cancellationToken).ConfigureAwait(false);

        if (!health.Contains("\"source\"", StringComparison.Ordinal))
        {
            throw new LspGatewayException(
                $"The MCP server is answering but reports no language servers, so nothing would be verifying the "
                + $"generated code. /health said: {health}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The session is being torn down; a client that cannot say goodbye politely must not
            // stop us from killing the process it was talking to.
        }

        KillTree(_process);
        _process.Dispose();
    }

    private static Process Launch(LspServerSettings settings, string baseUrl)
    {
        var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        startInfo.Environment["Logging__LogLevel__Microsoft"] = "Warning";
        startInfo.Environment["Logging__LogLevel__ModelContextProtocol"] = "Warning";

        // Roslyn emits diagnostics in the machine's language, and those messages do not stay in
        // the log: they are pasted into the prompt of the agent that has to fix the code
        // (block 2, third finding).
        startInfo.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";

        startInfo.ArgumentList.Add(settings.ServerAssemblyPath);
        startInfo.ArgumentList.Add($"--urls={baseUrl}");
        startInfo.ArgumentList.Add($"--LspServer:WorkspaceRoot={settings.WorkspaceRoot}");
        startInfo.ArgumentList.Add($"--LspServer:LogDirectory={settings.LogDirectory}");

        if (!settings.AnalyzeTypeScript)
        {
            startInfo.ArgumentList.Add("--LspServer:TypeScript:Enabled=false");
        }
        else if (settings.TypeScriptProjectPath.Length > 0)
        {
            startInfo.ArgumentList.Add($"--LspServer:TypeScript:ProjectPath={settings.TypeScriptProjectPath}");
        }

        if (settings.TraceProtocol)
        {
            startInfo.ArgumentList.Add("--LspServer:TraceProtocol=true");
        }

        return Process.Start(startInfo)
            ?? throw new LspGatewayException("The MCP server process could not be started.");
    }

    private static async Task WaitUntilAnsweringAsync(
        Process process,
        string healthUrl,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var startedAt = timeProvider.GetTimestamp();

        while (timeProvider.GetElapsedTime(startedAt) < timeout)
        {
            // Checked before the request, not after: a server that died on startup would
            // otherwise keep us polling a port nobody is listening on until the timeout, and
            // report a timeout instead of the real reason.
            if (process.HasExited)
            {
                throw new LspGatewayException(
                    $"The MCP server exited with code {process.ExitCode} before it started answering.");
            }

            try
            {
                using var response = await httpClient.GetAsync(healthUrl, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken).ConfigureAwait(false);
        }

        throw new LspGatewayException($"The MCP server never answered {healthUrl} within {timeout}.");
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(10));
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
