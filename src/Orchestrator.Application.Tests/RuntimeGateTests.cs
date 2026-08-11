using Orchestrator.Domain;
using Orchestrator.TestSupport;

namespace Orchestrator.Application.Tests;

/// <summary>
/// The node that asks whether the generated application runs, not whether it compiles.
/// </summary>
/// <remarks>
/// <para>
/// It exists because block 5's first full run produced an application that passed three compile
/// gates — clean diagnostics, zero errors from <c>dotnet build</c>, zero from <c>tsc</c> — and
/// returned 500 on its first request. The API agent had written valid C# around a false belief
/// about EF Core, and there is no diagnostic for a false belief (ADR-017, ROADMAP R4).
/// </para>
/// <para>
/// Every test here runs against fakes and starts nothing. The real verifier runs <c>dotnet
/// run</c> and waits minutes; the graph's behaviour around it must be provable in milliseconds,
/// or it stops being provable at all (golden rule 3).
/// </para>
/// </remarks>
public sealed class RuntimeGateTests
{
    private const string EfCoreMappingFailure =
        "`GET /api/tareas` devolvió 500 InternalServerError. La app compila pero falla al atender la request: "
        + "The 'HashSet<TareaId>' property 'Tarea._dependencias' could not be mapped.";

    [Fact]
    public async Task An_application_that_compiles_and_runs_lets_the_pipeline_finish()
    {
        var scenario = new GraphScenario().WithEveryLayerClean().WithRuntimeGate();

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.Completed, state.Termination!.Reason);
        Assert.Equal(1, scenario.ApplicationVerifier!.Invocations);
    }

    /// <summary>
    /// The case the whole feature was built for: everything compiles and the application does not
    /// start, so the work goes back to the API agent.
    /// </summary>
    [Fact]
    public async Task An_application_that_compiles_and_does_not_run_goes_back_to_the_api_agent()
    {
        var scenario = new GraphScenario().WithRuntimeGate();

        scenario.Agents
            .Script(Layer.Domain, AgentTurn.Fixes(Layer.Domain))
            .Script(Layer.Api, AgentTurn.BreaksAtRuntime(EfCoreMappingFailure), AgentTurn.FixesRuntime())
            .Script(Layer.Frontend, AgentTurn.Fixes(Layer.Frontend));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.Completed, state.Termination!.Reason);
        Assert.Equal(["spec-analyzer", "domain", "api", "api", "frontend"], scenario.InvokedAgents);
    }

    /// <summary>
    /// And it is handed the reason, not asked to guess.
    /// </summary>
    /// <remarks>
    /// The point of expressing a runtime failure as a <see cref="Diagnostic"/>: the review prompt
    /// carries it without knowing it came from an HTTP response rather than from a compiler.
    /// </remarks>
    [Fact]
    public async Task The_api_agent_is_handed_the_runtime_failure_as_input()
    {
        var scenario = new GraphScenario().WithRuntimeGate();

        scenario.Agents
            .Script(Layer.Domain, AgentTurn.Fixes(Layer.Domain))
            .Script(Layer.Api, AgentTurn.BreaksAtRuntime(EfCoreMappingFailure), AgentTurn.FixesRuntime())
            .Script(Layer.Frontend, AgentTurn.Fixes(Layer.Frontend));

        await scenario.RunAsync();

        var secondTurn = scenario.InvocationsOf(Layer.Api)[1];

        Assert.Single(secondTurn.Diagnostics);
        Assert.Contains("HashSet<TareaId>", secondTurn.Prompt, StringComparison.Ordinal);
        Assert.Contains(RuntimeDiagnostics.Code, secondTurn.Prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The frontend's turn is never paid for while the application does not run.
    /// </summary>
    /// <remarks>
    /// Why the node sits after the API stage rather than at the end of the pipeline. Both would
    /// catch the failure; only this one catches it before spending a turn on a layer that would
    /// have to be redone anyway (ADR-001).
    /// </remarks>
    [Fact]
    public async Task The_frontend_does_not_run_while_the_application_is_broken()
    {
        var scenario = new GraphScenario { Policy = new GraphPolicy { MaximumAttemptsPerNode = 2, IndexWaitDelay = TimeSpan.Zero } }
            .WithRuntimeGate();

        scenario.Agents
            .Script(Layer.Domain, AgentTurn.Fixes(Layer.Domain))
            .Script(Layer.Api, AgentTurn.BreaksAtRuntime(EfCoreMappingFailure))
            .Script(Layer.Frontend, AgentTurn.Fixes(Layer.Frontend));

        var state = await scenario.RunAsync();

        Assert.NotEqual(TerminationReason.Completed, state.Termination!.Reason);
        Assert.DoesNotContain("frontend", scenario.InvokedAgents);
    }

    /// <summary>
    /// The same startup failure twice is non-progress, exactly as it is for a compile error.
    /// </summary>
    /// <remarks>
    /// This is the dividend of expressing the result as a <see cref="DiagnosticSet"/>: the
    /// fingerprint comparison that <see cref="ReviewPolicy"/> already did needed no changes to
    /// cover a kind of failure it was not written for.
    /// </remarks>
    [Fact]
    public async Task The_same_startup_failure_twice_stops_the_run_for_non_progress()
    {
        var scenario = new GraphScenario().WithRuntimeGate();

        scenario.Agents
            .Script(Layer.Domain, AgentTurn.Fixes(Layer.Domain))
            .Script(Layer.Api, AgentTurn.BreaksAtRuntime(EfCoreMappingFailure), AgentTurn.ChangesNothing())
            .Script(Layer.Frontend, AgentTurn.Fixes(Layer.Frontend));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.NoProgress, state.Termination!.Reason);
    }

    /// <summary>
    /// An application that exposes nothing to call fails; it is never approved.
    /// </summary>
    /// <remarks>
    /// The door this gate could have opened and did not. A verifier that reported success because
    /// it found no endpoints would be a false green produced by the very check installed to
    /// prevent one — and it would be invisible, because a clean verdict is what a working
    /// application also produces.
    /// </remarks>
    [Fact]
    public async Task An_application_with_no_endpoints_to_exercise_is_a_failure_not_a_pass()
    {
        var scenario = new GraphScenario { Policy = new GraphPolicy { MaximumAttemptsPerNode = 1, IndexWaitDelay = TimeSpan.Zero } }
            .WithEveryLayerClean()
            .WithRuntimeGate();

        scenario.Workspace.DiscoverableRoutes = 0;

        var state = await scenario.RunAsync();

        Assert.NotEqual(TerminationReason.Completed, state.Termination!.Reason);
    }

    /// <summary>
    /// The gate does not run over code that does not compile.
    /// </summary>
    /// <remarks>
    /// Starting an application that failed to build costs a minute and tells you what the
    /// compile gate already said.
    /// </remarks>
    [Fact]
    public async Task The_application_is_not_started_while_the_api_layer_does_not_compile()
    {
        var scenario = new GraphScenario { Policy = new GraphPolicy { MaximumAttemptsPerNode = 1, IndexWaitDelay = TimeSpan.Zero } }
            .WithRuntimeGate();

        scenario.Agents
            .Script(Layer.Domain, AgentTurn.Fixes(Layer.Domain))
            .Script(Layer.Api, AgentTurn.Breaks(Layer.Api, Diagnostics.MissingMember("src/Api/TareasController.cs", "Cerrar")))
            .Script(Layer.Frontend, AgentTurn.Fixes(Layer.Frontend));

        await scenario.RunAsync();

        Assert.Equal(0, scenario.ApplicationVerifier!.Invocations);
    }

    /// <summary>The run's trace shows the node, so the log explains a failure nobody expected.</summary>
    [Fact]
    public async Task The_runtime_check_is_visible_in_the_log()
    {
        var scenario = new GraphScenario().WithEveryLayerClean().WithRuntimeGate();

        await scenario.RunAsync();

        var verified = Assert.Single(scenario.Observer.Events.OfType<ApplicationVerified>());

        Assert.Equal(0, verified.ErrorCount);
        Assert.True(verified.RoutesExercised > 0, "a clean verdict has to say how many endpoints it actually called.");
        Assert.Contains(NodeId.ApiRuntime.Value, scenario.Observer.Events.OfType<NodeEntered>().Select(entered => entered.Node.Value));
    }

    /// <summary>Without a verifier the graph is gated on compilation alone, and never pretends otherwise.</summary>
    [Fact]
    public async Task Without_a_verifier_the_node_does_not_exist()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();

        await scenario.RunAsync();

        Assert.Empty(scenario.Observer.Events.OfType<ApplicationVerified>());
    }
}
