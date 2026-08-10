using System.Text.Json;
using Orchestrator.Domain;

namespace Orchestrator.Lsp;

/// <summary>
/// Turns one <c>diagnostics</c> response of the MCP contract into the <see cref="GateVerdict"/>
/// the graph reasons about.
/// </summary>
/// <remarks>
/// <para>
/// A pure function with no process, no socket and no clock behind it, which is the whole reason
/// it is not folded into <see cref="McpLanguageServerGateway"/>: this is where the contract's
/// wire shape stops and the domain begins, so it is the part worth testing exhaustively — and
/// it can be, against responses recorded from the real server (ADR-014).
/// </para>
/// <para>
/// <strong>Every unreadable answer throws instead of degrading.</strong> An unknown
/// <c>status</c>, a missing field or an unrecognised severity are all contract drift, and the
/// tempting fallbacks — treat it as ready, drop the item, call it a hint — each produce the one
/// failure this project exists to prevent: a gate that approves code that does not compile.
/// </para>
/// </remarks>
internal static class DiagnosticTranslation
{
    private const string ReadyStatus = "ready";
    private const string IndexingStatus = "indexing";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static GateVerdict FromJson(string json)
    {
        DiagnosticsPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<DiagnosticsPayload>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new LspGatewayException(
                $"The LSP server answered something that is not a diagnostics response: {Excerpt(json)}",
                exception);
        }

        if (payload is null)
        {
            throw new LspGatewayException($"The LSP server answered an empty diagnostics response: {Excerpt(json)}");
        }

        return Translate(payload, json);
    }

    private static GateVerdict Translate(DiagnosticsPayload payload, string json)
    {
        var items = payload.Items.Select(item => TranslateItem(item, json)).ToList();

        var set = new DiagnosticSet
        {
            Items = items,
            Total = payload.Total,
            Truncated = payload.Truncated,
        };

        return payload.Status switch
        {
            ReadyStatus => GateVerdict.Ready(set),

            // The partial list travels with it, and the graph is expected to ignore it. Dropping
            // it here would be tidier and would cost the log the only clue about what the server
            // had found so far when it was still indexing.
            IndexingStatus => GateVerdict.Indexing(
                payload.StatusDetail ?? "The LSP server is still indexing and gave no detail.",
                set),

            null => throw new LspGatewayException(
                $"The LSP server answered a diagnostics response with no status: {Excerpt(json)}"),

            _ => throw new LspGatewayException(
                $"The LSP server answered the unknown status '{payload.Status}'. "
                + $"Only '{ReadyStatus}' and '{IndexingStatus}' are part of the contract, and guessing which one this "
                + "resembles is how a gate ends up approving code that does not compile."),
        };
    }

    private static Diagnostic TranslateItem(DiagnosticItemPayload item, string json)
    {
        if (string.IsNullOrWhiteSpace(item.FilePath))
        {
            throw new LspGatewayException(
                $"The LSP server returned a diagnostic with no file path, so no layer could own it: {Excerpt(json)}");
        }

        if (item.Range is null)
        {
            throw new LspGatewayException(
                $"The LSP server returned a diagnostic with no range for '{item.FilePath}'.");
        }

        return new Diagnostic
        {
            FilePath = item.FilePath,
            Range = new SourceRange(
                item.Range.StartLine,
                item.Range.StartColumn,
                item.Range.EndLine,
                item.Range.EndColumn),
            Severity = TranslateSeverity(item.Severity, item.FilePath),
            Code = item.Code ?? string.Empty,
            Message = item.Message ?? string.Empty,
            Source = item.Source ?? string.Empty,
        };
    }

    private static DiagnosticSeverity TranslateSeverity(string? severity, string filePath) => severity switch
    {
        "error" => DiagnosticSeverity.Error,
        "warning" => DiagnosticSeverity.Warning,
        "information" => DiagnosticSeverity.Information,
        "hint" => DiagnosticSeverity.Hint,

        // Not defaulting to a harmless severity on purpose: if the contract ever grows a value
        // this adapter does not know, the safe reading is not "probably a hint" — a mislabelled
        // error is an error the gate would wave through.
        _ => throw new LspGatewayException(
            $"The LSP server reported the unknown severity '{severity}' on '{filePath}'."),
    };

    private static string Excerpt(string json) =>
        json.Length <= 400 ? json : string.Concat(json.AsSpan(0, 400), "…");
}
