using Orchestrator.Domain;

namespace Orchestrator.TestSupport;

/// <summary>
/// The state of a make-believe generated application: which layer is broken, and how.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the piece that keeps the suite honest.</strong> The obvious way to test a
/// review loop is to script the agent and the gate separately — the agent returns a sequence
/// of answers, the gate returns a sequence of verdicts — and it is a trap: the two scripts can
/// contradict each other, so a test can pass while describing a run that could never happen.
/// </para>
/// <para>
/// Instead both fakes share this object. <see cref="FakeAgentRunner"/> mutates it the way a
/// real agent would mutate the workspace, and <see cref="FakeLanguageServer"/> reports what is
/// in it. The graph converges because the agent repaired something, not because a script said
/// the next verdict was clean — and "the agent changed nothing" stops being a scripted verdict
/// and becomes the literal definition of the non-progress test (ADR-014).
/// </para>
/// </remarks>
public sealed class FakeWorkspace
{
    private readonly Dictionary<Layer, List<Diagnostic>> _diagnosticsByLayer =
        LayerCatalog.InPipelineOrder.ToDictionary(layer => layer, _ => new List<Diagnostic>());

    /// <summary>How many more times the gate will answer <c>indexing</c> before it settles.</summary>
    public int IndexingAnswersRemaining { get; set; }

    /// <summary>What the server says it is waiting for while indexing.</summary>
    public string IndexingDetail { get; set; } = "Roslyn is loading the solution 'App.slnx'";

    /// <summary>
    /// When true an <c>indexing</c> answer carries the diagnostics it happens to know about,
    /// instead of the empty list a loading server normally returns. The other half-truth a
    /// consumer must not mistake for a verdict.
    /// </summary>
    public bool IndexingReportsPartialItems { get; set; }

    /// <summary>When true the gate reports the list as cut, without changing what it returns.</summary>
    public bool ReportTruncated { get; set; }

    /// <summary>The <c>total</c> the gate reports, when it should be larger than what it returns.</summary>
    public int? TotalOverride { get; set; }

    /// <summary>Diagnostics that belong to no layer at all, to exercise the unattributable case.</summary>
    public List<Diagnostic> OrphanDiagnostics { get; } = [];

    public FakeWorkspace Break(Layer layer, params Diagnostic[] diagnostics)
    {
        _diagnosticsByLayer[layer].AddRange(diagnostics);
        return this;
    }

    public FakeWorkspace Replace(Layer layer, params Diagnostic[] diagnostics)
    {
        _diagnosticsByLayer[layer].Clear();
        _diagnosticsByLayer[layer].AddRange(diagnostics);
        return this;
    }

    public FakeWorkspace Repair(Layer layer)
    {
        _diagnosticsByLayer[layer].Clear();
        return this;
    }

    public FakeWorkspace RepairEverything()
    {
        foreach (var layer in LayerCatalog.InPipelineOrder)
        {
            _diagnosticsByLayer[layer].Clear();
        }

        return this;
    }

    public IReadOnlyList<Diagnostic> DiagnosticsIn(Layer layer) => _diagnosticsByLayer[layer];

    /// <summary>Everything currently broken, in no particular order.</summary>
    public IReadOnlyList<Diagnostic> AllDiagnostics() =>
        LayerCatalog.InPipelineOrder
            .SelectMany(layer => _diagnosticsByLayer[layer])
            .Concat(OrphanDiagnostics)
            .ToList();
}
