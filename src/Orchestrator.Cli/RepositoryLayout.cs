namespace Orchestrator.Cli;

/// <summary>
/// Finds the repository resources a run needs: the templates and the built MCP server.
/// </summary>
/// <remarks>
/// <para>
/// It walks up from the executable rather than resolving paths next to it, because the MCP
/// server is launched as <c>dotnet Orchestrator.LspServer.dll</c> and needs its own
/// <c>runtimeconfig.json</c> beside it — copying the assembly here would produce a file that
/// cannot be run.
/// </para>
/// <para>
/// <strong>The consequence, said plainly: this CLI runs from the repository, not from an
/// install.</strong> That is fine for what it is — the host of a challenge project — and it is
/// recorded as debt D14 rather than left for someone to discover.
/// </para>
/// </remarks>
public sealed class RepositoryLayout
{
    private RepositoryLayout(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string TemplatesDirectory => Path.Combine(Root, "templates");

    public string LspServerAssemblyPath => Path.Combine(
        Root, "src", "Orchestrator.LspServer", "bin", Configuration, "net10.0", "Orchestrator.LspServer.dll");

    private static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>Walks up from the executable looking for the directory that holds <c>templates/</c>.</summary>
    public static RepositoryLayout Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "templates")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not find the repository root: no ancestor of "
                + $"'{AppContext.BaseDirectory}' contains a 'templates' directory.");
        }

        return new RepositoryLayout(directory.FullName);
    }

    /// <summary>
    /// Fails before the run starts if the MCP server has not been built.
    /// </summary>
    /// <remarks>
    /// AI.md asks for failing fast at startup, and this is the cheapest instance of it: without
    /// the server there is no gate, and without a gate the pipeline degrades into blind
    /// generation — which is the one failure this project exists to prevent.
    /// </remarks>
    public void RequireLspServerBuilt()
    {
        if (!File.Exists(LspServerAssemblyPath))
        {
            throw new InvalidOperationException(
                $"The MCP server has not been built: '{LspServerAssemblyPath}' does not exist. "
                + "Run 'dotnet build src/Orchestrator.slnx' first.");
        }
    }
}
