using Orchestrator.Domain;
using Orchestrator.TestSupport;

namespace Orchestrator.Application.Tests;

/// <summary>
/// The graph running end to end, and the three ways it is allowed to stop.
/// </summary>
public sealed class GraphRunnerTests
{
    private static Diagnostic DomainError(string message = "the type 'Estado' could not be found") =>
        Diagnostics.Error("src/Domain/Tarea.cs", "CS0246", message, line: 12);

    private static Diagnostic SecondDomainError() =>
        Diagnostics.Error("src/Domain/Dependencia.cs", "CS1002", "; expected", line: 4);

    private static Diagnostic ApiError(string member = "Cerrar") =>
        Diagnostics.MissingMember("src/Api/TareasController.cs", member);

    [Fact]
    public async Task Runs_the_three_layers_in_order_and_completes()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.Completed, state.Termination!.Reason);
        Assert.Equal(["spec-analyzer", "domain", "api", "frontend"], scenario.InvokedAgents);
        Assert.Equal(NodeId.Completed, state.CurrentNode);
    }

    [Fact]
    public async Task Hands_each_layer_agent_the_tasks_and_rules_of_its_own_layer()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();

        await scenario.RunAsync();

        var domainPrompt = scenario.InvocationsOf(Layer.Domain).Single().Prompt;

        Assert.Contains("T-01", domainPrompt, StringComparison.Ordinal);
        Assert.Contains("T-03", domainPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("T-05", domainPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("T-09", domainPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sends_the_work_back_with_the_diagnostics_and_converges()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(Layer.Domain, AgentTurn.Breaks(Layer.Domain, DomainError()), AgentTurn.Fixes(Layer.Domain));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.Completed, state.Termination!.Reason);

        var domainInvocations = scenario.InvocationsOf(Layer.Domain);
        Assert.Equal(2, domainInvocations.Count);
        Assert.Empty(domainInvocations[0].Diagnostics);
        Assert.Single(domainInvocations[1].Diagnostics);
        Assert.Contains("CS0246", domainInvocations[1].Prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The characteristic edge of the project: the gate looks at the whole workspace, so an
    /// error in a layer that is not the one currently being worked on still goes back to the
    /// agent that owns it.
    /// </summary>
    [Fact]
    public async Task Routes_a_diagnostic_back_to_the_layer_that_owns_the_file()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(
            Layer.Api,
            AgentTurn.Does(workspace => workspace.Replace(Layer.Domain, DomainError())),
            AgentTurn.Fixes(Layer.Api));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.Completed, state.Termination!.Reason);
        Assert.Equal(["spec-analyzer", "domain", "api", "domain", "api", "frontend"], scenario.InvokedAgents);
        Assert.Single(scenario.InvocationsOf(Layer.Domain)[1].Diagnostics);
    }

    [Fact]
    public async Task Stops_when_the_agent_hands_back_the_same_diagnostics_twice()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(Layer.Domain, AgentTurn.Breaks(Layer.Domain, DomainError()), AgentTurn.ChangesNothing());

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.NoProgress, state.Termination!.Reason);
        Assert.Equal(Layer.Domain, state.Termination.Layer);
        Assert.Equal(2, scenario.InvocationsOf(Layer.Domain).Count);
        Assert.Empty(scenario.InvocationsOf(Layer.Api));
    }

    [Fact]
    public async Task Stops_when_the_agent_keeps_producing_new_errors_past_the_ceiling()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(
            Layer.Domain,
            AgentTurn.Breaks(Layer.Domain, DomainError("first")),
            AgentTurn.Breaks(Layer.Domain, DomainError("second")),
            AgentTurn.Breaks(Layer.Domain, DomainError("third")));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.IterationLimitReached, state.Termination!.Reason);
        Assert.Equal(3, scenario.InvocationsOf(Layer.Domain).Count);
    }

    [Fact]
    public async Task A_failed_run_carries_the_trace_of_where_it_got_stuck()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(Layer.Domain, AgentTurn.Breaks(Layer.Domain, DomainError()), AgentTurn.ChangesNothing());

        var state = await scenario.RunAsync();

        Assert.Equal(
            ["spec-analysis", "domain-implementation", "domain-gate", "domain-implementation", "domain-gate"],
            state.Trace.Select(node => node.Value));
        Assert.Equal(NodeId.GateOf(Layer.Domain), state.Termination!.Node);
    }

    // ---- the gate ------------------------------------------------------------------------

    /// <summary>
    /// The most important test of this block. A language server that is still loading returns
    /// an empty list; reading that as "compiles clean" approves code that does not compile,
    /// which is worse than having no gate at all (ADR-010).
    /// </summary>
    [Fact]
    public async Task Never_treats_indexing_as_approval()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(Layer.Domain, AgentTurn.Breaks(Layer.Domain, DomainError()), AgentTurn.Fixes(Layer.Domain));
        scenario.Workspace.IndexingAnswersRemaining = 2;

        var state = await scenario.RunAsync();

        // Had the empty indexing answer been read as a verdict, the run would have completed
        // on the first pass with the domain still broken.
        Assert.Equal(2, scenario.Observer.Of<GateWaitingForIndex>().Count);
        Assert.Equal(2, scenario.InvocationsOf(Layer.Domain).Count);
        Assert.Equal(TerminationReason.Completed, state.Termination!.Reason);
    }

    [Fact]
    public async Task Never_treats_a_partial_indexing_answer_as_approval()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Workspace.IndexingReportsPartialItems = true;
        scenario.Agents.Script(Layer.Domain, AgentTurn.Breaks(Layer.Domain, DomainError()), AgentTurn.Fixes(Layer.Domain));
        scenario.Workspace.IndexingAnswersRemaining = 1;

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.Completed, state.Termination!.Reason);
        Assert.Single(scenario.Observer.Of<GateWaitingForIndex>());
    }

    /// <summary>
    /// The failure block 2 actually produced: a server that answers <c>indexing</c> forever
    /// because a JSON-RPC call was silently rejected (ADR-013). Waiting is mandatory; waiting
    /// without a ceiling is a hang.
    /// </summary>
    [Fact]
    public async Task Stops_with_the_servers_own_explanation_when_the_index_never_settles()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Policy = new GraphPolicy { MaximumIndexWaitAttempts = 4, IndexWaitDelay = TimeSpan.Zero };
        scenario.Workspace.IndexingAnswersRemaining = int.MaxValue;
        scenario.Workspace.IndexingDetail = "Roslyn is loading the solution 'App.slnx'";

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.TerminalFailure, state.Termination!.Reason);
        Assert.Contains("Roslyn is loading the solution", state.Termination.Detail, StringComparison.Ordinal);
        Assert.Equal(4, scenario.Observer.Of<GateWaitingForIndex>().Count);
    }

    [Fact]
    public async Task Actually_waits_between_two_index_queries()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Policy = new GraphPolicy { MaximumIndexWaitAttempts = 3, IndexWaitDelay = TimeSpan.FromMilliseconds(30) };
        scenario.Workspace.IndexingAnswersRemaining = 2;

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        await scenario.RunAsync();

        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task Stops_when_a_diagnostic_belongs_to_no_layer()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Workspace.OrphanDiagnostics.Add(Diagnostics.Error("build/Generated.cs", "CS1002", "; expected"));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.TerminalFailure, state.Termination!.Reason);
        Assert.Contains("build/Generated.cs", state.Termination.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warnings_alone_do_not_send_the_work_back()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(
            Layer.Domain,
            AgentTurn.Breaks(Layer.Domain, Diagnostics.Warning("src/Domain/Tarea.cs", "CS0168", "unused variable")));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.Completed, state.Termination!.Reason);
        Assert.Single(scenario.InvocationsOf(Layer.Domain));
    }

    // ---- agents that do not come back ------------------------------------------------------

    [Theory]
    [InlineData(AgentCompletion.Errored)]
    [InlineData(AgentCompletion.TurnLimitReached)]
    [InlineData(AgentCompletion.TimedOut)]
    public async Task Stops_when_a_layer_agent_does_not_finish(AgentCompletion completion)
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(Layer.Api, AgentTurn.Fails(completion, "el proceso terminó con código 1"));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.TerminalFailure, state.Termination!.Reason);
        Assert.Equal(Layer.Api, state.Termination.Layer);
        Assert.Contains(completion.ToString(), state.Termination.Detail, StringComparison.Ordinal);
        Assert.Empty(scenario.InvocationsOf(Layer.Frontend));
    }

    // ---- the spec analyzer -----------------------------------------------------------------

    [Fact]
    public async Task Retries_the_spec_analyzer_with_the_parse_error_when_its_answer_is_unusable()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.ScriptSpecAnalyzer(
            Fixture.SpecAnalyzerAnswer("unknown-layer.md"),
            Fixture.SpecAnalyzerAnswer("valid-plan.md"));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.Completed, state.Termination!.Reason);

        var analyzerInvocations = scenario.Agents.Invocations.Where(invocation => invocation.AgentName == "spec-analyzer").ToList();
        Assert.Equal(2, analyzerInvocations.Count);
        Assert.Contains("persistencia", analyzerInvocations[1].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stops_when_the_spec_analyzer_never_produces_a_usable_plan()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.ScriptSpecAnalyzer(Fixture.SpecAnalyzerAnswer("no-tasks.md"));

        var state = await scenario.RunAsync();

        Assert.Equal(TerminationReason.IterationLimitReached, state.Termination!.Reason);
        Assert.Equal(NodeId.SpecAnalysis, state.Termination.Node);
        Assert.Empty(scenario.InvocationsOf(Layer.Domain));
    }

    // ---- the log ---------------------------------------------------------------------------

    [Fact]
    public async Task The_log_says_which_rule_is_being_implemented_in_which_layer()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();

        await scenario.RunAsync();

        var domainEntry = scenario.Observer
            .Of<NodeEntered>()
            .Single(entered => entered.Node == NodeId.ImplementationOf(Layer.Domain));

        Assert.Equal("domain", domainEntry.Layer);
        Assert.Equal(["RN-01", "RN-02", "RN-03"], domainEntry.BusinessRules);
    }

    [Fact]
    public async Task The_log_says_what_a_review_iteration_changed()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(
            Layer.Domain,
            AgentTurn.Breaks(Layer.Domain, DomainError("first"), SecondDomainError()),
            AgentTurn.Breaks(Layer.Domain, DomainError("first")),
            AgentTurn.Fixes(Layer.Domain));

        await scenario.RunAsync();

        var iterations = scenario.Observer.Of<ReviewIterationEvaluated>();

        Assert.Equal(2, iterations.Count);
        Assert.Equal(2, iterations[0].Introduced);
        Assert.Equal(1, iterations[1].Resolved);
        Assert.Equal(1, iterations[1].Persisting);
        Assert.Equal(ReviewAction.SendBackToAgent, iterations[1].Action);
    }

    [Fact]
    public async Task The_log_opens_with_the_spec_and_closes_with_the_reason_the_run_stopped()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();

        await scenario.RunAsync();

        var started = scenario.Observer.Single<RunStarted>();
        var terminated = scenario.Observer.Single<RunTerminated>();

        Assert.Equal("specs/gestor-tareas.md", started.SpecPath);
        Assert.Equal(3, started.BusinessRules.Count);
        Assert.Equal(TerminationReason.Completed, terminated.Reason);
        Assert.True(terminated.Duration > TimeSpan.Zero);
        Assert.Equal("run-started", scenario.Observer.EventNames[0]);
        Assert.Equal("run-terminated", scenario.Observer.EventNames[^1]);
    }

    [Fact]
    public async Task The_log_reports_a_plan_that_does_not_cover_every_criterion()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.ScriptSpecAnalyzer(Fixture.SpecAnalyzerAnswer("wrapped-in-fences.md"));

        await scenario.RunAsync();

        Assert.Contains("CA-13", scenario.Observer.Single<PlanProduced>().CriteriaNotCovered);
    }

    [Fact]
    public async Task Every_run_event_renders_a_line_for_the_console()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();
        scenario.Agents.Script(Layer.Api, AgentTurn.Breaks(Layer.Api, ApiError()), AgentTurn.Fixes(Layer.Api));

        await scenario.RunAsync();

        Assert.All(scenario.Observer.Events, runEvent => Assert.False(string.IsNullOrWhiteSpace(runEvent.Summary)));
        Assert.Contains("CS1061", scenario.Observer.Transcript, StringComparison.Ordinal);
    }

    // ---- the gate is always asked about the whole workspace ----------------------------------

    [Fact]
    public async Task Asks_the_gate_about_the_whole_workspace_after_every_layer()
    {
        var scenario = new GraphScenario().WithEveryLayerClean();

        await scenario.RunAsync();

        Assert.Equal([".", ".", "."], scenario.Gate.QueriedScopes);
    }
}
