namespace Orchestrator.LspServer.Contract;

/// <summary>
/// One diagnostic as the gate and the layer agents see it: structured, 1-based, with the
/// file path relative to the workspace root.
/// </summary>
/// <remarks>
/// There is deliberately no <c>layer</c> field. Mapping a path to a layer is the
/// orchestrator's concern — it is its domain model that knows <c>src/Domain/**</c> belongs
/// to the domain agent. Keeping it out leaves this server agnostic of the project it is
/// analysing (ADR-010).
/// </remarks>
public sealed record DiagnosticItem
{
    public required string FilePath { get; init; }

    public required SourceRange Range { get; init; }

    /// <summary>One of <see cref="DiagnosticSeverityNames"/>.</summary>
    public required string Severity { get; init; }

    /// <summary>The compiler error code, such as <c>CS1061</c>. Empty when the server gave none.</summary>
    public required string Code { get; init; }

    public required string Message { get; init; }

    /// <summary>One of <see cref="DiagnosticSourceNames"/>.</summary>
    public required string Source { get; init; }
}

/// <summary>
/// The verdict. The only tool both consumers use: the layer agents while they write code,
/// the orchestrator between graph nodes to decide the transition.
/// </summary>
public sealed record DiagnosticsResponse
{
    /// <summary>One of <see cref="IndexingStatusNames"/>. Never approve on <c>indexing</c>.</summary>
    public required string Status { get; init; }

    /// <summary>How many diagnostics exist in the scope, before truncation.</summary>
    public required int Total { get; init; }

    /// <summary>True when <see cref="Items"/> holds fewer entries than <see cref="Total"/>.</summary>
    public required bool Truncated { get; init; }

    public required IReadOnlyList<DiagnosticItem> Items { get; init; }

    /// <summary>
    /// Only set when <see cref="Status"/> is <c>indexing</c>: what the server is still
    /// waiting for. Present so that a stalled index is diagnosable instead of silent.
    /// </summary>
    public string? StatusDetail { get; init; }

    public static DiagnosticsResponse StillIndexing(string statusDetail) => new()
    {
        Status = IndexingStatusNames.Indexing,
        Total = 0,
        Truncated = false,
        Items = [],
        StatusDetail = statusDetail,
    };
}
