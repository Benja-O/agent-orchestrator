# Evidencia de corridas

Tres logs JSONL de corridas reales, versionados a propósito.

**Por qué existen.** `logs/` está gitignoreado y una corrida completa gasta cuota del plan Pro
(ADR-001), así que sin esto la única forma de ver el pipeline funcionando sería correrlo. *"Corré el
pipeline y vas a ver"* no es una respuesta para quien evalúa sin cuota — es el mismo argumento por el
que la app generada viaja en su propio repo y no como instrucción de regenerarla (ADR-008, ADR-018).

Son copias inmutables, no un directorio de logs. `logs/` sigue siendo desechable.

Cada línea es un evento tipado con dos lecturas —`event` para una máquina, y el `Summary` que la
consola imprime a partir del mismo objeto— según ADR-015.

---

## `run-20260811-151703.jsonl` — la corrida que produjo la app entregada

**17 min 52 s, cuatro turnos de agente, cero iteraciones de revisión.** El camino feliz completo:
14 tareas planificadas sin dejar ningún `CA-nn` sin cubrir, las tres capas pasando su gate en la
primera pasada, y el gate de runtime ejercitando 2 rutas descubiertas por OpenAPI.

Qué mirar:

- **Línea 5** — `plan-produced` con `criteriaNotCovered: []`. El plan del spec analyzer parseó al
  primer intento, por segunda corrida consecutiva sobre un plan distinto (14 tareas contra 17).
- **Línea 15** — `api-gate` con `total: 10, errorCount: 0`. Diez diagnostics y ninguno bloquea:
  la distinción entre error y warning es lo que evita que el pipeline persiga advertencias con turnos
  pagos.
- **Línea 17** — `application-verified` con `routesExercised: 2`. Ese contador va **junto** a los
  diagnostics y no al lado, porque "sin fallos" significa una cosa con rutas ejercitadas y otra con
  cero (ADR-017).

## `run-20260811-132014.jsonl` — la corrida que compiló y no funcionaba

La corrida anterior, **con las tres capas en verde y una aplicación que devolvía 500 en la primera
request**. Es el hallazgo que produjo ADR-017 y no se ve en este archivo: el log termina en
`completed` y miente sin saberlo.

Está acá justamente por eso. Comparada con la de arriba, la diferencia es una línea —el nodo
`api-runtime`, que en esta corrida todavía no existía— y esa línea es la distancia entre *"el
pipeline produce código que compila"* y *"el pipeline produce la aplicación pedida"*.

El 500 lo produjo `tareaBuilder.Property("_dependencias")` en el `DbContext`, escrito alrededor de
una creencia falsa sobre EF Core. Es C# válido: **no hay diagnostic para una creencia.**

## `pipeline-verification-20260810-091708.jsonl` — el loop de revisión, con un error inyectado

La corrida del arnés del Bloque 4, sobre un spec mínimo. Es donde se ve la arista característica del
grafo funcionando de punta a punta:

| Línea | Qué pasa |
|---|---|
| 10 | El gate encuentra `CS0103` en `src/Domain/InjectedFault.cs` — el error inyectado **después** del primer turno del agente, por la misma puerta por la que llegaría uno real |
| 11 | `review-iteration` · `resolved: 0, introduced: 1` → `sendBackToAgent` |
| 13 | El agente de dominio vuelve a correr, ahora con `diagnosticsHandedOver: 1` |
| 16 | El gate contesta `total: 0`. **La iteración siguiente lo corrigió** |

Y después sigue, porque esta corrida además **termina mal**, que es lo que la vuelve buena evidencia:

| Línea | Qué pasa |
|---|---|
| 21 | El gate de API reporta 7 errores `CS0246`: tipos detrás de referencias que no resuelven |
| 28 | `review-iteration` · `resolved: 6, introduced: 7, persisting: 5` → `terminate` |
| 29 | `run-terminated` · `iterationLimitReached`, con la traza completa de los nueve nodos visitados |

**Esos `CS0246` no los causó el agente: es un workspace sin restaurar.** Roslyn reporta como error
cada tipo detrás de una referencia que no resuelve, así que la revisión llegó cargada de diagnostics
que nada de lo que el agente escribió produjo — el falso rojo, a escala y pagado en turnos. Se cerró
en el Bloque 5: `GeneratedWorkspaceRestorer` corre `dotnet restore` y `npm ci` antes del primer
agente (ADR-016).

Vale la pena leer la línea 28 completa. `resolved: 6, introduced: 7, persisting: 5` es el pipeline
diciendo con precisión que el agente estaba trabajando y no convergiendo, que es exactamente la
distinción que el techo de iteraciones existe para cortar antes de que consuma la ventana de 5 h.
