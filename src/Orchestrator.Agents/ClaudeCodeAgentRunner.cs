using Orchestrator.Domain;

namespace Orchestrator.Agents;

/// <summary>
/// <see cref="IAgentRunner"/> over the Claude Code CLI in headless mode.
/// </summary>
/// <remarks>
/// <para>
/// The whole reason this class can be thin: a layer agent does not report to the graph, it
/// writes files, and the gate is what speaks (ADR-014). So there is no protocol to invent here —
/// build the invocation, run it, say how it ended.
/// </para>
/// <para>
/// <strong>The CLI, never the API</strong> (ADR-001). Usage has to run against the subscription,
/// which is also why nothing in this file reads an environment variable holding a key.
/// </para>
/// </remarks>
public sealed class ClaudeCodeAgentRunner : IAgentRunner
{
    private readonly IAgentProcessRunner _processRunner;
    private readonly ClaudeCodeSettings _settings;
    private readonly LayerMap _layerMap;

    public ClaudeCodeAgentRunner(
        IAgentProcessRunner processRunner,
        ClaudeCodeSettings settings,
        LayerMap? layerMap = null)
    {
        _processRunner = processRunner;
        _settings = settings;
        _layerMap = layerMap ?? LayerMap.Default;
    }

    /// <summary>The wiring a real run uses: a real process runner, with the settings' timeout.</summary>
    public static ClaudeCodeAgentRunner Create(
        ClaudeCodeSettings settings,
        TimeProvider timeProvider,
        LayerMap? layerMap = null) =>
        new(new SystemAgentProcessRunner(settings.Timeout, timeProvider), settings, layerMap);

    public async Task<AgentOutcome> RunAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        var request = ClaudeCodeCommandLine.For(invocation, _settings, _layerMap);
        var processResult = await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);

        return ClaudeCodeOutcomeTranslation.FromProcessResult(processResult, invocation.AgentName);
    }
}
