namespace Orchestrator.LspServer.Tests;

/// <summary>A throwaway directory with real files, so path handling is exercised for real.</summary>
/// <remarks>
/// Real files, no language server. The query layer genuinely touches the filesystem — it
/// resolves scopes, refuses paths outside the workspace and reads source lines for reference
/// previews — and faking that away would test something other than the code that ships.
/// </remarks>
public sealed class TemporaryWorkspace : IDisposable
{
    public TemporaryWorkspace()
    {
        RootFullPath = Path.Combine(Path.GetTempPath(), "orchestrator-lsp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootFullPath);
    }

    public string RootFullPath { get; }

    public string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(RootFullPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootFullPath, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
