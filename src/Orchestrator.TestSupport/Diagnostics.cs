using Orchestrator.Domain;

namespace Orchestrator.TestSupport;

/// <summary>Shorthand for the diagnostics a test needs to describe a broken workspace.</summary>
/// <remarks>
/// The defaults are taken from what the real servers produced in block 2, so a scenario reads
/// like something that actually happened rather than like placeholder data.
/// </remarks>
public static class Diagnostics
{
    public static Diagnostic Error(string filePath, string code, string message, int line = 1, string? source = null) =>
        Create(filePath, code, message, line, DiagnosticSeverity.Error, source);

    public static Diagnostic Warning(string filePath, string code, string message, int line = 1, string? source = null) =>
        Create(filePath, code, message, line, DiagnosticSeverity.Warning, source);

    /// <summary>The error block 2 measured against <c>fixtures/broken-csharp</c>.</summary>
    public static Diagnostic MissingMember(string filePath, string member = "Cerrar", int line = 27) =>
        Error(filePath, "CS1061", $"'Tarea' does not contain a definition for '{member}'", line);

    private static Diagnostic Create(string filePath, string code, string message, int line, DiagnosticSeverity severity, string? source)
    {
        var isTypeScript = filePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase);

        return new Diagnostic
        {
            FilePath = filePath,
            Range = new SourceRange(line, 5, line, 20),
            Severity = severity,
            Code = code,
            Message = message,
            Source = source ?? (isTypeScript ? "typescript" : "roslyn"),
        };
    }
}
