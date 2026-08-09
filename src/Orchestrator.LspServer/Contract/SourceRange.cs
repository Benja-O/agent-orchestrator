namespace Orchestrator.LspServer.Contract;

/// <summary>
/// A span inside a source file, <b>1-based on both axes</b>.
/// </summary>
/// <remarks>
/// The LSP protocol numbers lines and columns from zero; compiler messages, editors and
/// people count from one. The conversion happens here, in the server, so that neither the
/// agent nor the orchestrator has to remember to add one — that is the kind of off-by-one
/// that makes an agent edit the wrong line and never notice.
/// </remarks>
public sealed record SourceRange
{
    public required int StartLine { get; init; }

    public required int StartColumn { get; init; }

    public required int EndLine { get; init; }

    public required int EndColumn { get; init; }
}
