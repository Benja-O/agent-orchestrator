using System.Text.Json;
using Orchestrator.Domain;

namespace Orchestrator.Agents;

/// <summary>Where the templates come from and where the generated application goes.</summary>
public sealed record WorkspaceLayout
{
    /// <summary>The repository's <c>templates/</c> directory.</summary>
    public required string TemplatesDirectory { get; init; }

    /// <summary>The generated application's root — <c>output/</c> in a normal run.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>The MCP endpoint written into the workspace's <c>.mcp.json</c>.</summary>
    public required string McpEndpoint { get; init; }
}

/// <summary>
/// Builds the workspace the layer agents will work in, from scratch, every run.
/// </summary>
/// <remarks>
/// <para>
/// From scratch is ADR-008 and it is load-bearing: a workspace carried over between runs makes
/// the pipeline look better than it is, because the second run starts from the first one's
/// output. It also means an orphaned language server holding handles on this directory is a bug
/// that stops the next run, which is why shutdown is deterministic in
/// <c>Orchestrator.Lsp.LspServerHost</c>.
/// </para>
/// </remarks>
public sealed class GeneratedWorkspacePreparer
{
    /// <summary>Directories the scaffold copy never carries over, however they got there.</summary>
    private static readonly string[] NeverCopied = ["node_modules", "bin", "obj"];

    private readonly WorkspaceLayout _layout;
    private readonly LayerMap _layerMap;

    public GeneratedWorkspacePreparer(WorkspaceLayout layout, LayerMap? layerMap = null)
    {
        _layout = layout;
        _layerMap = layerMap ?? LayerMap.Default;
    }

    public void Prepare(SpecDocument spec)
    {
        var root = Path.GetFullPath(_layout.WorkspaceRoot);

        RefuseToTouchAnythingThatIsNotAnOutputDirectory(root);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);

        foreach (var layer in LayerCatalog.InPipelineOrder)
        {
            Directory.CreateDirectory(Path.Combine(root, _layerMap.ScopeOf(layer).Replace('/', Path.DirectorySeparatorChar)));
        }

        CopyScaffold(root);
        CopyAgentDefinitions(root);
        CopyHookScript(root);
        CopyGeneratedApplicationInstructions(root);
        WriteSpec(root, spec);
        WriteMcpConfiguration(root);
        WriteSettings(root);
    }

    /// <summary>
    /// The build files of the generated application: the solution, the two project files and the
    /// frontend's <c>package.json</c> and <c>tsconfig.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The orchestrator writes these, never an agent (ADR-016).</strong> Roslyn opens a
    /// solution, not a folder of loose files, so without them the C# language server loads
    /// nothing and reports nothing — and a gate that is analysing nothing looks exactly like a
    /// gate that found clean code. That is the false green arriving through the one door neither
    /// ADR-006 nor block 2 had covered, and it is the reason this cannot sit on the
    /// non-deterministic side of the pipeline.
    /// </para>
    /// <para>
    /// They are files under <c>templates/scaffold/</c> rather than strings in this class, for the
    /// same reason the agent definitions and the hook are: what a run puts in the workspace
    /// should be readable without reading C#.
    /// </para>
    /// </remarks>
    private void CopyScaffold(string root)
    {
        var source = Path.Combine(_layout.TemplatesDirectory, "scaffold");
        RequireDirectory(source, "the generated application's scaffold");

        CopyDirectory(source, root);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);

            if (NeverCopied.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }

    /// <summary>
    /// The guard on a recursive delete.
    /// </summary>
    /// <remarks>
    /// <c>output/</c> is disposable by construction, but this method is one configuration
    /// mistake away from deleting a source tree, and a run that eats the repository is not a bug
    /// anybody gets to fix twice.
    /// </remarks>
    private static void RefuseToTouchAnythingThatIsNotAnOutputDirectory(string root)
    {
        if (Directory.Exists(Path.Combine(root, ".git")))
        {
            throw new AgentRunnerException(
                $"'{root}' is a git repository, so it is not the generated workspace. Refusing to delete it.");
        }

        if (Path.GetPathRoot(root) is { } pathRoot && string.Equals(
                Path.TrimEndingDirectorySeparator(root),
                Path.TrimEndingDirectorySeparator(pathRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentRunnerException($"'{root}' is a drive root. Refusing to delete it.");
        }
    }

    private void CopyAgentDefinitions(string root)
    {
        var source = Path.Combine(_layout.TemplatesDirectory, "agents");
        RequireDirectory(source, "the agent definitions");

        var destination = Path.Combine(root, ".claude", "agents");
        Directory.CreateDirectory(destination);

        foreach (var definition in Directory.EnumerateFiles(source, "*.md"))
        {
            File.Copy(definition, Path.Combine(destination, Path.GetFileName(definition)), overwrite: true);
        }
    }

    private void CopyHookScript(string root)
    {
        var source = Path.Combine(_layout.TemplatesDirectory, "hooks", "restrict-to-layer.js");
        RequireFile(source, "the file-scope hook");

        var destination = Path.Combine(root, ".claude", "hooks");
        Directory.CreateDirectory(destination);
        File.Copy(source, Path.Combine(destination, "restrict-to-layer.js"), overwrite: true);
    }

    private void CopyGeneratedApplicationInstructions(string root)
    {
        var source = Path.Combine(_layout.TemplatesDirectory, "generated-app-CLAUDE.md");
        RequireFile(source, "the generated application's CLAUDE.md");

        File.Copy(source, Path.Combine(root, "CLAUDE.md"), overwrite: true);
    }

    /// <summary>The spec travels into the workspace, because the generated app's CLAUDE.md points every agent at it.</summary>
    private static void WriteSpec(string root, SpecDocument spec) =>
        File.WriteAllText(Path.Combine(root, "spec.md"), spec.Text);

    /// <summary>
    /// Declares the server for anything that reads the workspace, such as a person opening it
    /// interactively to debug a run.
    /// </summary>
    /// <remarks>
    /// The runner does not depend on this file — it passes <c>--mcp-config</c> on every
    /// invocation — and that redundancy is deliberate. A project-scope <c>.mcp.json</c> alone is
    /// exactly what risk R5 turned out to be: it waits for an approval headless cannot give, and
    /// says nothing when it does not get one.
    /// </remarks>
    private void WriteMcpConfiguration(string root)
    {
        var configuration = JsonSerializer.Serialize(
            new
            {
                mcpServers = new Dictionary<string, object>
                {
                    ["lsp"] = new { type = "http", url = _layout.McpEndpoint, timeout = 120000 },
                },
            },
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(Path.Combine(root, ".mcp.json"), configuration + Environment.NewLine);
    }

    private static void WriteSettings(string root)
    {
        var settings = JsonSerializer.Serialize(
            new { enabledMcpjsonServers = new[] { "lsp" } },
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(Path.Combine(root, ".claude", "settings.json"), settings + Environment.NewLine);
    }

    private static void RequireDirectory(string path, string what)
    {
        if (!Directory.Exists(path))
        {
            throw new AgentRunnerException($"Cannot prepare the workspace: {what} are missing at '{path}'.");
        }
    }

    private static void RequireFile(string path, string what)
    {
        if (!File.Exists(path))
        {
            throw new AgentRunnerException($"Cannot prepare the workspace: {what} is missing at '{path}'.");
        }
    }
}
