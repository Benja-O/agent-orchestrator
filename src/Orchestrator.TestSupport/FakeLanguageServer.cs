using Orchestrator.Domain;

namespace Orchestrator.TestSupport;

/// <summary>
/// A gate that reports what is in a <see cref="FakeWorkspace"/>, including the answer that is
/// not an answer.
/// </summary>
/// <remarks>
/// The reason this exists rather than a stub returning canned verdicts: the most important
/// behaviour to test on the consumer side is that <c>indexing</c> is never read as approval,
/// and that only means something if the fake can answer <c>indexing</c> <em>while the
/// workspace is broken</em> — which is exactly the shape of a false green (ADR-010).
/// </remarks>
public sealed class FakeLanguageServer : ILanguageServerGateway
{
    private readonly FakeWorkspace _workspace;
    private readonly LayerMap _layerMap;

    public FakeLanguageServer(FakeWorkspace workspace, LayerMap? layerMap = null)
    {
        _workspace = workspace;
        _layerMap = layerMap ?? LayerMap.Default;
    }

    /// <summary>Every scope this gate was asked about, in order.</summary>
    public List<string> QueriedScopes { get; } = [];

    public Task<GateVerdict> GetDiagnosticsAsync(string scope, CancellationToken cancellationToken)
    {
        QueriedScopes.Add(scope);

        var items = InScope(scope).ToList();

        if (_workspace.IndexingAnswersRemaining > 0)
        {
            _workspace.IndexingAnswersRemaining--;

            // By default an indexing answer carries nothing, which is what a real language
            // server does while it loads: an empty list over a workspace that is actually
            // broken. That pairing — no items, and it means nothing — is the false green in
            // its purest form, so it is the default the consumer gets tested against.
            var partial = _workspace.IndexingReportsPartialItems
                ? new DiagnosticSet { Items = items, Total = items.Count, Truncated = true }
                : DiagnosticSet.Empty;

            return Task.FromResult(GateVerdict.Indexing(_workspace.IndexingDetail, partial));
        }

        return Task.FromResult(GateVerdict.Ready(new DiagnosticSet
        {
            Items = items,
            Total = _workspace.TotalOverride ?? items.Count,
            Truncated = _workspace.ReportTruncated,
        }));
    }

    private IEnumerable<Diagnostic> InScope(string scope)
    {
        var all = _workspace.AllDiagnostics();

        if (scope is "." or "")
        {
            return all;
        }

        var prefix = scope.Replace('\\', '/').TrimEnd('/') + "/";
        return all.Where(item => item.FilePath.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The layer this gate would attribute a path to, so tests can assert routing without the runner.</summary>
    public Layer? LayerOf(string relativeFilePath) => _layerMap.TryResolve(relativeFilePath, out var layer) ? layer : null;
}
