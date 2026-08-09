using System.Text.RegularExpressions;
using Orchestrator.Domain;

namespace Orchestrator.Application.Spec;

/// <summary>
/// Reads the identifiers out of an SDD spec and checks the spec's own invariants before a
/// single agent turn is paid for.
/// </summary>
/// <remarks>
/// <para>
/// ADR-012 chose a human markdown document with stable identifiers over a structured file,
/// precisely so the spec analyzer has real work to do. The price of that choice is that the
/// document can be internally inconsistent, and an inconsistent spec is a broken input that
/// contaminates everything downstream. This is where that is caught — at the start of the
/// run, which is the only place where catching it is free.
/// </para>
/// <para>
/// <strong>The convention this parser recognises</strong>, kept as small as ADR-012 promised:
/// an identifier is <em>declared</em> either by a heading that contains it or by being the
/// first cell of a table row. Anywhere else it is a <em>citation</em>.
/// </para>
/// </remarks>
public static partial class SpecParser
{
    public static Result<SpecDocument> Parse(string sourcePath, string text)
    {
        var businessRules = new List<string>();
        var acceptanceCriteria = new List<string>();
        var rulesCitedByCriterion = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();
        var citedRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith('#'))
            {
                var declared = IdentifierPattern().Match(trimmed);

                if (declared.Success)
                {
                    Declare(declared.Value, businessRules, acceptanceCriteria, duplicates);
                }

                continue;
            }

            if (!trimmed.StartsWith('|'))
            {
                continue;
            }

            var cells = trimmed.Trim('|').Split('|');
            var inFirstCell = IdentifierPattern().Match(cells[0]);

            if (!inFirstCell.Success)
            {
                continue;
            }

            Declare(inFirstCell.Value, businessRules, acceptanceCriteria, duplicates);

            var citations = cells
                .Skip(1)
                .SelectMany(cell => IdentifierPattern().Matches(cell).Select(match => match.Value))
                .Where(identifier => identifier.StartsWith("RN-", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var citation in citations)
            {
                citedRules.Add(citation);
            }

            if (inFirstCell.Value.StartsWith("CA-", StringComparison.OrdinalIgnoreCase))
            {
                rulesCitedByCriterion[inFirstCell.Value] = citations;
            }
        }

        if (duplicates.Count > 0)
        {
            return Result<SpecDocument>.Failure(
                $"The spec declares the same identifier more than once: {string.Join(", ", duplicates.Distinct())}.");
        }

        if (businessRules.Count == 0)
        {
            return Result<SpecDocument>.Failure("The spec declares no business rule (RN-nn). It cannot be verified against anything.");
        }

        if (acceptanceCriteria.Count == 0)
        {
            return Result<SpecDocument>.Failure("The spec declares no acceptance criterion (CA-nn). There would be no way to tell a finished run from a failed one.");
        }

        var gap = FirstGap(businessRules) ?? FirstGap(acceptanceCriteria);

        if (gap is not null)
        {
            return Result<SpecDocument>.Failure(
                $"The spec's identifiers are not correlative: {gap} is missing. A gap usually means a rule was deleted and something still cites it.");
        }

        // The invariant that actually matters, and the one a citation can violate silently:
        // a criterion pointing at a rule that does not exist. The reverse — a criterion that
        // cites no rule at all — is legitimate: the spec uses those for basic functionality.
        var dangling = citedRules
            .Where(rule => !businessRules.Contains(rule, StringComparer.OrdinalIgnoreCase))
            .OrderBy(rule => rule, StringComparer.Ordinal)
            .ToList();

        if (dangling.Count > 0)
        {
            return Result<SpecDocument>.Failure(
                $"The spec cites business rules that it never declares: {string.Join(", ", dangling)}.");
        }

        return Result<SpecDocument>.Success(new SpecDocument
        {
            SourcePath = sourcePath,
            Text = text,
            BusinessRules = businessRules,
            AcceptanceCriteria = acceptanceCriteria,
            RulesCitedByCriterion = rulesCitedByCriterion,
        });
    }

    private static void Declare(string identifier, List<string> businessRules, List<string> acceptanceCriteria, List<string> duplicates)
    {
        var target = identifier.StartsWith("RN-", StringComparison.OrdinalIgnoreCase) ? businessRules : acceptanceCriteria;

        if (target.Contains(identifier, StringComparer.OrdinalIgnoreCase))
        {
            duplicates.Add(identifier);
            return;
        }

        target.Add(identifier);
    }

    /// <summary>The first identifier missing from an otherwise correlative sequence starting at 01.</summary>
    private static string? FirstGap(IReadOnlyList<string> identifiers)
    {
        var prefix = identifiers[0][..3];
        var numbers = identifiers
            .Select(identifier => int.Parse(identifier[3..], System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(number => number)
            .ToList();

        for (var expected = 1; expected <= numbers.Count; expected++)
        {
            if (numbers[expected - 1] != expected)
            {
                return $"{prefix}{expected:00}";
            }
        }

        return null;
    }

    [GeneratedRegex(@"\b(RN|CA)-\d{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex IdentifierPattern();
}
