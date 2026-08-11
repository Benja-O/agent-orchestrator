using Orchestrator.Domain;

namespace Orchestrator.TestSupport;

/// <summary>
/// One scripted agent turn: what it answers, and what it does to the workspace.
/// </summary>
/// <remarks>
/// The effect is the important half. A turn that only produces text cannot make a review loop
/// converge honestly, because nothing the gate reads has changed.
/// </remarks>
public sealed record AgentTurn
{
    public required AgentOutcome Outcome { get; init; }

    /// <summary>What the agent did to the code. Null means it wrote nothing that mattered.</summary>
    public Action<FakeWorkspace>? Effect { get; init; }

    /// <summary>The agent fixed its layer.</summary>
    public static AgentTurn Fixes(Layer layer) => new()
    {
        Outcome = AgentOutcome.Completed($"Listo, corregí la capa {LayerCatalog.PlanNameOf(layer)}."),
        Effect = workspace => workspace.Repair(layer),
    };

    /// <summary>The agent wrote code that does not compile.</summary>
    public static AgentTurn Breaks(Layer layer, params Diagnostic[] diagnostics) => new()
    {
        Outcome = AgentOutcome.Completed("Listo, implementé lo pedido."),
        Effect = workspace => workspace.Replace(layer, diagnostics),
    };

    /// <summary>
    /// The agent wrote code that compiles and does not run.
    /// </summary>
    /// <remarks>
    /// The turn that models what block 5 actually produced: three clean gates and a 500 on the
    /// first request, from an agent that had written valid C# around a false belief about EF Core.
    /// No compile diagnostic exists for that, which is exactly why the runtime gate does.
    /// </remarks>
    public static AgentTurn BreaksAtRuntime(string reason) => new()
    {
        Outcome = AgentOutcome.Completed("Listo, implementé lo pedido."),
        Effect = workspace => workspace.BreakAtRuntime(reason),
    };

    /// <summary>The agent fixed the startup failure.</summary>
    public static AgentTurn FixesRuntime() => new()
    {
        Outcome = AgentOutcome.Completed("Listo, corregí la configuración."),
        Effect = workspace => workspace.RepairRuntime(),
    };

    /// <summary>
    /// The agent claimed success and changed nothing. The literal definition of non-progress,
    /// and the failure mode this whole project exists to catch (ADR-004).
    /// </summary>
    public static AgentTurn ChangesNothing() => new()
    {
        Outcome = AgentOutcome.Completed("Listo, ya estaba todo bien."),
    };

    /// <summary>An arbitrary effect, for scenarios the named factories do not cover.</summary>
    public static AgentTurn Does(Action<FakeWorkspace> effect, string transcript = "Listo.") => new()
    {
        Outcome = AgentOutcome.Completed(transcript),
        Effect = effect,
    };

    /// <summary>The agent answered with text, and wrote nothing. What the spec analyzer does.</summary>
    public static AgentTurn Answers(string transcript) => new()
    {
        Outcome = AgentOutcome.Completed(transcript),
    };

    /// <summary>The invocation itself did not work out.</summary>
    public static AgentTurn Fails(AgentCompletion completion, string failureDetail) => new()
    {
        Outcome = AgentOutcome.Failed(completion, failureDetail),
    };
}

/// <summary>
/// An agent runner that never launches anything.
/// </summary>
/// <remarks>
/// <para>
/// Golden rule 3 of AI.md, and the reason it exists: the five-hour window of the Pro plan runs
/// out exactly while debugging the state machine (ADR-001). The graph is iterated against this
/// class a hundred times a day instead.
/// </para>
/// <para>
/// The design problem this class had and the LSP fakes did not: an agent's answer is free-form
/// text, so there is no protocol shape to serve back. The way out was to notice that
/// <strong>the graph never reads that text</strong> for a layer agent — it reads the gate. So
/// what gets scripted here is not prose, it is an <see cref="AgentTurn.Effect"/> on the shared
/// <see cref="FakeWorkspace"/>. Prose is scripted at exactly one node, the spec analyzer, whose
/// output really is text; those answers are recorded as fixture files (ADR-014).
/// </para>
/// </remarks>
public sealed class FakeAgentRunner : IAgentRunner
{
    private readonly FakeWorkspace _workspace;
    private readonly Dictionary<string, Queue<AgentTurn>> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AgentTurn> _lastTurns = new(StringComparer.OrdinalIgnoreCase);

    public FakeAgentRunner(FakeWorkspace workspace) => _workspace = workspace;

    /// <summary>Every invocation the graph made, in order, so a test can assert what an agent was handed.</summary>
    public List<AgentInvocation> Invocations { get; } = [];

    /// <summary>
    /// Scripts the turns of one agent.
    /// </summary>
    /// <remarks>
    /// When the script runs out the last turn repeats. That is what makes "an agent that never
    /// makes progress" expressible without writing the same line ten times, and it is why the
    /// iteration ceilings have something to stop.
    /// </remarks>
    public FakeAgentRunner Script(string agentName, params AgentTurn[] turns)
    {
        _scripts[agentName] = new Queue<AgentTurn>(turns);
        return this;
    }

    public FakeAgentRunner Script(Layer layer, params AgentTurn[] turns) =>
        Script(LayerCatalog.AgentNameOf(layer), turns);

    /// <summary>Scripts the spec analyzer to answer with one recorded plan, every time.</summary>
    public FakeAgentRunner ScriptSpecAnalyzer(params string[] answers) =>
        Script("spec-analyzer", answers.Select(AgentTurn.Answers).ToArray());

    /// <summary>Scripts every layer agent to write code that compiles on the first pass.</summary>
    public FakeAgentRunner ScriptEveryLayerClean()
    {
        foreach (var layer in LayerCatalog.InPipelineOrder)
        {
            Script(layer, AgentTurn.Fixes(layer));
        }

        return this;
    }

    public Task<AgentOutcome> RunAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Invocations.Add(invocation);

        var turn = NextTurn(invocation.AgentName);
        turn.Effect?.Invoke(_workspace);

        return Task.FromResult(turn.Outcome);
    }

    private AgentTurn NextTurn(string agentName)
    {
        if (_scripts.TryGetValue(agentName, out var queue) && queue.Count > 0)
        {
            var turn = queue.Dequeue();
            _lastTurns[agentName] = turn;
            return turn;
        }

        if (_lastTurns.TryGetValue(agentName, out var lastTurn))
        {
            return lastTurn;
        }

        throw new InvalidOperationException(
            $"The graph invoked the agent '{agentName}', which this FakeAgentRunner has no script for. "
            + "Either the test forgot to script it, or the graph is calling an agent it should not.");
    }
}
