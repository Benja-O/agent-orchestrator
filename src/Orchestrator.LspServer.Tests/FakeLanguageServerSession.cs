using Orchestrator.LspServer.LanguageServers;
using Orchestrator.LspServer.Protocol;

namespace Orchestrator.LspServer.Tests;

/// <summary>
/// A language server session that is entirely made up.
/// </summary>
/// <remarks>
/// This is what makes golden rule 3 of AI.md hold for this project: the whole tool surface —
/// including the readiness rule the gate depends on — is exercised without starting a
/// language server, without a process and without waiting for an index. A suite that took
/// minutes would be a suite that was invoking something real.
/// </remarks>
public sealed class FakeLanguageServerSession : ILanguageServerSession
{
    public FakeLanguageServerSession(string sourceName, params string[] extensions)
    {
        SourceName = sourceName;
        Extensions = extensions.Length > 0 ? extensions : [".cs"];
    }

    public string SourceName { get; }

    public IReadOnlyList<string> Extensions { get; }

    public IndexingState IndexingState { get; set; } = IndexingState.Ready;

    /// <summary>Diagnostics served for any document, keyed by full path; the fallback key is <c>*</c>.</summary>
    public Dictionary<string, IReadOnlyList<LspDiagnostic>> Diagnostics { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<LspLocation> Definitions { get; } = [];

    public List<LspLocation> References { get; } = [];

    public List<LspDocumentSymbol> DocumentSymbols { get; } = [];

    public List<LspSymbolInformation> WorkspaceSymbols { get; } = [];

    public LspHover? Hover { get; set; }

    /// <summary>Every position this session was asked about, so tests can assert the 1-based to 0-based conversion.</summary>
    public List<LspPosition> RequestedPositions { get; } = [];

    public bool HandlesDocument(string documentFullPath) =>
        Extensions.Contains(Path.GetExtension(documentFullPath), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> EnumerateDocuments(string scopeFullPath)
    {
        if (File.Exists(scopeFullPath))
        {
            return HandlesDocument(scopeFullPath) ? [scopeFullPath] : [];
        }

        return Directory.Exists(scopeFullPath)
            ? Directory.EnumerateFiles(scopeFullPath, "*", SearchOption.AllDirectories).Where(HandlesDocument).ToList()
            : [];
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(string documentFullPath, CancellationToken cancellationToken)
    {
        if (Diagnostics.TryGetValue(documentFullPath, out var forDocument))
        {
            return Task.FromResult(forDocument);
        }

        return Task.FromResult(Diagnostics.TryGetValue("*", out var fallback) ? fallback : []);
    }

    public Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string documentFullPath, LspPosition position, CancellationToken cancellationToken)
    {
        RequestedPositions.Add(position);
        return Task.FromResult<IReadOnlyList<LspLocation>>(Definitions);
    }

    public Task<LspHover?> GetHoverAsync(string documentFullPath, LspPosition position, CancellationToken cancellationToken) =>
        Task.FromResult(Hover);

    public Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string documentFullPath, LspPosition position, CancellationToken cancellationToken)
    {
        RequestedPositions.Add(position);
        return Task.FromResult<IReadOnlyList<LspLocation>>(References);
    }

    public Task<IReadOnlyList<LspDocumentSymbol>> GetDocumentSymbolsAsync(string documentFullPath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LspDocumentSymbol>>(DocumentSymbols);

    public Task<IReadOnlyList<LspSymbolInformation>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LspSymbolInformation>>(WorkspaceSymbols);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>A registry over fake sessions, with an optional startup failure to simulate a dead server.</summary>
public sealed class FakeLanguageServerRegistry : ILanguageServerRegistry
{
    private readonly Dictionary<string, Exception> _startupFailures = new(StringComparer.OrdinalIgnoreCase);

    public FakeLanguageServerRegistry(params ILanguageServerSession[] sessions) => Sessions = sessions;

    public IReadOnlyList<ILanguageServerSession> Sessions { get; }

    public void FailStartup(string sourceName, Exception failure) => _startupFailures[sourceName] = failure;

    public void EnsureOperational(ILanguageServerSession session)
    {
        if (_startupFailures.TryGetValue(session.SourceName, out var failure))
        {
            throw new LanguageServerException($"The {session.SourceName} language server is not running.", failure);
        }
    }
}
