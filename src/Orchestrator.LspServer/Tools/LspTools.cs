using System.ComponentModel;
using ModelContextProtocol.Server;
using Orchestrator.LspServer.Contract;

namespace Orchestrator.LspServer.Tools;

/// <summary>
/// The MCP surface: the five tools of docs/mcp-contract.md, and nothing else.
/// </summary>
/// <remarks>
/// Four of the five are navigation. That is deliberate and it is the criterion ADR-004 set
/// against itself: if this layer had ended up exposing only <c>diagnostics</c>, it would be an
/// expensive <c>dotnet build</c> and the decision would not hold. The descriptions are part of
/// the contract too — they are what a language model reads to decide whether to call a tool.
/// </remarks>
[McpServerToolType]
public static class LspTools
{
    [McpServerTool(Name = "diagnostics")]
    [Description(
        "Compiler diagnostics for a scope of the workspace. Returns status 'ready' or 'indexing'. " +
        "'ready' with an empty item list means there are no errors; 'indexing' means the language " +
        "server has not finished loading and the result is NOT a verdict — wait and ask again. " +
        "Items are ordered by severity, then file, then line, and truncated from the end.")]
    public static Task<DiagnosticsResponse> DiagnosticsAsync(
        LspQueryService queryService,
        [Description("Path relative to the workspace root: a file, a directory, or '.' for the whole workspace.")]
        string scope,
        CancellationToken cancellationToken) =>
        queryService.GetDiagnosticsAsync(scope, cancellationToken);

    [McpServerTool(Name = "definition")]
    [Description(
        "Where the symbol at a position is defined, and with what signature. Use it to ask what " +
        "shape a method or type has instead of assuming it.")]
    public static Task<DefinitionResponse> DefinitionAsync(
        LspQueryService queryService,
        [Description("Path relative to the workspace root.")] string filePath,
        [Description("1-based line number.")] int line,
        [Description("1-based column number.")] int column,
        CancellationToken cancellationToken) =>
        queryService.GetDefinitionAsync(filePath, line, column, cancellationToken);

    [McpServerTool(Name = "references")]
    [Description(
        "Every place the symbol at a position is used, with the source line of each. Use it before " +
        "renaming anything or changing a signature.")]
    public static Task<ReferencesResponse> ReferencesAsync(
        LspQueryService queryService,
        [Description("Path relative to the workspace root.")] string filePath,
        [Description("1-based line number.")] int line,
        [Description("1-based column number.")] int column,
        CancellationToken cancellationToken) =>
        queryService.GetReferencesAsync(filePath, line, column, cancellationToken);

    [McpServerTool(Name = "documentSymbol")]
    [Description("The outline of a file — its types and their members — without reading the file.")]
    public static Task<SymbolsResponse> DocumentSymbolAsync(
        LspQueryService queryService,
        [Description("Path relative to the workspace root.")] string filePath,
        CancellationToken cancellationToken) =>
        queryService.GetDocumentSymbolsAsync(filePath, cancellationToken);

    [McpServerTool(Name = "workspaceSymbol")]
    [Description("Find a symbol by name anywhere in the workspace, without knowing which file holds it.")]
    public static Task<SymbolsResponse> WorkspaceSymbolAsync(
        LspQueryService queryService,
        [Description("Name or partial name of the symbol.")] string query,
        CancellationToken cancellationToken) =>
        queryService.GetWorkspaceSymbolsAsync(query, cancellationToken);
}
