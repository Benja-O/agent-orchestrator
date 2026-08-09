using System.Globalization;
using Orchestrator.Application.Spec;
using Orchestrator.Domain;

namespace Orchestrator.Application.Graph;

/// <summary>
/// The state machine: spec analysis, then one layer at a time, each followed by the gate, with
/// a review cycle back to whichever agent owns the errors.
/// </summary>
/// <remarks>
/// <para>
/// Written by hand rather than configured in a graph framework (ADR-003). The characteristic
/// edge of this pipeline — <em>if the gate reports errors, go back to the agent of the layer
/// that produced them, with the diagnostics as input; otherwise advance</em> — is the part of
/// the project being evaluated, so it is code you can read rather than a library's
/// configuration.
/// </para>
/// <para>
/// <strong>Every cycle terminates, three ways, all of them written here</strong> because no
/// framework provides them and an unbounded review loop against <c>claude -p</c> spends the
/// five-hour Pro window in one run (ADR-001):
/// </para>
/// <list type="number">
/// <item>A per-node attempt ceiling, checked both before an agent is invoked
/// (<see cref="RunLayersAsync"/>) and at the gate (<see cref="ReviewPolicy"/>), so the run
/// stops before paying for a turn it already knows is the last.</item>
/// <item>Non-progress: the same set of diagnostics twice in a row (<see cref="ReviewPolicy"/>).</item>
/// <item>Terminal failure with the trace: an unusable agent, a gate that never left
/// <c>indexing</c>, a plan that cannot be parsed, a diagnostic in a file no layer owns.</item>
/// </list>
/// <para>
/// A fourth ceiling that ADR-003 does not enumerate lives in <see cref="GateEvaluator"/>: the
/// number of times the gate may be re-asked while it answers <c>indexing</c>.
/// </para>
/// </remarks>
public sealed class GraphRunner
{
    /// <summary>
    /// The gate always looks at the whole workspace, not just the layer that was worked on.
    /// </summary>
    /// <remarks>
    /// Scoping the query to the current layer would make the run look tidier and would hide
    /// the case the project is about: the API agent calling a domain method that does not
    /// exist. A workspace-wide verdict, attributed through <see cref="LayerMap"/>, is what
    /// lets the conditional edge send the work back to the layer that actually owns it.
    /// </remarks>
    private const string WorkspaceScope = ".";

    private readonly IAgentRunner _agentRunner;
    private readonly GateEvaluator _gateEvaluator;
    private readonly IRunObserver _observer;
    private readonly TimeProvider _timeProvider;
    private readonly GraphPolicy _policy;
    private readonly LayerMap _layerMap;

    public GraphRunner(
        IAgentRunner agentRunner,
        ILanguageServerGateway gateway,
        IRunObserver observer,
        TimeProvider timeProvider,
        GraphPolicy? policy = null,
        LayerMap? layerMap = null)
    {
        _policy = policy ?? GraphPolicy.Default;
        _policy.Validate();

        _agentRunner = agentRunner;
        _observer = observer;
        _timeProvider = timeProvider;
        _layerMap = layerMap ?? LayerMap.Default;
        _gateEvaluator = new GateEvaluator(gateway, observer, timeProvider, _policy);
    }

    public async Task<GraphState> RunAsync(SpecDocument spec, CancellationToken cancellationToken)
    {
        var runId = RunId.NewRunId();
        var startedAt = _timeProvider.GetTimestamp();
        var state = GraphState.Start(runId, spec);

        Observe(new RunStarted
        {
            RunId = runId,
            Timestamp = _timeProvider.GetUtcNow(),
            SpecPath = spec.SourcePath,
            BusinessRules = spec.BusinessRules,
            AcceptanceCriteria = spec.AcceptanceCriteria,
        });

        state = await RunSpecAnalysisAsync(state, cancellationToken).ConfigureAwait(false);

        if (!state.HasTerminated)
        {
            state = await RunLayersAsync(state, cancellationToken).ConfigureAwait(false);
        }

        if (!state.HasTerminated)
        {
            state = state.TerminatedWith(RunTermination.Completed());
        }

        Observe(new RunTerminated
        {
            RunId = runId,
            Timestamp = _timeProvider.GetUtcNow(),
            Reason = state.Termination!.Reason,
            Detail = state.Termination.Detail,
            Duration = _timeProvider.GetElapsedTime(startedAt),
            Node = state.Termination.Node?.Value,
            Layer = state.Termination.Layer is { } layer ? LayerCatalog.AgentNameOf(layer) : null,
            Trace = state.Trace.Select(node => node.Value).ToList(),
        });

        return state;
    }

