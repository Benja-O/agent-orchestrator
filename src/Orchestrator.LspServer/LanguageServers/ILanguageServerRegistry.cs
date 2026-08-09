namespace Orchestrator.LspServer.LanguageServers;

/// <summary>The set of language servers this process owns, and whether they are usable.</summary>
/// <remarks>
/// The query layer depends on this interface and nothing else, which is what lets the whole
/// tool surface be tested against fake sessions — no process, no protocol, no indexing
/// (AI.md, golden rule 3).
/// </remarks>
public interface ILanguageServerRegistry
{
    IReadOnlyList<ILanguageServerSession> Sessions { get; }

    /// <summary>
    /// Throws when the session failed to start.
    /// </summary>
    /// <remarks>
    /// A server that never came up is infrastructure failure, and the caller has to let the
    /// exception travel. Swallowing it into an empty diagnostics list would answer "no errors"
    /// for a workspace nobody analysed.
    /// </remarks>
    void EnsureOperational(ILanguageServerSession session);
}
