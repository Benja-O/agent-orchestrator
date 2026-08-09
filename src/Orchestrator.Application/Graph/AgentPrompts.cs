using System.Globalization;
using System.Text;
using Orchestrator.Domain;

namespace Orchestrator.Application.Graph;

/// <summary>
/// Builds what each agent is asked to do.
/// </summary>
/// <remarks>
/// <para>
/// Composition lives here rather than in <c>Orchestrator.Agents</c> because <em>what</em> to
/// say is application logic — which tasks belong to this layer, which diagnostics this
/// iteration has to fix — while <em>how</em> to deliver it to a subprocess is the adapter's
/// problem. It is also pure string work, so it is testable without a runner.
/// </para>
/// <para>
/// The prose is in Spanish to match the subagent definitions in <c>templates/agents/</c> and
/// the spec itself: an agent reading instructions in two languages spends turns reconciling
/// them.
/// </para>
/// </remarks>
public static class AgentPrompts
{
    /// <summary>How many diagnostics get spelled out in a review prompt before the list is cut.</summary>
    /// <remarks>
    /// The gate already truncates by severity (ADR-010), so the survivors are the ones
    /// blocking compilation. This second ceiling exists because a prompt has a budget and an
    /// agent handed sixty errors fixes none of them well.
    /// </remarks>
    public const int MaximumDiagnosticsInPrompt = 20;

    public static string ForSpecAnalysis(SpecDocument spec, string? previousParseFailure)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("Analizá el siguiente spec y devolvé el plan de tareas por capa.");
        prompt.AppendLine();
        prompt.AppendLine("## Formato de salida obligatorio");
        prompt.AppendLine();
        prompt.AppendLine("Una sección por capa, y dentro de cada una un bloque por tarea, exactamente así:");
        prompt.AppendLine();
        prompt.AppendLine("## Capa: dominio");
        prompt.AppendLine();
        prompt.AppendLine("### T-01 — Enunciado de la tarea en términos del spec");
        prompt.AppendLine("- Implementa: RN-01");
        prompt.AppendLine("- Verifica: CA-05, CA-07");
        prompt.AppendLine("- Depende de: —");
        prompt.AppendLine();
        prompt.AppendLine("Las capas válidas son `dominio`, `api` y `frontend`, en ese orden. Los identificadores `T-nn` son");
        prompt.AppendLine("correlativos y únicos en todo el plan. **Toda tarea cita al menos un `RN-nn` o `CA-nn` del spec.**");
        prompt.AppendLine("Usá `—` cuando una lista esté vacía.");

        if (previousParseFailure is not null)
        {
            prompt.AppendLine();
            prompt.AppendLine("## Tu respuesta anterior no se pudo procesar");
            prompt.AppendLine();
            prompt.AppendLine(previousParseFailure);
            prompt.AppendLine();
            prompt.AppendLine("Corregí eso y volvé a emitir el plan completo, no solo la parte que falló.");
        }

        prompt.AppendLine();
        prompt.AppendLine("## Spec");
        prompt.AppendLine();
        prompt.AppendLine(spec.Text);

        return prompt.ToString();
    }

    public static string ForLayer(Layer layer, TaskPlan plan, SpecDocument spec, IReadOnlyList<Diagnostic> diagnosticsToFix)
    {
        var prompt = new StringBuilder();
        var tasks = plan.TasksFor(layer);

        if (diagnosticsToFix.Count == 0)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"Implementá las tareas de la capa `{LayerCatalog.PlanNameOf(layer)}` del plan.");
        }
        else
        {
            prompt.AppendLine(CultureInfo.InvariantCulture,
                $"El gate de LSP encontró errores en la capa `{LayerCatalog.PlanNameOf(layer)}`. Corregilos.");
            prompt.AppendLine();
            prompt.AppendLine("**No es tu reporte lo que decide si esto está listo, es el gate.** Los errores de abajo salen de");
            prompt.AppendLine("consultar los language servers sobre el código que escribiste, no de una opinión.");
            prompt.AppendLine();
            prompt.AppendLine("## Errores a corregir");
            prompt.AppendLine();

            foreach (var diagnostic in diagnosticsToFix.Take(MaximumDiagnosticsInPrompt))
            {
                var code = diagnostic.Code.Length > 0 ? $" {diagnostic.Code}" : string.Empty;
                prompt.AppendLine(CultureInfo.InvariantCulture,
                    $"- `{diagnostic.FilePath}:{diagnostic.Range.StartLine}:{diagnostic.Range.StartColumn}`{code} — {diagnostic.Message}");
            }

            if (diagnosticsToFix.Count > MaximumDiagnosticsInPrompt)
            {
                prompt.AppendLine(CultureInfo.InvariantCulture,
                    $"- … y {diagnosticsToFix.Count - MaximumDiagnosticsInPrompt} más. Consultá la tool `diagnostics` para verlos todos.");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("## Tus tareas");
        prompt.AppendLine();

        foreach (var task in tasks)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"### {task.Identifier} — {task.Statement}");

            if (task.BusinessRules.Count > 0)
            {
                prompt.AppendLine(CultureInfo.InvariantCulture, $"- Implementa: {string.Join(", ", task.BusinessRules)}");
            }

            if (task.AcceptanceCriteria.Count > 0)
            {
                prompt.AppendLine(CultureInfo.InvariantCulture, $"- Verifica: {string.Join(", ", task.AcceptanceCriteria)}");
            }

            prompt.AppendLine();
        }

        prompt.AppendLine("## Spec");
        prompt.AppendLine();
        prompt.AppendLine(spec.Text);

        return prompt.ToString();
    }
}
