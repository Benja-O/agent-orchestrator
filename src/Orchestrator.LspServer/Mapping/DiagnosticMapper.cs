using System.Text.Json;
using Orchestrator.LspServer.Contract;
using Orchestrator.LspServer.Protocol;

namespace Orchestrator.LspServer.Mapping;

/// <summary>
/// Turns protocol diagnostics into contract diagnostics: 0-based to 1-based, severity number
/// to name, and the ordering and truncation the contract promises.
/// </summary>
/// <remarks>
/// Deliberately pure and static. This is the part of the server whose behaviour the gate
/// depends on, so it has to be testable without a language server anywhere in sight
/// (AI.md, golden rule 3).
/// </remarks>
public static class DiagnosticMapper
{
    public static SourceRange ToSourceRange(LspRange range) => new()
    {
        StartLine = range.Start.Line + 1,
        StartColumn = range.Start.Character + 1,
        EndLine = range.End.Line + 1,
        EndColumn = range.End.Character + 1,
    };

    public static string ToSeverityName(int? severity) => severity switch
    {
        1 => DiagnosticSeverityNames.Error,
        2 => DiagnosticSeverityNames.Warning,
        3 => DiagnosticSeverityNames.Information,
        4 => DiagnosticSeverityNames.Hint,
        _ => DiagnosticSeverityNames.Information,
    };

    /// <summary>The protocol allows a diagnostic code to be a number or a string; the contract only has strings.</summary>
    public static string ToCode(JsonElement? code) => code switch
    {
        null => string.Empty,
        { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        { ValueKind: JsonValueKind.Number } element => element.ToString(),
        { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => string.Empty,
        { } element => element.ToString(),
    };

    public static DiagnosticItem ToDiagnosticItem(string relativeFilePath, LspDiagnostic diagnostic, string sourceName) => new()
    {
        FilePath = relativeFilePath,
        Range = ToSourceRange(diagnostic.Range),
        Severity = ToSeverityName(diagnostic.Severity),
        Code = ToCode(diagnostic.Code),
        Message = diagnostic.Message,
        Source = sourceName,
    };

    /// <summary>
    /// Assembles the response: sorts, truncates by the tail, and reports what was cut.
    /// </summary>
    /// <remarks>
    /// The order is fixed — severity, then file path, then line, then column — and it matters
    /// because the cut is made at the end. With this priority what survives truncation is
    /// always what blocks compilation. Without a stable order the review loop would hand the
    /// agent an arbitrary subset and send it to fix warnings while the errors stay put.
    /// </remarks>
    public static DiagnosticsResponse Compose(IReadOnlyList<DiagnosticItem> diagnostics, string status, int maximumItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);

        var ordered = diagnostics
            .OrderBy(SeverityRank)
            .ThenBy(item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Range.StartLine)
            .ThenBy(item => item.Range.StartColumn)
            .ToList();

        var items = ordered.Count > maximumItems
            ? ordered.GetRange(0, maximumItems)
            : ordered;

        return new DiagnosticsResponse
        {
            Status = status,
            Total = ordered.Count,
            Truncated = items.Count < ordered.Count,
            Items = items,
        };
    }

    private static int SeverityRank(DiagnosticItem item) => item.Severity switch
    {
        DiagnosticSeverityNames.Error => 0,
        DiagnosticSeverityNames.Warning => 1,
        DiagnosticSeverityNames.Information => 2,
        DiagnosticSeverityNames.Hint => 3,
        _ => 4,
    };
}
