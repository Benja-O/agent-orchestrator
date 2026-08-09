using Orchestrator.Domain;
using Orchestrator.TestSupport;

namespace Orchestrator.Domain.Tests;

/// <summary>
/// The conditional edge of the graph, on its own: no agent, no gateway, no clock.
/// </summary>
public sealed class ReviewPolicyTests
{
    private static readonly GraphPolicy Policy = new() { MaximumAttemptsPerNode = 3 };

    private static readonly SpecDocument Spec = new()
    {
        SourcePath = "specs/gestor-tareas.md",
        Text = "spec",
        BusinessRules = ["RN-01"],
        AcceptanceCriteria = ["CA-01"],
        RulesCitedByCriterion = new Dictionary<string, IReadOnlyList<string>>(),
    };

    private static readonly DiagnosticSet OneError =
        DiagnosticSet.Of(Diagnostics.MissingMember("src/Api/TareasController.cs"));

    private static readonly DiagnosticSet AnotherError =
        DiagnosticSet.Of(Diagnostics.Error("src/Api/TareasController.cs", "CS0103", "the name 'tarea' does not exist", line: 31));

    private static GraphState AfterAttempts(int attempts, DiagnosticSet? previousVerdict = null)
    {
        var state = GraphState.Start(new RunId("test"), Spec);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            state = state.Entering(NodeId.ImplementationOf(Layer.Api));
        }

        return previousVerdict is null ? state : state.WithVerdict(Layer.Api, previousVerdict);
    }

    [Fact]
    public void Advances_when_nothing_blocks()
    {
        var decision = ReviewPolicy.Decide(AfterAttempts(1), Layer.Api, DiagnosticSet.Empty, Policy);

        Assert.Equal(ReviewAction.Advance, decision.Action);
        Assert.Null(decision.Termination);
    }

    [Fact]
    public void Advances_on_warnings_alone()
    {
        var warnings = DiagnosticSet.Of(Diagnostics.Warning("src/Api/Program.cs", "CS0168", "unused variable"));

        Assert.Equal(ReviewAction.Advance, ReviewPolicy.Decide(AfterAttempts(1), Layer.Api, warnings, Policy).Action);
    }

    [Fact]
    public void Sends_the_work_back_the_first_time_the_gate_finds_errors()
    {
        var decision = ReviewPolicy.Decide(AfterAttempts(1), Layer.Api, OneError, Policy);

        Assert.Equal(ReviewAction.SendBackToAgent, decision.Action);
        Assert.Equal(1, decision.Delta.Introduced);
    }

    [Fact]
    public void Stops_when_the_agent_hands_back_the_same_errors_twice()
    {
        var decision = ReviewPolicy.Decide(AfterAttempts(2, previousVerdict: OneError), Layer.Api, OneError, Policy);

        Assert.Equal(ReviewAction.Terminate, decision.Action);
        Assert.Equal(TerminationReason.NoProgress, decision.Termination!.Reason);
        Assert.Equal(Layer.Api, decision.Termination.Layer);
        Assert.True(decision.Delta.IsUnchanged);
    }

    /// <summary>
    /// Non-progress is checked before the attempt ceiling on purpose: an agent returning the
    /// same errors twice will not fix them on the third try, and the point of the check is to
    /// stop paying for that turn.
    /// </summary>
    [Fact]
    public void Non_progress_wins_over_the_attempt_ceiling_when_both_would_fire()
    {
        var decision = ReviewPolicy.Decide(AfterAttempts(3, previousVerdict: OneError), Layer.Api, OneError, Policy);

        Assert.Equal(TerminationReason.NoProgress, decision.Termination!.Reason);
    }

    [Fact]
    public void Stops_when_the_agent_keeps_producing_new_errors_until_the_ceiling()
    {
        var decision = ReviewPolicy.Decide(AfterAttempts(3, previousVerdict: OneError), Layer.Api, AnotherError, Policy);

        Assert.Equal(ReviewAction.Terminate, decision.Action);
        Assert.Equal(TerminationReason.IterationLimitReached, decision.Termination!.Reason);
        Assert.Contains("3 time(s)", decision.Termination.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_iterating_while_the_errors_change_and_there_are_attempts_left()
    {
        var decision = ReviewPolicy.Decide(AfterAttempts(2, previousVerdict: OneError), Layer.Api, AnotherError, Policy);

        Assert.Equal(ReviewAction.SendBackToAgent, decision.Action);
        Assert.Equal(1, decision.Delta.Resolved);
        Assert.Equal(1, decision.Delta.Introduced);
    }

    [Fact]
    public void A_policy_with_no_ceiling_is_rejected_at_construction() =>
        Assert.Throws<InvalidOperationException>(() => new GraphPolicy { MaximumAttemptsPerNode = 0 }.Validate());
}
