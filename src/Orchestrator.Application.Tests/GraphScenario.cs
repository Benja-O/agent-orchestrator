using Orchestrator.Application.Graph;
using Orchestrator.Domain;
using Orchestrator.TestSupport;

namespace Orchestrator.Application.Tests;

/// <summary>
/// One run of the whole pipeline, wired entirely out of fakes.
/// </summary>
/// <remarks>
/// Golden rule 3 of AI.md made concrete: no process, no network, no <c>claude</c> on the PATH,
/// no language server, no waiting. The agent and the gate share one
/// <see cref="TestSupport.FakeWorkspace"/>, so a run described here is a run that could
/// actually happen.
/// </remarks>
internal sealed class GraphScenario
{
    public GraphScenario()
    {
        Agents = new FakeAgentRunner(Workspace).ScriptSpecAnalyzer(Fixture.SpecAnalyzerAnswer("valid-plan.md"));
        Gate = new FakeLanguageServer(Workspace);
    }

    public FakeWorkspace Workspace { get; } = new();

    public FakeAgentRunner Agents { get; }

    public FakeLanguageServer Gate { get; }

    public RecordingRunObserver Observer { get; } = new();

    public GraphPolicy Policy { get; set; } = new() { IndexWaitDelay = TimeSpan.Zero };

    public SpecDocument Spec { get; set; } = Fixture.RealSpec;

    /// <summary>
    /// The runtime gate, off unless a test asks for it.
    /// </summary>
    /// <remarks>
    /// Off by default so that every test written before ADR-017 keeps describing what it was
    /// written to describe: a pipeline gated on compilation. The runtime gate has its own tests,
    /// and mixing it into all of them would make each one answer two questions at once.
    /// </remarks>
    public FakeApplicationVerifier? ApplicationVerifier { get; private set; }

    /// <summary>Turns on the node that asks whether the generated application actually runs.</summary>
    public GraphScenario WithRuntimeGate()
    {
        ApplicationVerifier = new FakeApplicationVerifier(Workspace);
        return this;
    }

    /// <summary>Scripts every layer agent to produce code that compiles on the first pass.</summary>
    public GraphScenario WithEveryLayerClean()
    {
        Agents.ScriptEveryLayerClean();
        return this;
    }

    public Task<GraphState> RunAsync() =>
        new GraphRunner(Agents, Gate, Observer, new SteppingTimeProvider(), Policy, layerMap: null, ApplicationVerifier)
            .RunAsync(Spec, CancellationToken.None);

    /// <summary>The agents the graph invoked, in order. The shape of the run in one list.</summary>
    public IReadOnlyList<string> InvokedAgents => Agents.Invocations.Select(invocation => invocation.AgentName).ToList();

    public IReadOnlyList<AgentInvocation> InvocationsOf(Layer layer) =>
        Agents.Invocations.Where(invocation => invocation.AgentName == LayerCatalog.AgentNameOf(layer)).ToList();
}
