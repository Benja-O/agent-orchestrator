using Microsoft.Extensions.Options;
using Orchestrator.LspServer.Configuration;
using Orchestrator.LspServer.Contract;
using Orchestrator.LspServer.LanguageServers;
using Orchestrator.LspServer.Mapping;
using Orchestrator.LspServer.Protocol;
using Orchestrator.LspServer.Workspace;

namespace Orchestrator.LspServer.Tools;

/// <summary>
/// The five operations of docs/mcp-contract.md, expressed against the session seam rather
/// than against MCP.
/// </summary>
/// <remarks>
/// Everything the contract promises that is not plumbing lives here: when an answer is
/// <c>indexing</c> instead of a verdict, how a scope becomes a list of documents, and how a
/// definition acquires a signature. Keeping it out of the MCP attribute surface is what makes
/// it testable with a fake session.
/// </remarks>
public sealed class LspQueryService
{
    private readonly ILanguageServerRegistry _registry;
    private readonly WorkspacePaths _workspacePaths;
    private readonly LspServerOptions _options;
    private readonly ILogger<LspQueryService> _logger;

    public LspQueryService(
        ILanguageServerRegistry registry,
        WorkspacePaths workspacePaths,
        IOptions<LspServerOptions> options,
        ILogger<LspQueryService> logger)
    {
        _registry = registry;
        _workspacePaths = workspacePaths;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// The verdict over a scope.
    /// </summary>
    /// <remarks>
    /// The order of the checks is the point. Readiness is decided <b>before</b> any diagnostic
    /// is collected, and a single session still indexing makes the whole answer
    /// <c>indexing</c> — never a partial verdict dressed as a clean one. An empty
    /// <c>items</c> list only ever means "no errors" when it comes with <c>status: "ready"</c>.
    /// </remarks>
    public async Task<DiagnosticsResponse> GetDiagnosticsAsync(string scope, CancellationToken cancellationToken)
    {
        var scopeFullPath = ResolveScope(scope);

        var sessionsInScope = _registry.Sessions
            .Select(session => (Session: session, Documents: session.EnumerateDocuments(scopeFullPath)))
            .Where(candidate => candidate.Documents.Count > 0)
            .ToList();

        if (sessionsInScope.Count == 0)
        {
            return DiagnosticMapper.Compose([], IndexingStatusNames.Ready, _options.MaximumDiagnosticItems);
        }

        foreach (var (session, _) in sessionsInScope)
        {
            _registry.EnsureOperational(session);

            var indexingState = session.IndexingState;
            if (!indexingState.IsReady)
            {
                _logger.LogInformation(
                    "Answering 'indexing' for scope {Scope}: {Detail}", scope, indexingState.Detail);
                return DiagnosticsResponse.StillIndexing(indexingState.Detail);
            }
        }

        var items = new List<DiagnosticItem>();

        foreach (var (session, documents) in sessionsInScope)
        {
            foreach (var documentFullPath in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var diagnostics = await session.GetDiagnosticsAsync(documentFullPath, cancellationToken)
                    .ConfigureAwait(false);

                var relativePath = _workspacePaths.ToRelativePath(documentFullPath);
                items.AddRange(diagnostics.Select(diagnostic =>
                    DiagnosticMapper.ToDiagnosticItem(relativePath, diagnostic, session.SourceName)));
            }
        }

        return DiagnosticMapper.Compose(items, IndexingStatusNames.Ready, _options.MaximumDiagnosticItems);
    }

    /// <summary>
    /// Where a symbol is defined, plus the signature read from a hover on the definition itself.
    /// </summary>
    public async Task<DefinitionResponse> GetDefinitionAsync(
        string filePath, int line, int column, CancellationToken cancellationToken)
    {
        var (session, documentFullPath, position) = ResolvePosition(filePath, line, column);

        if (!session.IndexingState.IsReady)
        {
            return DefinitionResponse.StillIndexing(session.IndexingState.Detail);
        }

        var locations = await session.GetDefinitionAsync(documentFullPath, position, cancellationToken)
            .ConfigureAwait(false);

        if (locations.Count == 0)
        {
            return DefinitionResponse.NotFound();
        }

        var target = locations[0];
        var targetFullPath = WorkspacePaths.FromUri(target.Uri);

        var hover = await SafeHoverAsync(session, targetFullPath, target.Range.Start, cancellationToken)
            .ConfigureAwait(false);
        var summary = HoverMapper.Summarize(hover);

        return new DefinitionResponse
        {
            Status = IndexingStatusNames.Ready,
            Found = true,
            FilePath = _workspacePaths.ToRelativePath(targetFullPath),
            Range = DiagnosticMapper.ToSourceRange(target.Range),
            Signature = summary.Signature,
            Documentation = summary.Documentation,
        };
    }

    public async Task<ReferencesResponse> GetReferencesAsync(
        string filePath, int line, int column, CancellationToken cancellationToken)
    {
        var (session, documentFullPath, position) = ResolvePosition(filePath, line, column);

        if (!session.IndexingState.IsReady)
        {
            return ReferencesResponse.StillIndexing(session.IndexingState.Detail);
        }

        var locations = await session.GetReferencesAsync(documentFullPath, position, cancellationToken)
            .ConfigureAwait(false);

        var previewCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var items = locations
            .Select(location =>
            {
                var referenceFullPath = WorkspacePaths.FromUri(location.Uri);
                return new ReferenceItem
                {
                    FilePath = _workspacePaths.ToRelativePath(referenceFullPath),
                    Range = DiagnosticMapper.ToSourceRange(location.Range),
                    Preview = ReadSourceLine(previewCache, referenceFullPath, location.Range.Start.Line),
                };
            })
            .OrderBy(item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Range.StartLine)
            .ToList();

        var truncated = items.Count > _options.MaximumReferenceItems
            ? items.GetRange(0, _options.MaximumReferenceItems)
            : items;

        return new ReferencesResponse
        {
            Status = IndexingStatusNames.Ready,
            Total = items.Count,
            Truncated = truncated.Count < items.Count,
            Items = truncated,
        };
    }

    public async Task<SymbolsResponse> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken)
    {
        var (session, documentFullPath) = ResolveDocument(filePath);

        if (!session.IndexingState.IsReady)
        {
            return SymbolsResponse.StillIndexing(session.IndexingState.Detail);
        }

        var symbols = await session.GetDocumentSymbolsAsync(documentFullPath, cancellationToken)
            .ConfigureAwait(false);

        return new SymbolsResponse
        {
            Status = IndexingStatusNames.Ready,
            Items = symbols.Select(SymbolMapper.ToSymbolItem).ToList(),
        };
    }

