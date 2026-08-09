namespace Orchestrator.Domain;

/// <summary>
/// The three layers of the generated application, in the order the pipeline builds them.
/// </summary>
/// <remarks>
/// The order is load-bearing, not cosmetic: the API is written against a domain that already
/// exists and the frontend against an API that already exists (ADR-011). The numeric values
/// are the pipeline order.
/// </remarks>
public enum Layer
{
    Domain = 1,
    Api = 2,
    Frontend = 3,
}

/// <summary>
/// Maps a layer to the subagent that owns it and to the directory it is allowed to write in.
/// </summary>
/// <remarks>
/// The agent names have to match the <c>name</c> in the frontmatter of the templates under
/// <c>templates/agents/</c> — that string is what <c>claude -p</c> is asked to dispatch to.
/// </remarks>
public static class LayerCatalog
{
    public static readonly IReadOnlyList<Layer> InPipelineOrder = [Layer.Domain, Layer.Api, Layer.Frontend];

    /// <summary>Position of a layer in <see cref="InPipelineOrder"/>.</summary>
    public static int PipelineIndexOf(Layer layer)
    {
        for (var index = 0; index < InPipelineOrder.Count; index++)
        {
            if (InPipelineOrder[index] == layer)
            {
                return index;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown layer.");
    }

    public static string AgentNameOf(Layer layer) => layer switch
    {
        Layer.Domain => "domain",
        Layer.Api => "api",
        Layer.Frontend => "frontend",
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown layer."),
    };

    /// <summary>The name used in the plan produced by the spec analyzer, which is written in Spanish.</summary>
    public static string PlanNameOf(Layer layer) => layer switch
    {
        Layer.Domain => "dominio",
        Layer.Api => "api",
        Layer.Frontend => "frontend",
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown layer."),
    };

    public static bool TryParsePlanName(string planName, out Layer layer)
    {
        foreach (var candidate in InPipelineOrder)
        {
            if (string.Equals(PlanNameOf(candidate), planName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                layer = candidate;
                return true;
            }
        }

        layer = default;
        return false;
    }
}
