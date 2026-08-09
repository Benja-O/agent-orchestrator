namespace Orchestrator.Domain;

/// <summary>
/// The input spec, reduced to the part the pipeline can reason about mechanically: its
/// identifiers (ADR-012).
/// </summary>
/// <remarks>
/// The prose stays in <see cref="Text"/> and goes into the agent prompts untouched — the
/// point of ADR-012 is that the spec is a human document. What is extracted here is only what
/// makes the run <em>traceable</em>: which rule is being implemented in which layer, and
/// which criteria a plan claims to cover.
/// </remarks>
public sealed record SpecDocument
{
    /// <summary>Where the spec was read from, for the log.</summary>
    public required string SourcePath { get; init; }

    /// <summary>The whole document, verbatim.</summary>
    public required string Text { get; init; }

    /// <summary>Business rule identifiers, <c>RN-nn</c>, in document order.</summary>
    public required IReadOnlyList<string> BusinessRules { get; init; }

    /// <summary>Acceptance criteria identifiers, <c>CA-nn</c>, in document order.</summary>
    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }

    /// <summary>For each acceptance criterion, the business rules it cites. Empty for the ones that cover basic functionality.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> RulesCitedByCriterion { get; init; }

    public bool Knows(string identifier) =>
        BusinessRules.Contains(identifier, StringComparer.OrdinalIgnoreCase)
        || AcceptanceCriteria.Contains(identifier, StringComparer.OrdinalIgnoreCase);
}
