namespace Orchestrator.LspServer.Tools;

/// <summary>
/// The caller asked something that cannot be answered: a path that does not exist, a path
/// outside the workspace, a position below line 1, a file no language server owns.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="LanguageServers.LanguageServerException"/> on purpose. Both end
/// up as an MCP error rather than an empty result, but they mean different things to whoever
/// reads the log: this one is the caller's fault, the other one is the infrastructure's.
/// </remarks>
public sealed class ToolInputException : Exception
{
    public ToolInputException(string message)
        : base(message)
    {
    }
}
