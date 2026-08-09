using Orchestrator.Domain;
using Orchestrator.TestSupport;

namespace Orchestrator.Domain.Tests;

public sealed class GraphStateTests
{
    private static readonly SpecDocument Spec = new()
    {
        SourcePath = "specs/gestor-tareas.md",
        Text = "spec",
        BusinessRules = ["RN-01"],
        AcceptanceCriteria = ["CA-01"],
        RulesCitedByCriterion = new Dictionary<string, IReadOnlyList<string>>(),
    };

    private static GraphState Fresh() => GraphState.Start(new RunId("test"), Spec);

    [Fact]
    public void A_transition_leaves_the_previous_state_untouched()
    {
        var before = Fresh();
        var after = before.Entering(NodeId.ImplementationOf(Layer.Domain));

        Assert.Equal(NodeId.SpecAnalysis, before.CurrentNode);
        Assert.Empty(before.Trace);
        Assert.Equal(0, before.AttemptsOf(NodeId.ImplementationOf(Layer.Domain)));
        Assert.Equal(1, after.AttemptsOf(NodeId.ImplementationOf(Layer.Domain)));
    }

    [Fact]
    public void Entering_the_same_node_twice_counts_two_attempts()
    {
        var node = NodeId.ImplementationOf(Layer.Api);
        var state = Fresh().Entering(node).Entering(node);

        Assert.Equal(2, state.AttemptsOf(node));
        Assert.Equal([node, node], state.Trace);
    }

    [Fact]
    public void Keeps_the_last_verdict_of_each_layer_apart()
    {
        var state = Fresh()
            .WithVerdict(Layer.Domain, DiagnosticSet.Of(Diagnostics.Error("src/Domain/Tarea.cs", "CS1002", "; expected")))
            .WithVerdict(Layer.Api, DiagnosticSet.Empty);

        Assert.True(state.LastVerdictOf(Layer.Domain)!.HasBlockingItems);
        Assert.False(state.LastVerdictOf(Layer.Api)!.HasBlockingItems);
        Assert.Null(state.LastVerdictOf(Layer.Frontend));
    }

    [Fact]
    public void A_terminated_run_lands_on_a_terminal_node()
    {
        var failed = Fresh().TerminatedWith(RunTermination.Failure(TerminationReason.NoProgress, "stuck"));
        var completed = Fresh().TerminatedWith(RunTermination.Completed());

        Assert.Equal(NodeId.Failed, failed.CurrentNode);
        Assert.Equal(NodeId.Completed, completed.CurrentNode);
        Assert.True(failed.HasTerminated);
    }

    [Fact]
    public void A_failure_cannot_claim_to_be_a_completion() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RunTermination.Failure(TerminationReason.Completed, "not a failure"));
}
