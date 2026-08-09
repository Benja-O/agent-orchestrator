namespace Orchestrator.Domain;

/// <summary>
/// Whether the gate's answer can be trusted yet.
/// </summary>
/// <remarks>
/// The distinction the entire contract is built around, restated on the consumer side:
/// <see cref="Ready"/> with no items means <em>there are no errors</em>;
/// <see cref="Indexing"/> means <em>I do not know yet</em>. A consumer that treats the second
/// as the first approves code that does not compile (ADR-006, ADR-010).
/// </remarks>
public enum GateStatus
{
    Ready = 0,
    Indexing = 1,
}

/// <summary>One answer from the gate, exactly as the MCP contract shapes it.</summary>
public sealed record GateVerdict
{
    public required GateStatus Status { get; init; }

    public required DiagnosticSet Diagnostics { get; init; }

    /// <summary>Only set while <see cref="Status"/> is <see cref="GateStatus.Indexing"/>: what is being waited on.</summary>
    public string? StatusDetail { get; init; }

    public static GateVerdict Ready(DiagnosticSet diagnostics) => new()
    {
        Status = GateStatus.Ready,
        Diagnostics = diagnostics,
    };

    /// <summary>
    /// An answer that is not an answer. Note that it can carry items: a partial list during
    /// indexing is not an approval and not a rejection, it is nothing at all.
    /// </summary>
    public static GateVerdict Indexing(string statusDetail, DiagnosticSet? partial = null) => new()
    {
        Status = GateStatus.Indexing,
        Diagnostics = partial ?? DiagnosticSet.Empty,
        StatusDetail = statusDetail,
    };
}

/// <summary>
/// The graph's window into the language servers. Implemented by <c>Orchestrator.Lsp</c> over
/// the MCP contract, and by <c>FakeLanguageServer</c> in the suite.
/// </summary>
/// <remarks>
/// Deliberately one method wide. The four navigation tools of the contract exist for the
/// layer agents during their turn (ADR-005), not for the graph: the graph only ever wants a
/// verdict. Widening this interface would be the first sign that the orchestrator started
/// doing the agent's job.
/// </remarks>
public interface ILanguageServerGateway
{
    /// <param name="scope">A path relative to the workspace root: a file, a directory, or <c>.</c> for everything.</param>
    Task<GateVerdict> GetDiagnosticsAsync(string scope, CancellationToken cancellationToken);
}
