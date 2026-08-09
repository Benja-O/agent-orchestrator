namespace Orchestrator.LspServer.Contract;

/// <summary>
/// Where a symbol is defined and, above all, with what signature.
/// </summary>
/// <remarks>
/// <see cref="Signature"/> is what makes this tool worth building: it lets the API agent
/// <em>ask</em> what shape a domain method has instead of assuming it. Without it the agent
/// would have to open the whole file and infer (ADR-004, function 2).
/// </remarks>
public sealed record DefinitionResponse
{
    public required string Status { get; init; }

    public required bool Found { get; init; }

    public string? FilePath { get; init; }

    public SourceRange? Range { get; init; }

    public string? Signature { get; init; }

    public string? Documentation { get; init; }

    public string? StatusDetail { get; init; }

    public static DefinitionResponse StillIndexing(string statusDetail) => new()
    {
        Status = IndexingStatusNames.Indexing,
        Found = false,
        StatusDetail = statusDetail,
    };

    public static DefinitionResponse NotFound() => new()
    {
        Status = IndexingStatusNames.Ready,
        Found = false,
    };
}

/// <summary>One place where a symbol is used, with the source line so the agent can triage without opening the file.</summary>
public sealed record ReferenceItem
{
    public required string FilePath { get; init; }

    public required SourceRange Range { get; init; }

    /// <summary>The text of the line where the reference occurs, trimmed.</summary>
    public required string Preview { get; init; }
}

public sealed record ReferencesResponse
{
    public required string Status { get; init; }

    public required int Total { get; init; }

    public required bool Truncated { get; init; }

    public required IReadOnlyList<ReferenceItem> Items { get; init; }

    public string? StatusDetail { get; init; }

    public static ReferencesResponse StillIndexing(string statusDetail) => new()
    {
        Status = IndexingStatusNames.Indexing,
        Total = 0,
        Truncated = false,
        Items = [],
        StatusDetail = statusDetail,
    };
}

/// <summary>An entry of a file outline or of a workspace-wide symbol search.</summary>
public sealed record SymbolItem
{
    public required string Name { get; init; }

    /// <summary>The LSP symbol kind, lowercased: <c>class</c>, <c>method</c>, <c>property</c>…</summary>
    public required string Kind { get; init; }

    public required SourceRange Range { get; init; }

    /// <summary>Set when the language server offers one; the outline of a file is much less useful without it.</summary>
    public string? Signature { get; init; }

    /// <summary>Only populated by <c>workspaceSymbol</c>, where the file is not implied by the request.</summary>
    public string? FilePath { get; init; }

    /// <summary>Nested members. Always empty for <c>workspaceSymbol</c>, which returns a flat list.</summary>
    public IReadOnlyList<SymbolItem> Children { get; init; } = [];
}

public sealed record SymbolsResponse
{
    public required string Status { get; init; }

    public required IReadOnlyList<SymbolItem> Items { get; init; }

    public string? StatusDetail { get; init; }

    public static SymbolsResponse StillIndexing(string statusDetail) => new()
    {
        Status = IndexingStatusNames.Indexing,
        Items = [],
        StatusDetail = statusDetail,
    };
}
