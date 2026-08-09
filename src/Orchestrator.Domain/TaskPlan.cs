namespace Orchestrator.Domain;

/// <summary>One unit of work the spec analyzer attributed to a layer.</summary>
public sealed record PlannedTask
{
    /// <summary><c>T-nn</c>. Unique within the plan.</summary>
    public required string Identifier { get; init; }

    public required Layer Layer { get; init; }

    /// <summary>What has to be built, in the spec's terms.</summary>
    public required string Statement { get; init; }

    /// <summary>The <c>RN-nn</c> this task implements.</summary>
    public required IReadOnlyList<string> BusinessRules { get; init; }

    /// <summary>The <c>CA-nn</c> this task should satisfy.</summary>
    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }

    /// <summary>
    /// Other <see cref="Identifier"/>s this task depends on.
    /// </summary>
    /// <remarks>
    /// Recorded but not scheduled on. The pipeline is strictly sequential by layer (debt D2 of
    /// ROADMAP.md), so these are carried for the log and for the prompt, not for ordering.
    /// </remarks>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>Every spec identifier this task claims, in one list.</summary>
    public IEnumerable<string> CitedIdentifiers => BusinessRules.Concat(AcceptanceCriteria);
}

/// <summary>The spec analyzer's output, and the only thing that node produces.</summary>
public sealed record TaskPlan
{
    public required IReadOnlyList<PlannedTask> Tasks { get; init; }

    public IReadOnlyList<PlannedTask> TasksFor(Layer layer) =>
        Tasks.Where(task => task.Layer == layer).ToList();

    /// <summary>
    /// The business rules a layer is on the hook for, deduplicated and in plan order.
    /// </summary>
    /// <remarks>
    /// This is what makes the log say <em>which rule is being implemented in which layer</em>
    /// instead of only which node is running (ADR-012, ADR-015).
    /// </remarks>
    public IReadOnlyList<string> BusinessRulesFor(Layer layer) =>
        TasksFor(layer).SelectMany(task => task.BusinessRules).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The acceptance criteria of the spec that no task in this plan claims.</summary>
    public IReadOnlyList<string> CriteriaNotCovered(SpecDocument spec)
    {
        var claimed = Tasks
            .SelectMany(task => task.AcceptanceCriteria)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return spec.AcceptanceCriteria.Where(criterion => !claimed.Contains(criterion)).ToList();
    }
}
