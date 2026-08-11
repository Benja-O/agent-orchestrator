using Orchestrator.Domain;

namespace Orchestrator.PipelineVerification;

internal sealed record VerificationVerdict(bool Passed, IReadOnlyList<string> Lines);

/// <summary>
/// Watches the run go by and decides whether the block's exit criterion actually happened.
/// </summary>
/// <remarks>
/// Deliberately built out of the same events the log is built from, and not out of some private
/// bookkeeping. ADR-015 made the log the only window into the graph; if the criterion could be
/// checked from something the log does not carry, the log would be the wrong shape.
/// </remarks>
internal sealed class VerificationTranscript : IRunObserver
{
    private readonly List<RunEvent> _events = [];

    public void Observe(RunEvent runEvent) => _events.Add(runEvent);

    public VerificationVerdict Evaluate(string injectedFilePath)
    {
        var domain = LayerCatalog.AgentNameOf(Layer.Domain);
        var lines = new List<string>();
        var passed = true;

        void Check(string what, bool condition, string evidence)
        {
            lines.Add($"  {(condition ? "OK " : "MAL")}  {what}");

            if (evidence.Length > 0)
            {
                lines.Add($"          {evidence}");
            }

            passed &= condition;
        }

        lines.Add("Criterio de salida del Bloque 4:");

        // 1. The gate attributed a blocking diagnostic to the layer the fault was injected into.
        //
        //    Read from the review's own counters rather than from GateEvaluated.BlockingSample,
        //    which carries three items chosen from the whole workspace and ordered by path — so
        //    anything under src/Api sorts ahead of src/Domain and the injected file is essentially
        //    never in it. The sample is a convenience for a human reading the log; using it as
        //    evidence was this harness's own bug, not the pipeline's.
        var blamed = _events
            .OfType<ReviewIterationEvaluated>()
            .FirstOrDefault(review => review.Layer == domain && review.Introduced + review.Persisting > 0);

        Check(
            $"el gate vio el error inyectado en {injectedFilePath} y lo atribuyó a dominio",
            blamed is not null,
            blamed is null
                ? "ningún veredicto atribuyó un diagnostic bloqueante a la capa de dominio"
                : $"{blamed.Introduced} introducido(s), {blamed.Persisting} persistente(s) en dominio");

        // 2. The conditional edge sent the work back instead of advancing.
        var sentBack = _events
            .OfType<ReviewIterationEvaluated>()
            .FirstOrDefault(review => review.Action == ReviewAction.SendBackToAgent && review.Layer == domain);

        Check(
            "la arista condicional devolvió el trabajo al agente de dominio",
            sentBack is not null,
            sentBack is null
                ? "no hubo ninguna iteración con acción SendBackToAgent para dominio"
                : $"iteración {sentBack.Iteration}: resueltos {sentBack.Resolved}, introducidos {sentBack.Introduced}, persistentes {sentBack.Persisting}");

        // 3. The agent was re-invoked, and was handed the diagnostics rather than asked to guess.
        var reinvoked = _events
            .OfType<AgentInvoked>()
            .FirstOrDefault(invocation => invocation.AgentName == domain && invocation.Attempt >= 2);

        Check(
            "el agente de dominio se reinvocó con los diagnostics como input",
            reinvoked is { DiagnosticsHandedOver: > 0 },
            reinvoked is null
                ? "no hubo una segunda invocación del agente de dominio"
                : $"intento {reinvoked.Attempt}, {reinvoked.DiagnosticsHandedOver} diagnostic(s) entregados");

        // 4. And the layer stopped being the blocking one, which is the half that makes this a
        //    loop rather than a dead end.
        //
        //    Deliberately *not* "the whole workspace went clean". The criterion is that the
        //    injected error got fixed, and a global check would fold in every unrelated problem
        //    the other layers still have — today, the ones debt D12 causes. Tying this block's
        //    evidence to a debt block 5 owns would make it fail for the wrong reason, and passing
        //    would depend on work nobody has done yet.
        //
        //    The graph always sends the work back to the *earliest* blocking layer, so a later
        //    iteration aimed at a layer after this one is proof that this one is no longer
        //    blocking. A clean global verdict counts too, when it happens.
        var indexOfSendBack = sentBack is null ? -1 : _events.IndexOf(sentBack);
        var later = indexOfSendBack < 0 ? [] : _events.Skip(indexOfSendBack + 1).ToList();

        var movedOn = later
            .OfType<ReviewIterationEvaluated>()
            .FirstOrDefault(review => review.Layer != domain);

        var cleanGate = later.OfType<GateEvaluated>().FirstOrDefault(gate => gate.ErrorCount == 0);

        Check(
            "la iteración siguiente corrigió el error y la capa dejó de ser la bloqueante",
            movedOn is not null || cleanGate is not null,
            (movedOn, cleanGate) switch
            {
                (not null, _) => $"el grafo pasó a la capa '{movedOn.Layer}': dominio ya no bloquea",
                (_, not null) => $"veredicto {cleanGate.Fingerprint}: 0 errores sobre {cleanGate.Total} diagnostic(s)",
                _ => "dominio siguió siendo la capa bloqueante después de devolverle el trabajo",
            });

        lines.Add(string.Empty);
        lines.Add(passed
            ? "CRITERIO CUMPLIDO — el loop de revisión corrió contra diagnostics reales."
            : "CRITERIO NO CUMPLIDO — mirá el log completo.");

        return new VerificationVerdict(passed, lines);
    }
}
