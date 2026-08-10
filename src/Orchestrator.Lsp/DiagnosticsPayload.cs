using System.Text.Json.Serialization;

namespace Orchestrator.Lsp;

/// <summary>
/// The wire shape of a <c>diagnostics</c> response, exactly as docs/mcp-contract.md defines it.
/// </summary>
/// <remarks>
/// This is the adapter's own twin of the server's <c>DiagnosticsResponse</c>, and it is
/// duplicated rather than shared on purpose: <c>Orchestrator.LspServer</c> is agnostic of the
/// project it analyses and does not reference <c>Orchestrator.Domain</c> (ADR-013). Referencing
/// it from here to save a record would tie the orchestrator to the server's assembly and turn a
/// documented contract into an implementation detail shared by accident.
/// </remarks>
internal sealed record DiagnosticsPayload
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("statusDetail")]
    public string? StatusDetail { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<DiagnosticItemPayload> Items { get; init; } = [];
}

internal sealed record DiagnosticItemPayload
{
    [JsonPropertyName("filePath")]
    public string? FilePath { get; init; }

    [JsonPropertyName("range")]
    public SourceRangePayload? Range { get; init; }

    [JsonPropertyName("severity")]
    public string? Severity { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

/// <summary>1-based, because the contract converts from LSP's 0-based on the server side.</summary>
internal sealed record SourceRangePayload
{
    [JsonPropertyName("startLine")]
    public int StartLine { get; init; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; init; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; init; }

    [JsonPropertyName("endColumn")]
    public int EndColumn { get; init; }
}