    /// <summary>
    /// Runs the spec analyzer until its answer parses, or until the node runs out of attempts.
    /// </summary>
    /// <remarks>
    /// The retry is not politeness. This is the one node whose output is prose (ADR-014), so
    /// it is the one node that can fail by producing something unreadable rather than
    /// something wrong — and handing the parse error back is far cheaper than failing the run.
    /// The attempt ceiling still applies, because "ask again" without one is the unbounded
    /// loop ADR-003 forbids.
    /// </remarks>
    private async Task<GraphState> RunSpecAnalysisAsync(GraphState state, CancellationToken cancellationToken)
    {
        string? previousParseFailure = null;

        while (true)
        {
            state = state.Entering(NodeId.SpecAnalysis);
            var attempt = state.AttemptsOf(NodeId.SpecAnalysis);

            Observe(new NodeEntered
            {
                RunId = state.RunId,
                Timestamp = _timeProvider.GetUtcNow(),
                Node = NodeId.SpecAnalysis,
                Attempt = attempt,
            });

            var outcome = await InvokeAgentAsync(
                state,
                new AgentInvocation
                {
                    AgentName = "spec-analyzer",
                    Node = NodeId.SpecAnalysis,
                    Attempt = attempt,
                    Prompt = AgentPrompts.ForSpecAnalysis(state.Spec, previousParseFailure),
                },
                cancellationToken).ConfigureAwait(false);

            if (!outcome.IsUsable)
            {
                return state.TerminatedWith(RunTermination.Failure(
                    TerminationReason.TerminalFailure,
                    $"The spec analyzer did not finish: {outcome.Completion} — {outcome.FailureDetail}",
                    NodeId.SpecAnalysis));
            }

            var plan = PlanParser.Parse(outcome.Transcript, state.Spec);

            if (plan.IsSuccess)
            {
                Observe(new PlanProduced
                {
                    RunId = state.RunId,
                    Timestamp = _timeProvider.GetUtcNow(),
                    TaskCount = plan.Value.Tasks.Count,
                    TaskCountByLayer = LayerCatalog.InPipelineOrder.ToDictionary(
                        LayerCatalog.AgentNameOf,
                        layer => plan.Value.TasksFor(layer).Count),
                    CriteriaNotCovered = plan.Value.CriteriaNotCovered(state.Spec),
                });

                return state.WithPlan(plan.Value);
            }

            previousParseFailure = plan.FailureReason;

            if (attempt >= _policy.MaximumAttemptsPerNode)
            {
                return state.TerminatedWith(RunTermination.Failure(
                    TerminationReason.IterationLimitReached,
                    $"The spec analyzer produced a plan that could not be parsed {attempt} time(s). Last reason: {plan.FailureReason}",
                    NodeId.SpecAnalysis));
            }
        }
    }

