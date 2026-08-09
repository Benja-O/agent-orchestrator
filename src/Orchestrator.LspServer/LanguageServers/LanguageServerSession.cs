using System.Diagnostics;
using System.Text.Json;
using Orchestrator.LspServer.Protocol;
using StreamJsonRpc;

namespace Orchestrator.LspServer.LanguageServers;

/// <summary>
/// A language server running as a child process, spoken to over LSP on its standard streams.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place in the repository that starts a process. The MCP server owns the
/// language servers because it is the thing holding their connections; the orchestrator owns
/// the MCP server (AI.md, golden rule 2, as amended in ADR-013).
/// </para>
/// <para>
/// Shutdown is deterministic on purpose. A language server left alive after a failed run
/// keeps handles open on <c>output/</c>, which ADR-008 requires to be deletable and
/// regenerable from scratch — so an orphan is a bug, not an untidy detail.
/// </para>
/// </remarks>
public abstract class LanguageServerSession : ILanguageServerSession
{
    private static readonly string[] IgnoredDirectorySegments =
    [
        "bin", "obj", "node_modules", ".git", ".vs", "dist", "build",
    ];

    private readonly ILogger _logger;
    private readonly TimeSpan _requestTimeout;
    private readonly bool _traceProtocol;
    private readonly HashSet<string> _openedDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _documentGate = new(1, 1);

    private Process? _process;
    private JsonRpc? _jsonRpc;
    private bool _isDisposed;

    protected LanguageServerSession(
        string workspaceRootFullPath, TimeSpan requestTimeout, bool traceProtocol, ILogger logger)
    {
        WorkspaceRootFullPath = workspaceRootFullPath;
        _requestTimeout = requestTimeout;
        _traceProtocol = traceProtocol;
        _logger = logger;
    }

    public abstract string SourceName { get; }

    public abstract IndexingState IndexingState { get; }

    /// <summary>The file extensions this server owns, lowercase and dotted.</summary>
    protected abstract IReadOnlyList<string> DocumentExtensions { get; }

    protected string WorkspaceRootFullPath { get; }

    protected ILogger Logger => _logger;

    protected JsonRpc Rpc => _jsonRpc
        ?? throw new LanguageServerException($"The {SourceName} language server has not been started.");

    public bool HandlesDocument(string documentFullPath) =>
        DocumentExtensions.Contains(Path.GetExtension(documentFullPath).ToLowerInvariant());

    public IReadOnlyList<string> EnumerateDocuments(string scopeFullPath)
    {
        if (File.Exists(scopeFullPath))
        {
            return HandlesDocument(scopeFullPath) ? [scopeFullPath] : [];
        }

        if (!Directory.Exists(scopeFullPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(scopeFullPath, "*", SearchOption.AllDirectories)
            .Where(HandlesDocument)
            .Where(IsNotInsideIgnoredDirectory)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var startInfo = CreateProcessStartInfo();
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        _logger.LogInformation(
            "Starting the {SourceName} language server: {FileName} {Arguments}",
            SourceName,
            startInfo.FileName,
            string.Join(' ', startInfo.ArgumentList));

        try
        {
            _process = Process.Start(startInfo)
                ?? throw new LanguageServerException($"Could not start the {SourceName} language server.");
        }
        catch (Exception exception) when (exception is not LanguageServerException)
        {
            throw new LanguageServerException(
                $"Could not start the {SourceName} language server from '{startInfo.FileName}'.", exception);
        }

        _process.ErrorDataReceived += (_, eventArguments) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArguments.Data))
            {
                _logger.LogDebug("[{SourceName}] {Message}", SourceName, eventArguments.Data);
            }
        };
        _process.BeginErrorReadLine();

