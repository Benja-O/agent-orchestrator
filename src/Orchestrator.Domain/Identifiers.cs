namespace Orchestrator.Domain;

/// <summary>Identifies one node of the graph. Stable, because the log is keyed on it.</summary>
public readonly record struct NodeId(string Value)
{
    public override string ToString() => Value;

    public static NodeId SpecAnalysis { get; } = new("spec-analysis");

    public static NodeId Completed { get; } = new("completed");

    public static NodeId Failed { get; } = new("failed");

    public static NodeId ImplementationOf(Layer layer) => new($"{LayerCatalog.AgentNameOf(layer)}-implementation");

    public static NodeId GateOf(Layer layer) => new($"{LayerCatalog.AgentNameOf(layer)}-gate");
}

/// <summary>Identifies one run of the pipeline. Every log line carries it.</summary>
public readonly record struct RunId(string Value)
{
    public override string ToString() => Value;

    public static RunId NewRunId() => new(Guid.NewGuid().ToString("n")[..12]);
}
