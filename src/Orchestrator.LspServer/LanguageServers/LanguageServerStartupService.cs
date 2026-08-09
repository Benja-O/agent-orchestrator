namespace Orchestrator.LspServer.LanguageServers;

/// <summary>
/// Starts the language servers once the HTTP endpoint is already listening.
/// </summary>
/// <remarks>
/// Not awaited during host startup on purpose: loading a solution takes seconds, and the
/// honest answer during those seconds is <c>status: "indexing"</c>, which requires the server
/// to be reachable in order to say it.
/// </remarks>
public sealed class LanguageServerStartupService : BackgroundService
{
    private readonly LanguageServerRegistry _registry;

    public LanguageServerStartupService(LanguageServerRegistry registry) => _registry = registry;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _registry.StartAllAsync(stoppingToken);
}
