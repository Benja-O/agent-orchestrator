using Orchestrator.LspServer.Protocol;

namespace Orchestrator.LspServer.LanguageServers;

/// <summary>Whether a session can be trusted yet, and if not, what it is waiting for.</summary>
/// <param name="IsReady">
/// False means the answer to any query is <em>I do not know yet</em>. The tools turn this
/// into <c>status: "indexing"</c> and never into an empty-but-clean verdict.
/// </param>
/// <param name="Detail">Human-readable reason, surfaced as <c>statusDetail</c> so a stalled index is diagnosable.</param>
public readonly record struct IndexingState(bool IsReady, string Detail)
{
    public static IndexingState Ready { get; } = new(true, "ready");
}

/// <summary>
/// One live connection to one language server.
/// </summary>
/// <remarks>
/// This interface is the seam that keeps golden rule 3 of AI.md true for this project: the
/// tools depend on it, so the whole tool surface can be tested against a fake session with no
/// process, no protocol and no indexing anywhere in the suite.
/// <para>Everything it returns is in protocol shape — 0-based, untranslated. The mapping to
/// the contract happens above it.</para>
/// </remarks>
public interface ILanguageServerSession : IAsyncDisposable
{
    /// <summary>The <c>source</c> value the contract stamps on diagnostics from this server.</summary>
    string SourceName { get; }

    IndexingState IndexingState { get; }

    /// <summary>Whether this server is the one that owns a given file, decided by extension.</summary>
    bool HandlesDocument(string documentFullPath);

    /// <summary>Every file inside <paramref name="scopeFullPath"/> that this server owns.</summary>
    IReadOnlyList<string> EnumerateDocuments(string scopeFullPath);

    Task StartAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(string documentFullPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string documentFullPath, LspPosition position, CancellationToken cancellationToken);

    Task<LspHover?> GetHoverAsync(string documentFullPath, LspPosition position, CancellationToken cancellationToken);

    Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string documentFullPath, LspPosition position, CancellationToken cancellationToken);

    Task<IReadOnlyList<LspDocumentSymbol>> GetDocumentSymbolsAsync(string documentFullPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<LspSymbolInformation>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken);
}