        var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = LspJson.CreateOptions() };
        var messageHandler = new HeaderDelimitedMessageHandler(
            _process.StandardInput.BaseStream,
            _process.StandardOutput.BaseStream,
            formatter);

        _jsonRpc = new JsonRpc(messageHandler);

        if (_traceProtocol)
        {
            _jsonRpc.TraceSource = new System.Diagnostics.TraceSource(
                $"{SourceName}-rpc", System.Diagnostics.SourceLevels.Verbose);
            _jsonRpc.TraceSource.Listeners.Clear();
            _jsonRpc.TraceSource.Listeners.Add(new LoggerTraceListener(_logger, SourceName));
        }

        _jsonRpc.AddLocalRpcTarget(CreateClientEndpoint());
        _jsonRpc.StartListening();

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await PrepareWorkspaceAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(string documentFullPath, CancellationToken cancellationToken)
    {
        await OpenDocumentAsync(documentFullPath, cancellationToken).ConfigureAwait(false);

        var report = await InvokeAsync<LspDocumentDiagnosticReport?>(
            LspMethodNames.DocumentDiagnostic,
            new LspDocumentDiagnosticParams { TextDocument = ToDocumentIdentifier(documentFullPath) },
            cancellationToken).ConfigureAwait(false);

        return report?.Items ?? [];
    }

    public async Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string documentFullPath, LspPosition position, CancellationToken cancellationToken)
    {
        await OpenDocumentAsync(documentFullPath, cancellationToken).ConfigureAwait(false);

        var raw = await InvokeAsync<JsonElement?>(
            LspMethodNames.Definition,
            new LspTextDocumentPositionParams
            {
                TextDocument = ToDocumentIdentifier(documentFullPath),
                Position = position,
            },
            cancellationToken).ConfigureAwait(false);

        return ReadLocations(raw);
    }

    public async Task<LspHover?> GetHoverAsync(string documentFullPath, LspPosition position, CancellationToken cancellationToken)
    {
        await OpenDocumentAsync(documentFullPath, cancellationToken).ConfigureAwait(false);

        return await InvokeAsync<LspHover?>(
            LspMethodNames.Hover,
            new LspTextDocumentPositionParams
            {
                TextDocument = ToDocumentIdentifier(documentFullPath),
                Position = position,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string documentFullPath, LspPosition position, CancellationToken cancellationToken)
    {
        await OpenDocumentAsync(documentFullPath, cancellationToken).ConfigureAwait(false);

        var locations = await InvokeAsync<LspLocation[]?>(
            LspMethodNames.References,
            new LspReferenceParams
            {
                TextDocument = ToDocumentIdentifier(documentFullPath),
                Position = position,
            },
            cancellationToken).ConfigureAwait(false);

        return locations ?? [];
    }

    public async Task<IReadOnlyList<LspDocumentSymbol>> GetDocumentSymbolsAsync(string documentFullPath, CancellationToken cancellationToken)
    {
        await OpenDocumentAsync(documentFullPath, cancellationToken).ConfigureAwait(false);

        var symbols = await InvokeAsync<LspDocumentSymbol[]?>(
            LspMethodNames.DocumentSymbol,
            new LspDocumentSymbolParams { TextDocument = ToDocumentIdentifier(documentFullPath) },
            cancellationToken).ConfigureAwait(false);

        return symbols ?? [];
    }

    public async Task<IReadOnlyList<LspSymbolInformation>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken)
    {
        var symbols = await InvokeAsync<LspSymbolInformation[]?>(
            LspMethodNames.WorkspaceSymbol,
            new LspWorkspaceSymbolParams { Query = query },
            cancellationToken).ConfigureAwait(false);

        return symbols ?? [];
    }

    /// <summary>How this particular server is launched.</summary>
    protected abstract ProcessStartInfo CreateProcessStartInfo();

    /// <summary>The LSP <c>languageId</c> for a file this server owns.</summary>
    protected abstract string GetLanguageId(string documentFullPath);

    /// <summary>
    /// Whatever has to happen after <c>initialized</c> before queries mean anything — for
    /// Roslyn, opening the solution and waiting for it to finish loading.
    /// </summary>
    protected abstract Task PrepareWorkspaceAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The object holding the methods the server may call on us. Overridden where a server
    /// adds its own, as Roslyn does.
    /// </summary>
    protected virtual LspClientEndpoint CreateClientEndpoint() => new(this);

    /// <summary>
    /// Called when the server pushes diagnostics. Ignored by default: Roslyn only answers
    /// pulls. The TypeScript server pushes, and overrides this.
    /// </summary>
    protected virtual void OnDiagnosticsPublished(JsonElement notification)
    {
    }

    internal void HandleDiagnosticsPublished(JsonElement notification) => OnDiagnosticsPublished(notification);

    internal void LogServerMessage(JsonElement message) =>
        _logger.LogDebug("[{SourceName}] {Message}", SourceName, message.ToString());

    protected LspTextDocumentIdentifier ToDocumentIdentifier(string documentFullPath) =>
        new() { Uri = new Uri(documentFullPath).AbsoluteUri };

    protected async Task<TResult> InvokeAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);

        try
        {
            return await Rpc.InvokeWithParameterObjectAsync<TResult>(method, parameters, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanguageServerException(
                $"The {SourceName} language server did not answer '{method}' within {_requestTimeout.TotalSeconds:F0} s.");
        }
        catch (Exception exception) when (exception is RemoteRpcException or ObjectDisposedException)
        {
            throw new LanguageServerException(
                $"The {SourceName} language server failed to answer '{method}'.", exception);
        }
    }

    protected Task NotifyAsync(string method, object parameters) =>
        Rpc.NotifyWithParameterObjectAsync(method, parameters);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var initializeParameters = new
        {
            processId = Environment.ProcessId,
            clientInfo = new { name = "Orchestrator.LspServer", version = "1.0.0" },
            rootUri = new Uri(WorkspaceRootFullPath).AbsoluteUri,
            workspaceFolders = new[]
            {
                new { uri = new Uri(WorkspaceRootFullPath).AbsoluteUri, name = Path.GetFileName(WorkspaceRootFullPath) },
            },
            capabilities = new
            {
                workspace = new
                {
                    configuration = true,
                    workspaceFolders = true,
                    diagnostics = new { refreshSupport = false },
                    symbol = new { dynamicRegistration = false },
                },
                textDocument = new
                {
                    synchronization = new { dynamicRegistration = false, didSave = false },
                    diagnostic = new { dynamicRegistration = false, relatedDocumentSupport = false },
                    definition = new { dynamicRegistration = false, linkSupport = true },
                    references = new { dynamicRegistration = false },
                    hover = new { dynamicRegistration = false, contentFormat = new[] { "markdown", "plaintext" } },
                    documentSymbol = new { dynamicRegistration = false, hierarchicalDocumentSymbolSupport = true },
                    publishDiagnostics = new { relatedInformation = false },
                },
                window = new { workDoneProgress = true },
            },
        };

        await InvokeAsync<JsonElement>(LspMethodNames.Initialize, initializeParameters, cancellationToken)
            .ConfigureAwait(false);

        await NotifyAsync(LspMethodNames.Initialized, new { }).ConfigureAwait(false);
    }

    protected async Task OpenDocumentAsync(string documentFullPath, CancellationToken cancellationToken)
    {
        await _documentGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_openedDocuments.Add(documentFullPath))
            {
                return;
            }

            var text = await File.ReadAllTextAsync(documentFullPath, cancellationToken).ConfigureAwait(false);

            await NotifyAsync(LspMethodNames.DidOpenTextDocument, new LspDidOpenTextDocumentParams
            {
                TextDocument = new LspTextDocumentItem
                {
                    Uri = new Uri(documentFullPath).AbsoluteUri,
                    LanguageId = GetLanguageId(documentFullPath),
                    Version = 1,
                    Text = text,
                },
            }).ConfigureAwait(false);
        }
        finally
        {
            _documentGate.Release();
        }
    }

    /// <summary>
    /// <c>textDocument/definition</c> may answer with a single location, an array of them, or
    /// an array of link objects. All three shapes are legal, so the payload stays raw until here.
    /// </summary>
    private static IReadOnlyList<LspLocation> ReadLocations(JsonElement? raw)
    {
        if (raw is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        var options = LspJson.CreateOptions();

        if (element.ValueKind == JsonValueKind.Object)
        {
            return [ReadLocation(element, options)];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Select(item => ReadLocation(item, options))
            .Where(location => location.Uri.Length > 0)
            .ToList();
    }

    private static LspLocation ReadLocation(JsonElement element, JsonSerializerOptions options)
    {
        if (element.TryGetProperty("targetUri", out _))
        {
            var link = element.Deserialize<LspLocationLink>(options);
            return link is null
                ? new LspLocation()
                : new LspLocation { Uri = link.TargetUri, Range = link.TargetSelectionRange };
        }

        return element.Deserialize<LspLocation>(options) ?? new LspLocation();
    }

    private static bool IsNotInsideIgnoredDirectory(string fullPath) =>
        !fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => IgnoredDirectorySegments.Contains(segment, StringComparer.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);

        if (_jsonRpc is not null)
        {
            try
            {
                using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _jsonRpc.InvokeWithParameterObjectAsync<object?>(LspMethodNames.Shutdown, new { }, shutdownTimeout.Token)
                    .ConfigureAwait(false);
                await _jsonRpc.NotifyAsync(LspMethodNames.Exit).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "The {SourceName} language server did not shut down cleanly; killing it.", SourceName);
            }

            _jsonRpc.Dispose();
        }

        if (_process is not null)
        {
            try
            {
                if (!_process.WaitForExit(3_000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process was already gone; nothing to shut down.
            }

            _process.Dispose();
        }

        _documentGate.Dispose();
    }
}
