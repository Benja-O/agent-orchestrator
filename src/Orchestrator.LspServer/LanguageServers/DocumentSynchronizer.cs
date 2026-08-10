namespace Orchestrator.LspServer.LanguageServers;

/// <summary>What has to be sent to the language server before it can be asked about a document.</summary>
public enum DocumentSyncAction
{
    /// <summary>The server already holds this exact text.</summary>
    Nothing = 0,

    /// <summary>First time: <c>textDocument/didOpen</c>.</summary>
    Open = 1,

    /// <summary>The file changed underneath it: <c>textDocument/didChange</c>.</summary>
    Change = 2,
}

/// <summary>A 0-based position, as the LSP protocol counts them.</summary>
public readonly record struct TextPosition(int Line, int Character);

/// <summary>
/// The decision: what to send, with which version, and — for a change — how far the text being
/// replaced extended.
/// </summary>
/// <remarks>
/// <see cref="EndOfPreviousText"/> is what turns a whole-document rewrite into an edit spanning
/// the entire previous document. It exists because Roslyn does not accept the range-less form of
/// a content change (see <c>LspTextDocumentContentChangeEvent</c>).
/// </remarks>
public readonly record struct DocumentSyncDecision(
    DocumentSyncAction Action,
    int Version,
    TextPosition EndOfPreviousText);

/// <summary>
/// Tracks what the language server currently believes each document contains.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="LanguageServerSession"/> so this can be exercised without a real
/// language server behind it (AI.md, golden rule 3). It earned its own type by being wrong: the
/// first version opened every document once and never spoke about it again, which is correct for
/// anything that reads a file a single time and silently broken for a review loop.
/// </para>
/// <para>
/// <strong>The failure it caused is worth naming, because it is the inverse of the one this whole
/// layer exists to prevent.</strong> An LSP server answers about the text it was given, not about
/// the file: with no change notification, the agent fixes the code and the gate keeps reporting
/// the error it saw the first time. Not a false green — a <em>false red</em>. It fails safe, and
/// it still lies to the graph and spends a paid turn re-fixing what was already fixed.
/// </para>
/// </remarks>
public sealed class DocumentSynchronizer
{
    private readonly Dictionary<string, Entry> _documents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records the new state and says what the server has to be told.</summary>
    public DocumentSyncDecision Reconcile(string documentFullPath, string text)
    {
        if (!_documents.TryGetValue(documentFullPath, out var known))
        {
            _documents[documentFullPath] = new Entry(Version: 1, Text: text);
            return new DocumentSyncDecision(DocumentSyncAction.Open, 1, default);
        }

        if (string.Equals(known.Text, text, StringComparison.Ordinal))
        {
            return new DocumentSyncDecision(DocumentSyncAction.Nothing, known.Version, default);
        }

        // Monotonic per document, as the protocol requires: a version that does not move is a
        // change the server is entitled to ignore.
        var nextVersion = known.Version + 1;
        var endOfPrevious = EndOf(known.Text);
        _documents[documentFullPath] = new Entry(nextVersion, text);

        return new DocumentSyncDecision(DocumentSyncAction.Change, nextVersion, endOfPrevious);
    }

    /// <summary>
    /// The position just past the last character of <paramref name="text"/>, 0-based.
    /// </summary>
    /// <remarks>
    /// Line breaks are counted by <c>\n</c> alone, which handles both endings: in a CRLF file the
    /// <c>\r</c> belongs to the line before the break, so it never lands on the last line. A file
    /// ending in a newline therefore ends at character 0 of an empty final line, which is what
    /// every editor also reports.
    /// </remarks>
    public static TextPosition EndOf(string text)
    {
        var line = 0;
        var lastBreakIndex = -1;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lastBreakIndex = index;
            }
        }

        return new TextPosition(line, text.Length - lastBreakIndex - 1);
    }

    private sealed record Entry(int Version, string Text);
}
