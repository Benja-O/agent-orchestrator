namespace Orchestrator.Domain;

/// <summary>
/// Attributes a diagnostic to the layer whose agent has to fix it, and tells the gate which
/// directory to scope a query to.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the MCP contract deliberately has no <c>layer</c> field: mapping a path
/// to a layer is the orchestrator's concern, so the language server stays agnostic of the
/// project it analyses (ADR-010). This is that concern, and it lives in the domain because it
/// is the piece that gives the conditional edge of the graph somewhere to go.
/// </para>
/// <para>
/// <strong>A path that matches no layer is not ignored.</strong> See
/// <see cref="Attribute(System.Collections.Generic.IEnumerable{Diagnostic})"/> — silently
/// dropping an unattributable diagnostic is the false green arriving through the back door:
/// the code does not compile, there is nobody to send it back to, and the graph would advance
/// anyway.
/// </para>
/// <para>
/// The comparison is the one place in this project where a file identity is matched as a
/// string, so it normalises first. That is the lesson block 2 paid for: two spellings of the
/// same path read as two different files, and the file looks clean (AI.md, diagnostics).
/// </para>
/// </remarks>
public sealed class LayerMap
{
    private readonly IReadOnlyList<LayerDirectory> _directories;

    public LayerMap(IReadOnlyList<LayerDirectory> directories)
    {
        if (directories.Count == 0)
        {
            throw new ArgumentException("A layer map needs at least one directory.", nameof(directories));
        }

        _directories = directories;
    }

    /// <summary>
    /// The layout the orchestrator imposes on the generated workspace.
    /// </summary>
    /// <remarks>
    /// The generated application does not get to choose its own layout: the layer boundary is
    /// only enforceable — by this map today and by the <c>PreToolUse</c> hook of ADR-011 in
    /// block 4 — if the orchestrator fixes the directories up front.
    /// </remarks>
    public static LayerMap Default { get; } = new(
    [
        new LayerDirectory(Layer.Domain, "src/Domain"),
        new LayerDirectory(Layer.Api, "src/Api"),
        new LayerDirectory(Layer.Frontend, "src/Frontend"),
    ]);

    /// <summary>The relative path the gate scopes a query to when verifying <paramref name="layer"/>.</summary>
    public string ScopeOf(Layer layer)
    {
        foreach (var directory in _directories)
        {
            if (directory.Layer == layer)
            {
                return directory.RelativePath;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(layer), layer, "The layer map has no directory for this layer.");
    }

    public bool TryResolve(string relativeFilePath, out Layer layer)
    {
        var normalized = Normalize(relativeFilePath);

        foreach (var directory in _directories)
        {
            var prefix = Normalize(directory.RelativePath);

            if (normalized.StartsWith(prefix + '/', StringComparison.OrdinalIgnoreCase))
            {
                layer = directory.Layer;
                return true;
            }
        }

        layer = default;
        return false;
    }

    /// <summary>
    /// Groups diagnostics by the layer responsible for them, or fails if any of them belongs
    /// to no layer.
    /// </summary>
    /// <remarks>
    /// Failing is the whole point of the method. An unattributable diagnostic means the gate
    /// found something broken that no agent owns: the run cannot honestly continue, and the
    /// caller turns this into a terminal failure with the offending paths in the trace.
    /// </remarks>
    public Result<IReadOnlyDictionary<Layer, IReadOnlyList<Diagnostic>>> Attribute(IEnumerable<Diagnostic> diagnostics)
    {
        var grouped = new Dictionary<Layer, List<Diagnostic>>();
        var unattributable = new List<string>();

        foreach (var diagnostic in diagnostics)
        {
            if (!TryResolve(diagnostic.FilePath, out var layer))
            {
                if (!unattributable.Contains(diagnostic.FilePath, StringComparer.OrdinalIgnoreCase))
                {
                    unattributable.Add(diagnostic.FilePath);
                }

                continue;
            }

            if (!grouped.TryGetValue(layer, out var forLayer))
            {
                forLayer = [];
                grouped[layer] = forLayer;
            }

            forLayer.Add(diagnostic);
        }

        if (unattributable.Count > 0)
        {
            return Result<IReadOnlyDictionary<Layer, IReadOnlyList<Diagnostic>>>.Failure(
                "The gate reported diagnostics in files that belong to no layer, so there is no agent to send them back to: "
                + string.Join(", ", unattributable));
        }

        var result = grouped.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<Diagnostic>)entry.Value);
        return Result<IReadOnlyDictionary<Layer, IReadOnlyList<Diagnostic>>>.Success(result);
    }

    private static string Normalize(string relativePath)
    {
        var withForwardSlashes = relativePath.Replace('\\', '/');

        while (withForwardSlashes.StartsWith("./", StringComparison.Ordinal))
        {
            withForwardSlashes = withForwardSlashes[2..];
        }

        return withForwardSlashes.TrimStart('/').TrimEnd('/');
    }
}

/// <summary>One entry of a <see cref="LayerMap"/>: a layer and the directory it owns.</summary>
public readonly record struct LayerDirectory(Layer Layer, string RelativePath);
