using Orchestrator.LspServer.Contract;
using Orchestrator.LspServer.Protocol;

namespace Orchestrator.LspServer.Mapping;

/// <summary>Protocol symbols to contract symbols, keeping the outline nesting of a file and flattening a workspace search.</summary>
public static class SymbolMapper
{
    public static SymbolItem ToSymbolItem(LspDocumentSymbol symbol) => new()
    {
        Name = symbol.Name,
        Kind = LspSymbolKinds.ToName(symbol.Kind),
        Range = DiagnosticMapper.ToSourceRange(symbol.Range),
        Signature = string.IsNullOrWhiteSpace(symbol.Detail) ? null : symbol.Detail,
        Children = symbol.Children.Select(ToSymbolItem).ToList(),
    };

    public static SymbolItem ToSymbolItem(LspSymbolInformation symbol, string relativeFilePath) => new()
    {
        Name = string.IsNullOrWhiteSpace(symbol.ContainerName)
            ? symbol.Name
            : $"{symbol.ContainerName}.{symbol.Name}",
        Kind = LspSymbolKinds.ToName(symbol.Kind),
        Range = DiagnosticMapper.ToSourceRange(symbol.Location.Range),
        FilePath = relativeFilePath,
    };
}
