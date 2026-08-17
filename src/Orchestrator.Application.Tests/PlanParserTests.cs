using Orchestrator.Application.Spec;
using Orchestrator.Domain;

namespace Orchestrator.Application.Tests;

/// <summary>
/// The one place free-form agent text becomes a structure, tested against recorded answers —
/// including the ones a model actually produces when it does not follow instructions.
/// </summary>
public sealed class PlanParserTests
{
    [Fact]
    public void Parses_a_well_formed_plan_and_keeps_the_layer_of_every_task()
    {
        var plan = Parse("valid-plan.md");

        Assert.Equal(12, plan.Tasks.Count);
        Assert.Equal(4, plan.TasksFor(Layer.Domain).Count);
        Assert.Equal(4, plan.TasksFor(Layer.Api).Count);
        Assert.Equal(4, plan.TasksFor(Layer.Frontend).Count);
    }

    [Fact]
    public void Keeps_the_statement_the_rules_and_the_criteria_of_a_task()
    {
        var task = Parse("valid-plan.md").Tasks.Single(candidate => candidate.Identifier == "T-07");

        Assert.Equal(Layer.Api, task.Layer);
        Assert.StartsWith("Exponer la operación de completar", task.Statement, StringComparison.Ordinal);
        Assert.Equal(["RN-01"], task.BusinessRules);
        Assert.Equal(["CA-06", "CA-08"], task.AcceptanceCriteria);
        Assert.Equal(["T-04"], task.DependsOn);
    }

    /// <summary>
    /// What makes the run traceable: the log can say which rule is being implemented in which
    /// layer, not only which node is running (ADR-012, ADR-015).
    /// </summary>
    [Fact]
    public void Says_which_business_rules_each_layer_is_on_the_hook_for()
    {
        var plan = Parse("valid-plan.md");

        Assert.Equal(["RN-01", "RN-02", "RN-03"], plan.BusinessRulesFor(Layer.Domain));
        Assert.Equal(["RN-01"], plan.BusinessRulesFor(Layer.Frontend));
    }

    [Fact]
    public void The_recorded_plan_covers_every_criterion_of_the_real_spec() =>
        Assert.Empty(Parse("valid-plan.md").CriteriaNotCovered(Fixture.RealSpec));

    [Fact]
    public void Reports_the_criteria_a_plan_leaves_out()
    {
        var plan = Parse("wrapped-in-fences.md");

        Assert.Equal(13, plan.CriteriaNotCovered(Fixture.RealSpec).Count);
        Assert.DoesNotContain("CA-05", plan.CriteriaNotCovered(Fixture.RealSpec));
    }

    /// <summary>
    /// Half the time a model wraps its answer in a code fence, uses asterisks for bullets, an
    /// en dash instead of an em dash, and capitalises the layer. None of that carries meaning,
    /// so none of it is a parse failure.
    /// </summary>
    [Fact]
    public void Survives_the_formatting_a_model_varies_freely()
    {
        var plan = Parse("wrapped-in-fences.md");

        Assert.Equal(2, plan.Tasks.Count);
        Assert.Equal(Layer.Api, plan.Tasks[1].Layer);
        Assert.Empty(plan.Tasks[0].DependsOn);
    }

    [Fact]
    public void Rejects_a_plan_that_invents_a_layer()
    {
        var failure = ParseFailure("unknown-layer.md");

        Assert.Contains("persistencia", failure, StringComparison.Ordinal);
        Assert.Contains("dominio, api, frontend", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_plan_that_cites_identifiers_the_spec_does_not_have()
    {
        var failure = ParseFailure("invented-identifier.md");

        Assert.Contains("RN-07", failure, StringComparison.Ordinal);
        Assert.Contains("CA-42", failure, StringComparison.Ordinal);
    }

    /// <summary>Rule 1 of the spec-analyzer prompt, enforced instead of trusted.</summary>
    [Fact]
    public void Rejects_a_task_that_can_be_attributed_to_nothing_in_the_spec()
    {
        var failure = ParseFailure("task-without-citation.md");

        Assert.Contains("T-02", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_answer_that_contains_no_plan()
    {
        var failure = ParseFailure("no-tasks.md");

        Assert.Contains("no task", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_task_declared_before_any_layer()
    {
        var parsed = PlanParser.Parse("### T-01 — Algo\n- Implementa: RN-01", Fixture.RealSpec);

        Assert.True(parsed.IsFailure);
        Assert.Contains("before saying which layer", parsed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_plan_that_reuses_a_task_identifier()
    {
        const string text = """
            ## Capa: dominio

            ### T-01 — Una tarea
            - Implementa: RN-01

            ### T-01 — Otra tarea con el mismo identificador
            - Implementa: RN-02
            """;

        var parsed = PlanParser.Parse(text, Fixture.RealSpec);

        Assert.True(parsed.IsFailure);
        Assert.Contains("T-01", parsed.FailureReason, StringComparison.Ordinal);
    }

    private static TaskPlan Parse(string fileName)
    {
        var parsed = PlanParser.Parse(Fixture.SpecAnalyzerAnswer(fileName), Fixture.RealSpec);

        Assert.True(parsed.IsSuccess, parsed.FailureReason);
        return parsed.Value;
    }

    private static string ParseFailure(string fileName)
    {
        var parsed = PlanParser.Parse(Fixture.SpecAnalyzerAnswer(fileName), Fixture.RealSpec);

        Assert.True(parsed.IsFailure, "The plan was expected not to parse.");
        return parsed.FailureReason;
    }
}
