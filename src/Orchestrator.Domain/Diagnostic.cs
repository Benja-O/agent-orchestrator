using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Orchestrator.Domain;

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4,
}

/// <summary>A 1-based source range, as the MCP contract exposes it.</summary>
public readonly record struct SourceRange(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{StartLine}:{StartColumn}-{EndLine}:{EndColumn}");
}

/// <summary>
/// One diagnostic as the graph reasons about it: the domain-side twin of the contract's
/// <c>DiagnosticItem</c>, translated by <c>Orchestrator.Lsp</c>.
/// </summary>
/// <remarks>
/// The translation is not a formality. It is what keeps golden rule 1 true: the graph decides
/// transitions on this type, never on a wire shape, so it cannot start depending on the
/// language server's spelling of anything.
/// </remarks>
public sealed record Diagnostic
{
    /// <summary>Relative to the workspace root, as the contract guarantees.</summary>
    public required string FilePath { get; init; }

    public required SourceRange Range { get; init; }

    public required DiagnosticSeverity Severity { get; init; }

    /// <summary>The compiler error code, such as <c>CS1061</c>. Empty when the server gave none.</summary>
    public required string Code { get; init; }

    public required string Message { get; init; }

    /// <summary>Which language server produced it: <c>roslyn</c> or <c>typescript</c>.</summary>
    public required string Source { get; init; }

    public bool IsBlocking => Severity == DiagnosticSeverity.Error;

    /// <summary>Everything that makes this diagnostic distinguishable from another one.</summary>
    internal string CanonicalForm() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Severity}|{FilePath}|{Range}|{Code}|{Message}");
}

/// <summary>
/// What the gate saw in one scope, at one moment: the diagnostics plus the truncation the
/// contract declared.
/// </summary>
public sealed record DiagnosticSet
{
    public static DiagnosticSet Empty { get; } = new() { Items = [], Total = 0, Truncated = false };

    public required IReadOnlyList<Diagnostic> Items { get; init; }

    /// <summary>How many diagnostics exist in the scope, before truncation.</summary>
    public required int Total { get; init; }

    /// <summary>True when <see cref="Items"/> holds fewer entries than <see cref="Total"/>.</summary>
    public required bool Truncated { get; init; }

    public IReadOnlyList<Diagnostic> BlockingItems => Items.Where(item => item.IsBlocking).ToList();

    public bool HasBlockingItems => Items.Any(item => item.IsBlocking);

    public static DiagnosticSet Of(params Diagnostic[] items) => new()
    {
        Items = items,
        Total = items.Length,
        Truncated = false,
    };

    /// <summary>
    /// A stable fingerprint of this set, used to detect that an agent is not making progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Order-independent: two responses listing the same diagnostics in a different order are
    /// the same lack of progress. <see cref="Total"/> is folded in so that a change below the
    /// truncation line still moves the fingerprint whenever it moves the count.
    /// </para>
    /// <para>
    /// <strong>Known limit, stated rather than hidden.</strong> When <see cref="Truncated"/>
    /// is true the fingerprint only covers the visible window, so an agent could be fixing
    /// diagnostics that never made it into <see cref="Items"/> and still look stuck. That is
    /// why non-progress is not the only way out of a cycle: the per-node attempt limit backs
    /// it up, and the log records <c>truncated</c> on every gate verdict so the case is
    /// visible when it happens (ADR-014).
    /// </para>
    /// </remarks>
    public string Fingerprint()
    {
        var canonical = Items
            .Select(item => item.CanonicalForm())
            .OrderBy(form => form, StringComparer.Ordinal);

        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"total={Total};truncated={Truncated};{string.Join("\n", canonical)}");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash)[..16];
    }

    /// <summary>How this set differs from the one the previous iteration produced.</summary>
    public DiagnosticDelta CompareWith(DiagnosticSet previous)
    {
        var previousForms = previous.Items.Select(item => item.CanonicalForm()).ToHashSet(StringComparer.Ordinal);
        var currentForms = Items.Select(item => item.CanonicalForm()).ToHashSet(StringComparer.Ordinal);

        return new DiagnosticDelta(
            Resolved: previousForms.Count(form => !currentForms.Contains(form)),
            Introduced: currentForms.Count(form => !previousForms.Contains(form)),
            Persisting: currentForms.Count(previousForms.Contains));
    }
}

/// <summary>
/// The one number the demo is really about: how many diagnostics an iteration of the review
/// loop actually removed, and how many it created on the way.
/// </summary>
public readonly record struct DiagnosticDelta(int Resolved, int Introduced, int Persisting)
{
    public bool IsUnchanged => Resolved == 0 && Introduced == 0;
}