    private async Task<GraphState> RunLayersAsync(GraphState state, CancellationToken cancellationToken)
    {
        var plan = state.Plan!;
        var layers = LayerCatalog.InPipelineOrder;
        var stageIndex = 0;

        while (stageIndex < layers.Count)
        {
            var layer = layers[stageIndex];
            var implementationNode = NodeId.ImplementationOf(layer);

            // The backstop for the attempt ceiling. ReviewPolicy normally stops the run one
            // iteration earlier, with a better message; this is what makes the bound provable
            // no matter which path led back here.
            if (state.AttemptsOf(implementationNode) >= _policy.MaximumAttemptsPerNode)
            {
                return state.TerminatedWith(RunTermination.Failure(
                    TerminationReason.IterationLimitReached,
                    $"The {LayerCatalog.AgentNameOf(layer)} agent already ran {_policy.MaximumAttemptsPerNode} time(s).",
                    implementationNode,
                    layer));
            }

            state = state.Entering(implementationNode);
            var attempt = state.AttemptsOf(implementationNode);
            var diagnosticsToFix = state.LastVerdictOf(layer)?.BlockingItems ?? [];

            Observe(new NodeEntered
            {
                RunId = state.RunId,
                Timestamp = _timeProvider.GetUtcNow(),
                Node = implementationNode,
                Attempt = attempt,
                Layer = LayerCatalog.AgentNameOf(layer),
                BusinessRules = plan.BusinessRulesFor(layer),
            });

            var outcome = await InvokeAgentAsync(
                state,
                new AgentInvocation
                {
                    AgentName = LayerCatalog.AgentNameOf(layer),
                    Node = implementationNode,
                    Attempt = attempt,
                    Prompt = AgentPrompts.ForLayer(layer, plan, state.Spec, diagnosticsToFix),
                    Diagnostics = diagnosticsToFix,
                },
                cancellationToken).ConfigureAwait(false);

            if (!outcome.IsUsable)
            {
                return state.TerminatedWith(RunTermination.Failure(
                    TerminationReason.TerminalFailure,
                    $"The {LayerCatalog.AgentNameOf(layer)} agent did not finish: {outcome.Completion} — {outcome.FailureDetail}",
                    implementationNode,
                    layer));
            }

            var gateNode = NodeId.GateOf(layer);
            state = state.Entering(gateNode);

            Observe(new NodeEntered
            {
                RunId = state.RunId,
                Timestamp = _timeProvider.GetUtcNow(),
                Node = gateNode,
                Attempt = state.AttemptsOf(gateNode),
                Layer = LayerCatalog.AgentNameOf(layer),
            });

            var evaluation = await _gateEvaluator.EvaluateAsync(state.RunId, WorkspaceScope, cancellationToken).ConfigureAwait(false);

            if (evaluation.IsFailure)
            {
                return state.TerminatedWith(RunTermination.Failure(
                    TerminationReason.TerminalFailure,
                    evaluation.FailureReason,
                    gateNode,
                    layer));
            }

            var attribution = _layerMap.Attribute(evaluation.Value.Items);

            if (attribution.IsFailure)
            {
                return state.TerminatedWith(RunTermination.Failure(
                    TerminationReason.TerminalFailure,
                    attribution.FailureReason,
                    gateNode,
                    layer));
            }

            var verdictByLayer = SplitByLayer(evaluation.Value, attribution.Value);
            ObserveGateVerdict(state, layer, evaluation.Value);

            var responsible = FirstBlockingLayer(verdictByLayer, stageIndex);

            if (responsible is null)
            {
                state = RecordVerdicts(state, verdictByLayer);
                stageIndex++;
                continue;
            }

            var decision = ReviewPolicy.Decide(state, responsible.Value, verdictByLayer[responsible.Value], _policy);

            Observe(new ReviewIterationEvaluated
            {
                RunId = state.RunId,
                Timestamp = _timeProvider.GetUtcNow(),
                Layer = LayerCatalog.AgentNameOf(responsible.Value),
                Iteration = state.AttemptsOf(NodeId.ImplementationOf(responsible.Value)),
                Resolved = decision.Delta.Resolved,
                Introduced = decision.Delta.Introduced,
                Persisting = decision.Delta.Persisting,
                Action = decision.Action,
            });

            state = RecordVerdicts(state, verdictByLayer);

            if (decision.Action == ReviewAction.Terminate)
            {
                return state.TerminatedWith(decision.Termination!);
            }

            stageIndex = LayerCatalog.PipelineIndexOf(responsible.Value);
        }

        return state;
    }