    public async Task<SymbolsResponse> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ToolInputException("workspaceSymbol needs a non-empty query.");
        }

        var items = new List<SymbolItem>();

        foreach (var session in _registry.Sessions)
        {
            _registry.EnsureOperational(session);

            if (!session.IndexingState.IsReady)
            {
                return SymbolsResponse.StillIndexing(session.IndexingState.Detail);
            }

            var symbols = await session.GetWorkspaceSymbolsAsync(query, cancellationToken).ConfigureAwait(false);

            items.AddRange(symbols
                .Where(symbol => _workspacePaths
                    .TryResolveFullPath(WorkspacePaths.FromUri(symbol.Location.Uri), out _))
                .Select(symbol => SymbolMapper.ToSymbolItem(
                    symbol,
                    _workspacePaths.ToRelativePath(WorkspacePaths.FromUri(symbol.Location.Uri)))));
        }

        return new SymbolsResponse
        {
            Status = IndexingStatusNames.Ready,
            Items = items.Take(_options.MaximumSymbolItems).ToList(),
        };
    }

    private string ResolveScope(string scope)
    {
        var requested = string.IsNullOrWhiteSpace(scope) ? "." : scope;

        if (!_workspacePaths.TryResolveFullPath(requested, out var scopeFullPath))
        {
            throw new ToolInputException($"The scope '{scope}' is outside the workspace.");
        }

        if (!File.Exists(scopeFullPath) && !Directory.Exists(scopeFullPath))
        {
            throw new ToolInputException($"The scope '{scope}' does not exist in the workspace.");
        }

        return scopeFullPath;
    }

    private (ILanguageServerSession Session, string DocumentFullPath) ResolveDocument(string filePath)
    {
        if (!_workspacePaths.TryResolveFullPath(filePath, out var documentFullPath))
        {
            throw new ToolInputException($"The path '{filePath}' is outside the workspace.");
        }

        if (!File.Exists(documentFullPath))
        {
            throw new ToolInputException($"The file '{filePath}' does not exist in the workspace.");
        }

        var session = _registry.Sessions.FirstOrDefault(candidate => candidate.HandlesDocument(documentFullPath))
            ?? throw new ToolInputException($"No language server in this workspace handles '{filePath}'.");

        _registry.EnsureOperational(session);

        return (session, documentFullPath);
    }

    private (ILanguageServerSession Session, string DocumentFullPath, LspPosition Position) ResolvePosition(
        string filePath, int line, int column)
    {
        if (line < 1 || column < 1)
        {
            throw new ToolInputException(
                $"Positions are 1-based; got line {line}, column {column}.");
        }

        var (session, documentFullPath) = ResolveDocument(filePath);

        return (session, documentFullPath, new LspPosition { Line = line - 1, Character = column - 1 });
    }

    /// <summary>
    /// A hover is a nicety, not the answer. If the server declines to produce one the
    /// definition is still valid, so a failure here degrades the response instead of failing it.
    /// </summary>
    private async Task<LspHover?> SafeHoverAsync(
        ILanguageServerSession session, string documentFullPath, LspPosition position, CancellationToken cancellationToken)
    {
        try
        {
            return await session.GetHoverAsync(documentFullPath, position, cancellationToken).ConfigureAwait(false);
        }
        catch (LanguageServerException exception)
        {
            _logger.LogDebug(exception, "No hover available at {Path}:{Line}", documentFullPath, position.Line + 1);
            return null;
        }
    }

    private static string ReadSourceLine(Dictionary<string, string[]> cache, string fullPath, int zeroBasedLine)
    {
        if (!cache.TryGetValue(fullPath, out var lines))
        {
            lines = File.Exists(fullPath) ? File.ReadAllLines(fullPath) : [];
            cache[fullPath] = lines;
        }

        return zeroBasedLine >= 0 && zeroBasedLine < lines.Length ? lines[zeroBasedLine].Trim() : string.Empty;
    }
}
