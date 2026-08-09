using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Orchestrator.LspServer.Configuration;
using Orchestrator.LspServer.Protocol;
using Orchestrator.LspServer.Workspace;

namespace Orchestrator.LspServer.LanguageServers;

/// <summary>
/// The TypeScript/React side: <c>typescript-language-server</c> over stdio.
/// </summary>
/// <remarks>
/// It differs from Roslyn in the one way that matters here: it <b>pushes</b> diagnostics with
/// <c>textDocument/publishDiagnostics</c> after a document is opened, rather than answering a
/// pull. So the wait for an answer is per document and explicit, and a document that never
/// gets a publication is reported as still indexing instead of as clean.
/// </remarks>
public sealed class TypeScriptLanguageServerSession : LanguageServerSession
{
    private static readonly TimeSpan PublicationTimeout = TimeSpan.FromSeconds(15);

    private readonly TypeScriptOptions _options;
    private readonly ConcurrentDictionary<string, IReadOnlyList<LspDiagnostic>> _publishedDiagnostics =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _publicationSignals =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _isInitialized;

    public TypeScriptLanguageServerSession(
        string workspaceRootFullPath,
        TypeScriptOptions options,
        TimeSpan requestTimeout,
        bool traceProtocol,
        ILogger<TypeScriptLanguageServerSession> logger)
        : base(workspaceRootFullPath, requestTimeout, traceProtocol, logger)
    {
        _options = options;
    }

    public override string SourceName => Contract.DiagnosticSourceNames.TypeScript;

    public override IndexingState IndexingState => _isInitialized
        ? IndexingState.Ready
        : new IndexingState(false, "the TypeScript language server is still starting");

    protected override IReadOnlyList<string> DocumentExtensions =>
        [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"];

    protected override ProcessStartInfo CreateProcessStartInfo()
    {
        var projectDirectory = string.IsNullOrWhiteSpace(_options.ProjectPath)
            ? WorkspaceRootFullPath
            : Path.GetFullPath(Path.Combine(WorkspaceRootFullPath, _options.ProjectPath));

        // Prefer the copy installed in the project's own node_modules, and launch its script
        // with node directly. Going through the npm shim would mean cmd.exe on Windows, whose
        // argument quoting is its own class of bug; and requiring a global install would put a
        // machine-wide dependency in the way of a workspace the orchestrator generates from
        // scratch.
        var installedEntryPoint = Path.Combine(
            projectDirectory, "node_modules", "typescript-language-server", "lib", "cli.mjs");

        var startInfo = File.Exists(installedEntryPoint)
            ? new ProcessStartInfo("node")
            : new ProcessStartInfo(_options.Command);

        startInfo.WorkingDirectory = projectDirectory;

        if (File.Exists(installedEntryPoint))
        {
            startInfo.ArgumentList.Add(installedEntryPoint);
        }

        startInfo.ArgumentList.Add("--stdio");

        return startInfo;
    }

    protected override string GetLanguageId(string documentFullPath) =>
        Path.GetExtension(documentFullPath).ToLowerInvariant() switch
        {
            ".tsx" => "typescriptreact",
            ".jsx" => "javascriptreact",
            ".js" or ".mjs" or ".cjs" => "javascript",
            _ => "typescript",
        };

    protected override Task PrepareWorkspaceAsync(CancellationToken cancellationToken)
    {
        _isInitialized = true;
        return Task.CompletedTask;
    }

    protected override void OnDiagnosticsPublished(JsonElement notification)
    {
        if (!notification.TryGetProperty("uri", out var uriElement))
        {
            return;
        }

        var uri = uriElement.GetString();
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        var fullPath = WorkspacePaths.FromUri(uri);
        var diagnostics = notification.TryGetProperty("diagnostics", out var diagnosticsElement)
            ? diagnosticsElement.Deserialize<LspDiagnostic[]>(LspJson.CreateOptions()) ?? []
            : [];

        _publishedDiagnostics[fullPath] = diagnostics;
        GetPublicationSignal(fullPath).TrySetResult();
    }

    /// <summary>
    /// Waits for this document's first publication rather than reading whatever happens to be
    /// cached. Returning the empty cache early would be the push-based version of the false
    /// green the contract exists to prevent.
    /// </summary>
    public override async Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(
        string documentFullPath, CancellationToken cancellationToken)
    {
        var signal = GetPublicationSignal(documentFullPath);

        await OpenDocumentAsync(documentFullPath, cancellationToken).ConfigureAwait(false);

        var completed = await Task.WhenAny(signal.Task, Task.Delay(PublicationTimeout, cancellationToken))
            .ConfigureAwait(false);

        if (completed != signal.Task)
        {
            throw new LanguageServerException(
                $"typescript-language-server published no diagnostics for '{documentFullPath}' " +
                $"within {PublicationTimeout.TotalSeconds:F0} s.");
        }

        return _publishedDiagnostics.TryGetValue(documentFullPath, out var diagnostics) ? diagnostics : [];
    }

    private TaskCompletionSource GetPublicationSignal(string documentFullPath) =>
        _publicationSignals.GetOrAdd(
            documentFullPath,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