    /// <summary>
    /// The earliest layer, in pipeline order, that has blocking diagnostics and has already had
    /// its turn.
    /// </summary>
    /// <remarks>
    /// Earliest, because a domain error is very often the cause of the API error sitting on top
    /// of it, and fixing the API against a broken domain is work thrown away. Layers past the
    /// current stage are skipped on purpose: they have not run yet, so there is nothing to send
    /// back to them, and nothing escapes — the last stage's gate sees every layer.
    /// </remarks>
    private static Layer? FirstBlockingLayer(IReadOnlyDictionary<Layer, DiagnosticSet> verdictByLayer, int stageIndex)
    {
        for (var index = 0; index <= stageIndex; index++)
        {
            var layer = LayerCatalog.InPipelineOrder[index];

            if (verdictByLayer[layer].HasBlockingItems)
            {
                return layer;
            }
        }

        return null;
    }

    /// <summary>Splits one workspace-wide verdict into the per-layer verdicts the review policy compares.</summary>
    private static IReadOnlyDictionary<Layer, DiagnosticSet> SplitByLayer(
        DiagnosticSet whole,
        IReadOnlyDictionary<Layer, IReadOnlyList<Diagnostic>> attributed)
    {
        return LayerCatalog.InPipelineOrder.ToDictionary(
            layer => layer,
            layer =>
            {
                var items = attributed.TryGetValue(layer, out var forLayer) ? forLayer : [];

                return new DiagnosticSet
                {
                    Items = items,
                    Total = items.Count,

                    // Inherited from the whole response on purpose. If the server cut the list,
                    // this slice might be short too, and saying so is what keeps the fingerprint
                    // honest about what it does not know (see DiagnosticSet.Fingerprint).
                    Truncated = whole.Truncated,
                };
            });
    }

    private static GraphState RecordVerdicts(GraphState state, IReadOnlyDictionary<Layer, DiagnosticSet> verdictByLayer)
    {
        foreach (var (layer, verdict) in verdictByLayer)
        {
            state = state.WithVerdict(layer, verdict);
        }

        return state;
    }

    private void ObserveGateVerdict(GraphState state, Layer layer, DiagnosticSet verdict)
    {
        Observe(new GateEvaluated
        {
            RunId = state.RunId,
            Timestamp = _timeProvider.GetUtcNow(),
            Layer = LayerCatalog.AgentNameOf(layer),
            Scope = WorkspaceScope,
            Total = verdict.Total,
            Truncated = verdict.Truncated,
            ErrorCount = verdict.Items.Count(item => item.Severity == DiagnosticSeverity.Error),
            WarningCount = verdict.Items.Count(item => item.Severity == DiagnosticSeverity.Warning),
            Fingerprint = verdict.Fingerprint(),
            BlockingSample = verdict.BlockingItems
                .Take(3)
                .Select(item => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{item.FilePath}:{item.Range.StartLine} {item.Code} {item.Message}"))
                .ToList(),
        });
    }

    private async Task<AgentOutcome> InvokeAgentAsync(GraphState state, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        Observe(new AgentInvoked
        {
            RunId = state.RunId,
            Timestamp = _timeProvider.GetUtcNow(),
            Node = invocation.Node,
            AgentName = invocation.AgentName,
            Attempt = invocation.Attempt,
            DiagnosticsHandedOver = invocation.Diagnostics.Count,
        });

        var startedAt = _timeProvider.GetTimestamp();

        // The orchestrator's own ceiling on a single turn, independent of the maxTurns that
        // Claude Code applies inside it (ADR-011). An agent hung in its own turn never returns
        // control, so the graph's iteration limit would never get a chance to fire.
        using var timeout = new CancellationTokenSource(_policy.AgentTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        AgentOutcome outcome;

        try
        {
            outcome = await _agentRunner.RunAsync(invocation, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = AgentOutcome.Failed(
                AgentCompletion.TimedOut,
                $"The agent did not answer within {_policy.AgentTimeout}.");
        }

        Observe(new AgentReturned
        {
            RunId = state.RunId,
            Timestamp = _timeProvider.GetUtcNow(),
            Node = invocation.Node,
            AgentName = invocation.AgentName,
            Completion = outcome.Completion,
            Duration = _timeProvider.GetElapsedTime(startedAt),
            FailureDetail = outcome.FailureDetail,
        });

        return outcome;
    }

    private void Observe(RunEvent runEvent) => _observer.Observe(runEvent);
}
