using System.Text.RegularExpressions;
using Orchestrator.Domain;

namespace Orchestrator.Application.Spec;

/// <summary>
/// Turns the spec analyzer's answer into a <see cref="TaskPlan"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the only place in the orchestrator where free-form agent text becomes a
/// structure.</strong> A layer agent does not report to the graph — it writes files, and the
/// gate is what speaks — so nothing else has to parse prose. Concentrating the whole problem
/// in one function is what makes it testable against recorded answers, including the
/// malformed ones, without ever invoking <c>claude -p</c> (ADR-014).
/// </para>
/// <para>
/// The format it accepts is the one <c>templates/agents/spec-analyzer.md</c> instructs the
/// agent to produce. Parsing is forgiving about the things a language model varies freely —
/// surrounding prose, code fences, which dash it uses, whether a bullet is present — and
/// strict about the things that carry meaning: the layer, the identifier, and the citations.
/// </para>
/// </remarks>
public static partial class PlanParser
{
    public static Result<TaskPlan> Parse(string planText, SpecDocument spec)
    {
        var tasks = new List<PlannedTask>();
        Layer? currentLayer = null;
        PlannedTaskDraft? draft = null;

        foreach (var rawLine in StripCodeFences(planText).Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var layerHeading = LayerHeadingPattern().Match(line);

            if (layerHeading.Success)
            {
                if (!LayerCatalog.TryParsePlanName(layerHeading.Groups["layer"].Value, out var parsedLayer))
                {
                    return Result<TaskPlan>.Failure(
                        $"The plan groups tasks under an unknown layer '{layerHeading.Groups["layer"].Value.Trim()}'. "
                        + $"The only layers are: {string.Join(", ", LayerCatalog.InPipelineOrder.Select(LayerCatalog.PlanNameOf))}.");
                }

                Flush(ref draft, tasks);
                currentLayer = parsedLayer;
                continue;
            }

            var taskHeading = TaskHeadingPattern().Match(line);

            if (taskHeading.Success)
            {
                if (currentLayer is null)
                {
                    return Result<TaskPlan>.Failure(
                        $"The plan declares task {taskHeading.Groups["identifier"].Value} before saying which layer it belongs to.");
                }

                Flush(ref draft, tasks);
                draft = new PlannedTaskDraft(taskHeading.Groups["identifier"].Value, currentLayer.Value, taskHeading.Groups["statement"].Value.Trim());
                continue;
            }

            if (draft is null)
            {
                continue;
            }

            var field = FieldPattern().Match(line);

            if (!field.Success)
            {
                continue;
            }

            var values = ParseIdentifierList(field.Groups["values"].Value);

            switch (field.Groups["field"].Value.Trim().ToLowerInvariant())
            {
                case "implementa":
                    draft.BusinessRules.AddRange(values);
                    break;
                case "verifica":
                    draft.AcceptanceCriteria.AddRange(values);
                    break;
                case "depende de":
                    draft.DependsOn.AddRange(values);
                    break;
            }
        }

        Flush(ref draft, tasks);

        if (tasks.Count == 0)
        {
            return Result<TaskPlan>.Failure("The plan contains no task. There is nothing to build.");
        }

        var duplicates = tasks
            .GroupBy(task => task.Identifier, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            return Result<TaskPlan>.Failure($"The plan declares the same task identifier more than once: {string.Join(", ", duplicates)}.");
        }

        // Rule 1 of the spec-analyzer prompt, enforced instead of trusted: a task that cannot
        // be attributed to any rule or criterion is a task nobody asked for, and letting it
        // through is how a pipeline quietly builds something the spec never requested.
        var uncited = tasks.Where(task => !task.CitedIdentifiers.Any()).Select(task => task.Identifier).ToList();

        if (uncited.Count > 0)
        {
            return Result<TaskPlan>.Failure(
                $"The plan has task(s) that cite no spec identifier: {string.Join(", ", uncited)}. Every task has to name the RN-nn or CA-nn it comes from.");
        }

        var invented = tasks
            .SelectMany(task => task.CitedIdentifiers.Select(identifier => (task.Identifier, Cited: identifier)))
            .Where(pair => !spec.Knows(pair.Cited))
            .Select(pair => $"{pair.Identifier} cites {pair.Cited}")
            .ToList();

        if (invented.Count > 0)
        {
            return Result<TaskPlan>.Failure(
                $"The plan cites identifiers that are not in the spec: {string.Join(", ", invented)}.");
        }

        return Result<TaskPlan>.Success(new TaskPlan { Tasks = tasks });
    }

    private static void Flush(ref PlannedTaskDraft? draft, List<PlannedTask> tasks)
    {
        if (draft is null)
        {
            return;
        }

        tasks.Add(draft.ToPlannedTask());
        draft = null;
    }

    private static IReadOnlyList<string> ParseIdentifierList(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0 || NothingPattern().IsMatch(trimmed))
        {
            return [];
        }

        return ListedIdentifierPattern()
            .Matches(trimmed)
            .Select(match => match.Value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Removes markdown code fences, which a model wraps its answer in about half the time.</summary>
    private static string StripCodeFences(string text) => CodeFencePattern().Replace(text, string.Empty);

    private sealed class PlannedTaskDraft(string identifier, Layer layer, string statement)
    {
        public List<string> BusinessRules { get; } = [];

        public List<string> AcceptanceCriteria { get; } = [];

        public List<string> DependsOn { get; } = [];

        public PlannedTask ToPlannedTask() => new()
        {
            Identifier = identifier.ToUpperInvariant(),
            Layer = layer,
            Statement = statement,
            BusinessRules = BusinessRules,
            AcceptanceCriteria = AcceptanceCriteria,
            DependsOn = DependsOn,
        };
    }

    [GeneratedRegex(@"^\s*#{1,6}\s*Capa\s*:\s*(?<layer>[^\r\n]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LayerHeadingPattern();

    [GeneratedRegex(@"^\s*#{1,6}\s*(?<identifier>T-\d{2,})\s*[—–-]?\s*(?<statement>[^\r\n]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TaskHeadingPattern();

    [GeneratedRegex(@"^\s*[-*]\s*(?<field>Implementa|Verifica|Depende de)\s*:\s*(?<values>[^\r\n]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FieldPattern();

    [GeneratedRegex(@"\b(RN|CA|T)-\d{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex ListedIdentifierPattern();

    [GeneratedRegex(@"^(—|–|-|ninguna|ninguno|none|n/a)$", RegexOptions.IgnoreCase)]
    private static partial Regex NothingPattern();

    [GeneratedRegex(@"^\s*```[^\r\n]*$", RegexOptions.Multiline)]
    private static partial Regex CodeFencePattern();
}
