using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.LspServer.Protocol;

/// <summary>
/// The subset of the Language Server Protocol this server actually speaks, typed by hand.
/// </summary>
/// <remarks>
/// Hand-typed rather than taken from a protocol library: the message set is small (initialize,
/// pull diagnostics, definition, references, hover, symbols) and Roslyn adds custom methods
/// that no general-purpose library models anyway. See ADR-013.
/// <para><b>Everything here is 0-based</b>, as the protocol defines it. The conversion to the
/// 1-based contract happens in the mapping layer and nowhere else.</para>
/// </remarks>
public sealed record LspPosition
{
    public int Line { get; init; }

    public int Character { get; init; }
}

public sealed record LspRange
{
    public LspPosition Start { get; init; } = new();

    public LspPosition End { get; init; } = new();
}

public sealed record LspLocation
{
    public string Uri { get; init; } = string.Empty;

    public LspRange Range { get; init; } = new();
}

/// <summary>The alternative shape a server may return from <c>textDocument/definition</c>.</summary>
public sealed record LspLocationLink
{
    public string TargetUri { get; init; } = string.Empty;

    public LspRange TargetRange { get; init; } = new();

    public LspRange TargetSelectionRange { get; init; } = new();
}

public sealed record LspTextDocumentIdentifier
{
    public required string Uri { get; init; }
}

public sealed record LspTextDocumentItem
{
    public required string Uri { get; init; }

    public required string LanguageId { get; init; }

    public required int Version { get; init; }

    public required string Text { get; init; }
}

public sealed record LspDidOpenTextDocumentParams
{
    public required LspTextDocumentItem TextDocument { get; init; }
}

public sealed record LspVersionedTextDocumentIdentifier
{
    public required string Uri { get; init; }

    /// <summary>Has to increase on every change, or the server keeps the text it already had.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// One edit to a document: <see cref="Text"/> replaces whatever <see cref="Range"/> covers.
/// </summary>
/// <remarks>
/// The protocol also allows a range-less form meaning "this is the whole new document", and
/// <strong>Roslyn does not implement it</strong>: its <c>DidChangeHandler</c> dereferences the
/// range unconditionally and throws a <c>NullReferenceException</c> inside its request queue —
/// after which it stops answering anything, silently, forever. So the range is required here even
/// though the specification says otherwise, and a whole-document replacement is expressed as an
/// edit spanning the whole of the previous text (block 4).
/// </remarks>
public sealed record LspTextDocumentContentChangeEvent
{
    public required LspRange Range { get; init; }

    public required string Text { get; init; }
}

public sealed record LspDidChangeTextDocumentParams
{
    public required LspVersionedTextDocumentIdentifier TextDocument { get; init; }

    public required IReadOnlyList<LspTextDocumentContentChangeEvent> ContentChanges { get; init; }
}

/// <summary>The <c>FileChangeType</c> values of the protocol.</summary>
public static class LspFileChangeType
{
    public const int Created = 1;
    public const int Changed = 2;
    public const int Deleted = 3;
}

public sealed record LspFileEvent
{
    public required string Uri { get; init; }

    /// <summary>One of <see cref="LspFileChangeType"/>.</summary>
    public required int Type { get; init; }
}

/// <summary>
/// Tells the server that files appeared, changed or vanished on disk.
/// </summary>
/// <remarks>
/// Distinct from <c>textDocument/didOpen</c>, and the difference is the one that matters for a
/// pipeline whose agents write new files: <c>didOpen</c> hands the server a buffer, while this is
/// what makes a project system <em>include</em> the file in the compilation. Without it a file
/// created after the solution was loaded is analysed detached from its project, or not at all —
/// and a file nobody analyses reports no errors, which the gate reads as clean (block 4).
/// </remarks>
public sealed record LspDidChangeWatchedFilesParams
{
    public required IReadOnlyList<LspFileEvent> Changes { get; init; }
}

public sealed record LspTextDocumentPositionParams
{
    public required LspTextDocumentIdentifier TextDocument { get; init; }

    public required LspPosition Position { get; init; }
}

public sealed record LspReferenceContext
{
    public bool IncludeDeclaration { get; init; } = true;
}

public sealed record LspReferenceParams
{
    public required LspTextDocumentIdentifier TextDocument { get; init; }

    public required LspPosition Position { get; init; }

    public LspReferenceContext Context { get; init; } = new();
}

public sealed record LspDocumentDiagnosticParams
{
    public required LspTextDocumentIdentifier TextDocument { get; init; }

