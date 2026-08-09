using Orchestrator.LspServer.Protocol;

namespace Orchestrator.LspServer.Mapping;

/// <summary>The two useful halves of a hover: the signature and the prose under it.</summary>
public readonly record struct HoverSummary(string? Signature, string? Documentation);

/// <summary>
/// Pulls the signature out of a hover payload.
/// </summary>
/// <remarks>
/// The protocol has no <c>signature</c> field on <c>textDocument/definition</c>: a definition
/// is only a location. Both Roslyn and typescript-language-server put the declaration in a
/// fenced code block at the top of the hover markdown, so that block is the signature and
/// whatever follows is the documentation. Doing this here is what lets the API agent ask for
/// a shape instead of opening the file and guessing (ADR-004, function 2).
/// </remarks>
public static class HoverMapper
{
    public static HoverSummary Summarize(LspHover? hover)
    {
        var value = hover?.Contents?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HoverSummary(null, null);
        }

        var lines = value.Replace("\r\n", "\n").Split('\n');
        var signatureLines = new List<string>();
        var documentationLines = new List<string>();
        var insideCodeFence = false;
        var codeFenceClosed = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (insideCodeFence)
                {
                    insideCodeFence = false;
                    codeFenceClosed = true;
                }
                else if (!codeFenceClosed)
                {
                    insideCodeFence = true;
                }

                continue;
            }

            if (insideCodeFence)
            {
                signatureLines.Add(line);
            }
            else if (codeFenceClosed)
            {
                documentationLines.Add(line);
            }
        }

        // No fenced block at all: the whole hover is the best signature guess we have.
        if (signatureLines.Count == 0 && documentationLines.Count == 0)
        {
            return new HoverSummary(Collapse(lines), null);
        }

        return new HoverSummary(Collapse(signatureLines), Collapse(documentationLines));
    }

    private static string? Collapse(IReadOnlyList<string> lines)
    {
        var text = string.Join(' ', lines.Select(line => line.Trim()).Where(line => line.Length > 0));
        return text.Length == 0 ? null : UnescapeMarkdown(text);
    }

    /// <summary>
    /// Strips the backslashes markdown escaping adds in front of punctuation.
    /// </summary>
    /// <remarks>
    /// Roslyn escapes hover prose for a markdown renderer, so a summary comes back as
    /// <c>refusing while any prerequisite is open \(RN\-01\)</c>. The consumer here is a prompt,
    /// not a renderer, and those backslashes are noise inside an identifier the agent may copy.
    /// </remarks>
    private static string UnescapeMarkdown(string text)
    {
        var unescaped = new System.Text.StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            var isEscapedPunctuation =
                text[index] == '\\' &&
                index + 1 < text.Length &&
                !char.IsLetterOrDigit(text[index + 1]) &&
                !char.IsWhiteSpace(text[index + 1]);

            if (!isEscapedPunctuation)
            {
                unescaped.Append(text[index]);
            }
        }

        return unescaped.ToString();
    }
}
