using System.Globalization;

namespace Orchestrator.GeneratedAppVerification;

public sealed record VerificationVerdict(bool Passed, IReadOnlyList<string> Lines);

/// <summary>
/// The part of block 5's exit criterion the LSP gate cannot answer.
/// </summary>
/// <remarks>
/// <para>
/// R4 of the ROADMAP, stated as a runnable check: <strong>the gate verifies compilation, not
/// correctness.</strong> A business rule can be missing entirely and every file still compile, so
/// a run that ends green proves the pipeline produced code that builds — not that it produced the
/// application the spec asked for. CA-06 is where those two things come apart, and the spec says
/// so itself: <em>"es el que distingue 'el pipeline generó código que compila' de 'el pipeline
/// generó la aplicación pedida'"</em>.
/// </para>
/// <para>
/// The script below is the spec's own worked example, driven over HTTP: «Publicar el informe»
/// depends on «Redactar el informe», so publishing must be refused while drafting is open, and
/// must succeed once it is done. It ends by completing both, because a rule that rejects
/// everything would pass a check that only ever looked for rejections.
/// </para>
/// </remarks>
public static class InvariantVerification
{
    public static async Task<VerificationVerdict> RunAsync(TaskManagerClient client, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var passed = true;
        var suffix = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
        var prerequisiteTitle = $"Redactar el informe {suffix}";
        var dependentTitle = $"Publicar el informe {suffix}";

        void Check(string what, bool condition, string evidence)
        {
            lines.Add($"  {(condition ? "OK " : "MAL")}  {what}");

            if (evidence.Length > 0)
            {
                lines.Add($"          {evidence}");
            }

            passed &= condition;
        }

        VerificationVerdict Abandon(string why)
        {
            lines.Add($"  MAL  {why}");
            lines.Add(string.Empty);
            lines.Add("CRITERIO NO CUMPLIDO — la verificación no pudo llegar hasta CA-06.");
            return new VerificationVerdict(false, lines);
        }

        lines.Add("Criterio de salida del Bloque 5 — la invariante RN-01, vista desde afuera:");

        var prerequisite = await client.CreateTaskAsync(prerequisiteTitle, cancellationToken).ConfigureAwait(false);
        var dependent = await client.CreateTaskAsync(dependentTitle, cancellationToken).ConfigureAwait(false);

        Check(
            "CA-01 — se crean dos tareas",
            prerequisite is not null && dependent is not null,
            prerequisite is null || dependent is null
                ? "no se pudo crear una tarea ni leer su identificador; mirá el detalle de abajo"
                : $"«{prerequisiteTitle}» = {prerequisite}, «{dependentTitle}» = {dependent}");

        if (prerequisite is null || dependent is null)
        {
            return Abandon("sin tareas no hay dependencia que declarar");
        }

        var declared = await client.DeclareDependencyAsync(dependent, prerequisite, cancellationToken).ConfigureAwait(false);

        Check(
            "CA-04 — «Publicar» pasa a depender de «Redactar»",
            declared,
            declared ? $"{dependent} depende de {prerequisite}" : "la API no aceptó la dependencia");

        if (!declared)
        {
            return Abandon("sin la dependencia, completar «Publicar» no violaría nada y el resto no probaría nada");
        }

        // The one that matters. Everything above exists to make this call meaningful.
        var refused = await client.CompleteAsync(dependent, cancellationToken).ConfigureAwait(false);

        Check(
            "CA-06 — completar «Publicar» con «Redactar» pendiente es rechazado",
            refused.RejectedByTheClient,
            $"{(int)refused.Status} {refused.Status}"
            + (refused.Succeeded ? " — la invariante NO se sostiene: la API aceptó la operación" : string.Empty));

        var namesTheBlocker = refused.ResponseBody.Contains(prerequisiteTitle, StringComparison.OrdinalIgnoreCase)
            || refused.ResponseBody.Contains(prerequisite, StringComparison.OrdinalIgnoreCase);

        Check(
            "CA-08 — el error dice cuál es el prerrequisito que bloquea",
            namesTheBlocker,
            namesTheBlocker
                ? "la respuesta nombra «Redactar el informe»"
                : "la respuesta no menciona ni el título ni el identificador de la tarea que bloquea");

        var stillPending = !await client.IsCompletedAsync(dependent, cancellationToken).ConfigureAwait(false);

        Check(
            "CA-06 — después del rechazo, «Publicar» sigue Pendiente",
            stillPending,
            stillPending ? "el estado no cambió" : "la operación rechazada igual dejó rastro");

        var prerequisiteCompleted = await client.CompleteAsync(prerequisite, cancellationToken).ConfigureAwait(false);

        Check(
            "CA-05 — completar «Redactar», que no tiene prerrequisitos, funciona",
            prerequisiteCompleted.Succeeded,
            $"{(int)prerequisiteCompleted.Status} {prerequisiteCompleted.Status}");

        var dependentCompleted = await client.CompleteAsync(dependent, cancellationToken).ConfigureAwait(false);

        Check(
            "CA-07 — y ahora «Publicar» sí se completa",
            dependentCompleted.Succeeded,
            $"{(int)dependentCompleted.Status} {dependentCompleted.Status}"
            + (dependentCompleted.Succeeded
                ? string.Empty
                : " — la regla rechaza de más: bloquea una tarea cuyos prerrequisitos están completos"));

        lines.Add(string.Empty);
        lines.Add(passed
            ? "CRITERIO CUMPLIDO — el endpoint sostiene RN-01, que es lo que el gate no puede verificar."
            : "CRITERIO NO CUMPLIDO — la app compila pero no es la aplicación que el spec pedía.");

        return new VerificationVerdict(passed, lines);
    }
}