    public string? Identifier { get; init; }

    public string? PreviousResultId { get; init; }
}

public sealed record LspDocumentDiagnosticReport
{
    /// <summary><c>full</c> or <c>unchanged</c>.</summary>
    public string Kind { get; init; } = "full";

    public string? ResultId { get; init; }

    public IReadOnlyList<LspDiagnostic> Items { get; init; } = [];
}

public sealed record LspDiagnostic
{
    public LspRange Range { get; init; } = new();

    /// <summary>1 = error, 2 = warning, 3 = information, 4 = hint. Absent means the server did not classify it.</summary>
    public int? Severity { get; init; }

    /// <summary>The protocol allows either a number or a string here, so it stays raw until the mapper stringifies it.</summary>
    public JsonElement? Code { get; init; }

    public string? Source { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed record LspMarkupContent
{
    public string Kind { get; init; } = "markdown";

    public string Value { get; init; } = string.Empty;
}

public sealed record LspHover
{
    public LspMarkupContent? Contents { get; init; }

    public LspRange? Range { get; init; }
}

public sealed record LspDocumentSymbolParams
{
    public required LspTextDocumentIdentifier TextDocument { get; init; }
}

public sealed record LspDocumentSymbol
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Roslyn puts the signature-ish text here; it is what feeds the contract's <c>signature</c>.</summary>
    public string? Detail { get; init; }

    public int Kind { get; init; }

    public LspRange Range { get; init; } = new();

    public LspRange? SelectionRange { get; init; }

    public IReadOnlyList<LspDocumentSymbol> Children { get; init; } = [];
}

public sealed record LspWorkspaceSymbolParams
{
    public required string Query { get; init; }
}

/// <summary>
/// The flat shape <c>workspace/symbol</c> returns. Modelled as <c>SymbolInformation</c>,
/// which every server still supports, rather than the newer <c>WorkspaceSymbol</c> whose
/// location may be a bare uri needing a resolve round trip.
/// </summary>
public sealed record LspSymbolInformation
{
    public string Name { get; init; } = string.Empty;

    public int Kind { get; init; }

    public string? ContainerName { get; init; }

    public LspLocation Location { get; init; } = new();
}

public sealed record LspConfigurationItem
{
    public string? ScopeUri { get; init; }

    public string? Section { get; init; }
}

public sealed record LspConfigurationParams
{
    public IReadOnlyList<LspConfigurationItem> Items { get; init; } = [];
}

/// <summary>
/// The Roslyn-specific notification that says the solution finished loading. This is the
/// signal the <c>status</c> field of the contract is derived from — the alternative would be
/// sleeping for an arbitrary interval and hoping, which is exactly how a gate ends up
/// approving code that does not compile.
/// </summary>
public static class LspMethodNames
{
    public const string Initialize = "initialize";
    public const string Initialized = "initialized";
    public const string Shutdown = "shutdown";
    public const string Exit = "exit";

    public const string DidOpenTextDocument = "textDocument/didOpen";
    public const string DidChangeTextDocument = "textDocument/didChange";
    public const string DidChangeWatchedFiles = "workspace/didChangeWatchedFiles";
    public const string DidCloseTextDocument = "textDocument/didClose";
    public const string DocumentDiagnostic = "textDocument/diagnostic";
    public const string Definition = "textDocument/definition";
    public const string References = "textDocument/references";
    public const string Hover = "textDocument/hover";
    public const string DocumentSymbol = "textDocument/documentSymbol";
    public const string WorkspaceSymbol = "workspace/symbol";
    public const string PublishDiagnostics = "textDocument/publishDiagnostics";

    public const string RoslynOpenSolution = "solution/open";
    public const string RoslynOpenProject = "project/open";
    public const string RoslynProjectInitializationComplete = "workspace/projectInitializationComplete";
}

/// <summary>LSP <c>SymbolKind</c>, whose wire form is a number.</summary>
public static class LspSymbolKinds
{
    private static readonly string[] Names =
    [
        "file", "module", "namespace", "package", "class", "method", "property", "field",
        "constructor", "enum", "interface", "function", "variable", "constant", "string",
        "number", "boolean", "array", "object", "key", "null", "enumMember", "struct",
        "event", "operator", "typeParameter",
    ];

    public static string ToName(int kind) =>
        kind >= 1 && kind <= Names.Length ? Names[kind - 1] : "unknown";
}

/// <summary>Converters that keep the wire form of every LSP message camelCased.</summary>
public static class LspJson
{
    public static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
