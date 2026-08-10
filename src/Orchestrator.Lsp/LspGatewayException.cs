namespace Orchestrator.Lsp;

/// <summary>
/// The LSP layer failed as infrastructure: the server did not start, did not answer, or
/// answered something this adapter cannot interpret.
/// </summary>
/// <remarks>
/// Deliberately an exception and not a <c>Result</c>, following the split AI.md draws: a file
/// that does not compile is a <em>state</em> the graph reasons about, a language server that is
/// down is an <em>exception</em>. The reason matters more here than anywhere else in the
/// system — answering "no diagnostics" when the server is unreachable would reintroduce the
/// false green through the back door (ADR-010).
/// </remarks>
public sealed class LspGatewayException : Exception
{
    public LspGatewayException(string message) : base(message)
    {
    }

    public LspGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
