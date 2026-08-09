using System.Collections.Concurrent;

namespace Orchestrator.LspServer.LanguageServers;

/// <summary>
/// Owns the language server sessions for the lifetime of the process and starts them in the
/// background.
/// </summary>
/// <remarks>
/// Startup is deliberately not awaited by the HTTP endpoint. A language server takes seconds
/// to load a solution, and blocking the first tool call for that long invites a timeout on
/// the client side; answering <c>indexing</c> immediately is both faster and truer.
/// </remarks>
public sealed class LanguageServerRegistry : ILanguageServerRegistry, IAsyncDisposable
{
    private readonly IReadOnlyList<ILanguageServerSession> _sessions;
    private readonly ConcurrentDictionary<string, Exception> _startupFailures = new();
    private readonly ILogger<LanguageServerRegistry> _logger;

    public LanguageServerRegistry(IEnumerable<ILanguageServerSession> sessions, ILogger<LanguageServerRegistry> logger)
    {
        _sessions = sessions.ToList();
        _logger = logger;
    }

    public IReadOnlyList<ILanguageServerSession> Sessions => _sessions;

    public async Task StartAllAsync(CancellationToken cancellationToken)
    {
        foreach (var session in _sessions)
        {
            try
            {
                await session.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _startupFailures[session.SourceName] = exception;
                _logger.LogError(exception, "The {SourceName} language server failed to start.", session.SourceName);
            }
        }
    }

    public void EnsureOperational(ILanguageServerSession session)
    {
        if (_startupFailures.TryGetValue(session.SourceName, out var failure))
        {
            throw new LanguageServerException(
                $"The {session.SourceName} language server is not running: {failure.Message}", failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
