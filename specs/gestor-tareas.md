# Spec — Gestor de tareas con dependencias

> **Qué es este documento.** La especificación de entrada del orquestador, bajo filosofía SDD. Describe **qué** debe hacer la aplicación y **por qué**, no cómo construirla: no nombra clases, endpoints, archivos ni estructura de proyectos. Esa descomposición es trabajo del Spec Analyzer, y adelantarla acá lo dejaría sin nada que analizar.
>
> **Convención de identificadores.** Las reglas de negocio se numeran `RN-nn` y los criterios de aceptación `CA-nn`. Los IDs son estables y se citan a lo largo del pipeline: el plan de tareas cita la regla que implementa, el log de la corrida muestra qué regla se está trabajando en qué capa, y la verificación final se corre contra la lista de criterios. **Toda cita a una `RN-nn` apunta a una regla que existe** — es una invariante del propio spec, comprobable mecánicamente. No todos los criterios citan una regla: los que cubren funcionalidad básica no ejercitan ninguna, y eso es legítimo. Lo que nunca es legítimo es citar una regla que no está.

## 1. Propósito

Una aplicación web para gestionar tareas que dependen unas de otras. El valor está en que el sistema **impide cerrar trabajo cuyos prerrequisitos siguen abiertos**: el usuario no tiene que acordarse del orden, la aplicación lo sostiene.

## 2. Actores

**Usuario** — persona única que gestiona su propia lista. No hay roles, ni cuentas, ni separación entre usuarios.

## 3. Casos de uso

- Crear, ver, editar y eliminar tareas.
- Declarar que una tarea depende de otra, y quitar esa dependencia.
- Marcar una tarea como completada.
- Ver, para una tarea que no puede completarse todavía, **qué la está bloqueando**.

## 4. Modelo conceptual

### Tarea

| Atributo | Descripción |
|---|---|
| Identificador | Único y estable durante toda la vida de la tarea |
| Título | Texto obligatorio, no vacío |
| Estado | `Pendiente` o `Completada`. Toda tarea nace `Pendiente` |
| Fecha límite | Opcional. Informativa: no participa de ninguna regla de negocio |

### Dependencia

Relación dirigida entre dos tareas distintas, que se lee **"la tarea A depende de la tarea B"**. Significa que B es prerrequisito de A: B tiene que estar terminada para que A pueda terminarse.

Una tarea puede depender de varias, y varias pueden depender de la misma. El conjunto de tareas y dependencias forma un grafo dirigido.

**Vocabulario, para evitar ambigüedad en el resto del documento:**

- Las **dependencias de A** son las tareas de las que A depende — sus prerrequisitos.
- Los **dependientes de A** son las tareas que dependen de A — las que A bloquea.

Los dos términos se parecen y significan cosas opuestas. Donde importa, el documento dice cuál.

## 5. Reglas de negocio

### RN-01 — Una tarea no se completa con prerrequisitos abiertos

Una tarea no puede pasar a estado `Completada` mientras **alguna de las tareas de las que depende** siga en estado `Pendiente`.

Ejemplo, para fijar la dirección de la regla sin lugar a duda:

> «Publicar el informe» **depende de** «Redactar el informe».
>
> - Si «Redactar el informe» está `Pendiente`, entonces «Publicar el informe» **no puede completarse**. El bloqueado es «Publicar».
> - «Redactar el informe» sí puede completarse en cualquier momento: que «Publicar» dependa de ella no la bloquea a ella.

Dicho de otro modo: lo que bloquea a una tarea son sus prerrequisitos, nunca sus dependientes. Una tarea sin dependencias siempre puede completarse.

### RN-02 — Las dependencias no pueden formar ciclos

No se puede crear una dependencia que haga que una tarea dependa, directa o indirectamente, de sí misma.

Si A depende de B y B depende de C, entonces declarar que C depende de A queda rechazado: cerraría el ciclo A → B → C → A y ninguna de las tres podría completarse nunca, por RN-01.

### RN-03 — No se elimina una tarea que otras necesitan

No se puede eliminar una tarea que tenga **dependientes** — es decir, de la que alguna otra tarea dependa. Para eliminarla, primero hay que quitar esas dependencias.

Esto vale para tareas en cualquier estado: una tarea `Completada` que sigue siendo prerrequisito de otra tampoco se elimina, porque borrarla haría desaparecer la razón por la que su dependiente quedó desbloqueado.

