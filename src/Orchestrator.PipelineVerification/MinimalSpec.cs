namespace Orchestrator.PipelineVerification;

/// <summary>
/// A spec small enough to run the whole graph for a few turns.
/// </summary>
/// <remarks>
/// Not <c>specs/gestor-tareas.md</c> on purpose. What this harness has to demonstrate is the
/// review loop against real diagnostics, and every extra rule in the spec is another paid turn
/// per layer without making the loop any more visible. The real spec is block 5's business.
/// </remarks>
internal static class MinimalSpec
{
    public const string Text = """
        # Spec — Lista de tareas mínima

        > Spec reducido, usado por el arnés de verificación del Bloque 4. El artefacto real es
        > `specs/gestor-tareas.md`.

        ## 1. Propósito

        Una aplicación web para registrar tareas y marcarlas como completadas.

        ## 2. Modelo conceptual

        ### Tarea

        | Atributo | Descripción |
        |---|---|
        | Identificador | Único y estable |
        | Título | Texto obligatorio, no vacío |
        | Estado | `Pendiente` o `Completada`. Toda tarea nace `Pendiente` |

        ## 3. Reglas de negocio

        ### RN-01 — Una tarea sin título no existe

        Crear una tarea con el título vacío, o compuesto solo de espacios, queda rechazado. El
        rechazo es un resultado que el llamador puede inspeccionar, no una excepción.

        ## 4. Criterios de aceptación

        | ID | Criterio | Verifica |
        |---|---|---|
        | **CA-01** | Se puede crear una tarea con un título no vacío. Queda en estado `Pendiente`. | — |
        | **CA-02** | Crear una tarea con el título vacío es rechazado, y la tarea no se registra. | RN-01 |
        | **CA-03** | Se puede listar las tareas registradas. | — |

        ## 5. Restricciones técnicas

        - Backend en .NET, API HTTP.
        - Frontend en React con TypeScript.
        - Persistencia en memoria. Sin base de datos externa.
        - Sin autenticación.
        """;
}
