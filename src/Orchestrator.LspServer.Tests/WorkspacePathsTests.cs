using Orchestrator.LspServer.Workspace;

namespace Orchestrator.LspServer.Tests;

public sealed class WorkspacePathsTests : IDisposable
{
    private readonly TemporaryWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void ToRelativePath_uses_forward_slashes_so_the_contract_reads_the_same_everywhere()
    {
        var paths = new WorkspacePaths(_workspace.RootFullPath);
        var fullPath = Path.Combine(_workspace.RootFullPath, "src", "Domain", "Tarea.cs");

        Assert.Equal("src/Domain/Tarea.cs", paths.ToRelativePath(fullPath));
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("../../etc/passwd")]
    [InlineData("src/../../outside.cs")]
    public void TryResolveFullPath_refuses_anything_that_escapes_the_workspace(string candidate)
    {
        var paths = new WorkspacePaths(_workspace.RootFullPath);

        Assert.False(paths.TryResolveFullPath(candidate, out _));
    }

    [Fact]
    public void TryResolveFullPath_accepts_the_root_itself_and_paths_below_it()
    {
        var paths = new WorkspacePaths(_workspace.RootFullPath);

        Assert.True(paths.TryResolveFullPath(".", out _));
        Assert.True(paths.TryResolveFullPath("src/Domain/Tarea.cs", out _));
    }

    /// <summary>
    /// The two spellings language servers actually use for the same Windows file.
    /// </summary>
    /// <remarks>
    /// We send <c>file:///F:/…</c>; typescript-language-server answers about
    /// <c>file:///f%3A/…</c>. Treating those as different files files a document's diagnostics
    /// under a key nobody reads, and the file then looks clean — a false green produced by path
    /// normalisation rather than by timing. This is the regression test for a bug that actually
    /// happened in Block 2.
    /// </remarks>
    [Fact]
    public void FromUri_normalises_both_spellings_of_a_windows_path_to_the_same_place()
    {
        var upperCaseDrive = WorkspacePaths.FromUri("file:///F:/project/src/tarea.ts");
        var escapedLowerCaseDrive = WorkspacePaths.FromUri("file:///f%3A/project/src/tarea.ts");

        Assert.Equal(upperCaseDrive, escapedLowerCaseDrive, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromUri_round_trips_a_path_produced_by_ToUri()
    {
        var fullPath = Path.Combine(_workspace.RootFullPath, "src", "Tarea.cs");

        Assert.Equal(fullPath, WorkspacePaths.FromUri(WorkspacePaths.ToUri(fullPath)), StringComparer.OrdinalIgnoreCase);
    }
}
