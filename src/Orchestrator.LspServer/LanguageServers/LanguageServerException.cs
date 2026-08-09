namespace Orchestrator.LspServer.LanguageServers;

/// <summary>
/// A language server could not be started, died, or did not answer in time.
/// </summary>
/// <remarks>
/// This is the line the contract draws: a file that does not compile is a <em>state</em> the
/// graph reasons about; a language server that is not there is an <em>exception</em>.
/// Answering <c>items: []</c> to a dead server would put the false green back in through the
/// side door (docs/mcp-contract.md, "Errores").
/// </remarks>
public sealed class LanguageServerException : Exception
{
    public LanguageServerException(string message)
        : base(message)
    {
    }

    public LanguageServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
