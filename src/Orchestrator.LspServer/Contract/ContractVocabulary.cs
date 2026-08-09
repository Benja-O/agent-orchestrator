namespace Orchestrator.LspServer.Contract;

/// <summary>
/// The <c>status</c> values every tool response carries, as defined in docs/mcp-contract.md.
/// </summary>
/// <remarks>
/// These are plain strings on purpose. This is a wire contract read by a language model and
/// by the orchestrator's gate, so the exact spelling is part of the interface, not an
/// implementation detail of an enum serializer.
/// <para>
/// The distinction the whole contract is built around: <see cref="Ready"/> with an empty
/// item list means <em>there are no errors</em>. <see cref="Indexing"/> means <em>I do not
/// know yet</em>, and the items are not conclusive. A consumer that treats the second as the
/// first approves code that does not compile (ADR-006, ADR-010).
/// </para>
/// </remarks>
public static class IndexingStatusNames
{
    public const string Ready = "ready";
    public const string Indexing = "indexing";
}

/// <summary>
/// The <c>severity</c> values of a diagnostic, mapped from the LSP severity numbers.
/// </summary>
public static class DiagnosticSeverityNames
{
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Information = "information";
    public const string Hint = "hint";
}

/// <summary>
/// The <c>source</c> values of a diagnostic: which language server produced it.
/// </summary>
public static class DiagnosticSourceNames
{
    public const string Roslyn = "roslyn";
    public const string TypeScript = "typescript";
}