## 6. Criterios de aceptación

Cada criterio describe un comportamiento observable desde fuera del sistema. La columna **Verifica** indica qué regla ejercita; los criterios sin regla asociada cubren funcionalidad básica.

| ID | Criterio | Verifica |
|---|---|---|
| **CA-01** | Se puede crear una tarea con título y, opcionalmente, fecha límite. Queda en estado `Pendiente`. | — |
| **CA-02** | Se puede obtener la lista de tareas, y para cada una sus dependencias. | — |
| **CA-03** | Crear una tarea sin título, o con el título vacío, es rechazado y no crea nada. | — |
| **CA-04** | Se puede declarar que una tarea depende de otra, y quitar esa dependencia después. | — |
| **CA-05** | Completar una tarea **sin dependencias** funciona y su estado pasa a `Completada`. | RN-01 |
| **CA-06** | **Completar una tarea que tiene al menos una dependencia en estado `Pendiente` es rechazado con un error de regla de negocio, y el estado de la tarea no cambia.** Consultar la tarea después de la operación fallida la muestra todavía `Pendiente`. | RN-01 |
| **CA-07** | Completar una tarea cuyas dependencias están **todas** `Completada` funciona. | RN-01 |
| **CA-08** | El error de CA-06 indica **cuáles** son las dependencias pendientes que bloquean, no solo que la operación falló. | RN-01 |
| **CA-09** | Declarar una dependencia que cerraría un ciclo —incluyendo el caso de una tarea que dependa de sí misma— es rechazado y el grafo no cambia. | RN-02 |
| **CA-10** | Eliminar una tarea que tiene dependientes es rechazado; eliminar una que no los tiene funciona. | RN-03 |
| **CA-11** | La interfaz lista las tareas con un control para marcarlas como completadas. | — |
| **CA-12** | En la interfaz, el control de una tarea bloqueada por RN-01 aparece deshabilitado, y el motivo del bloqueo es visible para el usuario sin necesidad de intentarlo. | RN-01 |
| **CA-13** | Si aun así se intenta completar una tarea bloqueada, la interfaz muestra el error sin dejar la lista en un estado inconsistente. | RN-01 |
| **CA-14** | La interfaz tiene un formulario para crear una tarea, con título y fecha límite opcional, y la tarea creada aparece en la lista sin recargar la página. | — |
| **CA-15** | La interfaz permite declarar que una tarea depende de otra existente. | — |

**CA-06 es el criterio central de todo el proyecto.** Es el que distingue "el pipeline generó código que compila" de "el pipeline generó la aplicación pedida": la compilación la verifica el gate de LSP, la regla de negocio solo la verifica este criterio.

## 7. Restricciones técnicas

Impuestas por el contexto, no derivadas del problema. Se listan aparte precisamente para no confundirlas con diseño.

- **Backend en .NET**, exponiendo una API HTTP.
- **Frontend en React.**
- **Persistencia con Entity Framework Core, proveedor InMemory.** Sin base de datos externa, sin migraciones. Los datos no sobreviven al reinicio del proceso, cosa aceptable para este alcance.
- **Las reglas de negocio viven en el código de dominio**, no delegadas a constraints de la base de datos. El proveedor InMemory no las aplicaría de todos modos, y el objetivo es que las invariantes sean código verificable.
- Sin autenticación ni autorización.

## 8. Fuera de alcance

Lo que este spec deliberadamente **no** pide. Cada exclusión es una decisión, no un olvido.

- **Reabrir una tarea completada.** El estado solo avanza de `Pendiente` a `Completada`. Vale registrar por qué: permitir el camino inverso abriría un hueco en RN-01 — si A depende de B y ambas están completas, reabrir B dejaría a A completada con un prerrequisito pendiente. Cerrar ese hueco requiere una cuarta regla (propagar la reapertura, o bloquearla cuando haya dependientes completados). Es un problema real y resoluble, y queda fuera para no ampliar el alcance del artefacto.
- Usuarios, cuentas, autenticación, permisos.
- Prioridades, etiquetas, categorías, adjuntos, comentarios.
- Notificaciones o alertas por fecha límite. La fecha es informativa.
- Persistencia real, backups, historial de cambios.
- Paginación, búsqueda o filtros sobre la lista.
- Edición masiva o reordenamiento manual.
