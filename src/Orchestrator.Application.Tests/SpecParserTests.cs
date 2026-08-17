using Orchestrator.Application.Spec;

namespace Orchestrator.Application.Tests;

/// <summary>
/// The invariants ADR-012 said would be "a grep today and a test in block 3". This is that
/// test, and it runs against the repository's real spec.
/// </summary>
public sealed class SpecParserTests
{
    [Fact]
    public void The_repositorys_spec_declares_the_identifiers_the_pipeline_expects()
    {
        var spec = Fixture.RealSpec;

        Assert.Equal(["RN-01", "RN-02", "RN-03"], spec.BusinessRules);
        Assert.Equal(15, spec.AcceptanceCriteria.Count);
        Assert.Equal("CA-15", spec.AcceptanceCriteria[^1]);
    }

    [Fact]
    public void Reads_which_rule_each_criterion_verifies()
    {
        var spec = Fixture.RealSpec;

        Assert.Equal(["RN-01"], spec.RulesCitedByCriterion["CA-06"]);
        Assert.Equal(["RN-02"], spec.RulesCitedByCriterion["CA-09"]);

        // Criteria that cover basic functionality cite no rule, and that is legitimate.
        Assert.Empty(spec.RulesCitedByCriterion["CA-01"]);
    }

    /// <summary>
    /// The invariant that a citation can break silently: a criterion pointing at a rule that
    /// does not exist. A spec that is internally inconsistent is a broken input, and every
    /// component downstream inherits the breakage.
    /// </summary>
    [Fact]
    public void Rejects_a_criterion_that_cites_a_rule_the_spec_never_declares()
    {
        const string text = """
            ### RN-01 — Una regla

            | ID | Criterio | Verifica |
            |---|---|---|
            | **CA-01** | Algo observable | RN-01 |
            | **CA-02** | Otra cosa | RN-09 |
            """;

        var parsed = SpecParser.Parse("test.md", text);

        Assert.True(parsed.IsFailure);
        Assert.Contains("RN-09", parsed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_spec_that_declares_the_same_identifier_twice()
    {
        const string text = """
            ### RN-01 — Una regla
            ### RN-01 — La misma regla otra vez

            | ID | Criterio |
            |---|---|
            | **CA-01** | Algo observable |
            """;

        var parsed = SpecParser.Parse("test.md", text);

        Assert.True(parsed.IsFailure);
        Assert.Contains("RN-01", parsed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_identifiers_with_a_gap_in_the_middle()
    {
        const string text = """
            ### RN-01 — Una regla
            ### RN-03 — Otra regla, y falta la del medio

            | ID | Criterio |
            |---|---|
            | **CA-01** | Algo observable |
            """;

        var parsed = SpecParser.Parse("test.md", text);

        Assert.True(parsed.IsFailure);
        Assert.Contains("RN-02", parsed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_spec_with_no_acceptance_criterion()
    {
        var parsed = SpecParser.Parse("test.md", "### RN-01 — Una regla sin forma de verificarla");

        Assert.True(parsed.IsFailure);
        Assert.Contains("CA-nn", parsed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_document_that_is_not_a_spec_at_all()
    {
        var parsed = SpecParser.Parse("test.md", "# Notas sueltas\n\nAlgunas ideas para la aplicación.");

        Assert.True(parsed.IsFailure);
        Assert.Contains("RN-nn", parsed.FailureReason, StringComparison.Ordinal);
    }
}
