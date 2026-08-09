# AI.md — Referencia Técnica de Arquitectura

> Las instrucciones de comportamiento para Claude Code (rol, método de trabajo, control de
> costo, formato de commits) viven en `CLAUDE.md`. Este archivo es exclusivamente referencia
> técnica del codebase: arquitectura, convenciones, tipos, anti-patrones.
>
> **Estado a 2026-08-09: mayormente arquitectura *objetivo*, con una parte ya real.** El
> Bloque 2 construyó `Orchestrator.LspServer` y su suite; el resto —grafo, agentes, CLI— sigue
> siendo el contrato que el código deberá cumplir cuando se escriba, derivado de las decisiones
> cerradas en `DECISIONS.md`. Se revisa otra vez al cierre del Bloque 3 del `ROADMAP.md`, cuando
> el grafo corra end-to-end. Cualquier divergencia entre este archivo y el código que exista es
> un bug de uno de los dos, y hay que resolverla explícitamente, no dejarla pasar.

## 🏛️ Filosofía general

El orquestador aplica **Clean Architecture**: el grafo de agentes es lógica de aplicación
pura, y todo lo que habla con el mundo exterior —la CLI de Claude Code, los language servers,
el filesystem— vive en adaptadores. Esa separación no es purismo: es lo que permite testear
la máquina de estados sin gastar cuota del plan Pro (ADR-001).

**REGLAS DE ORO (innegociables):**

1. **`Orchestrator.Domain` y `Orchestrator.Application` nunca conocen Claude Code ni LSP.**
   Si aparece `System.Diagnostics.Process`, un tipo del protocolo LSP, o la cadena `claude`
   fuera de los proyectos adaptadores, la arquitectura está rota. El grafo razona sobre
   `Diagnostic` y `AgentResult`, no sobre stdout ni sobre JSON-RPC.
   *Verificable con:* `grep -rn "System.Diagnostics.Process\|LanguageServer" Orchestrator.Domain Orchestrator.Application` → debe dar cero.

2. **Todo I/O de subproceso vive en los adaptadores, y en ningún otro lado.** `Process.Start`
   aparece en exactamente dos proyectos, cada uno dueño de los procesos que lanza:
   `Orchestrator.Agents` lanza la CLI de Claude Code; `Orchestrator.LspServer` lanza los dos
   language servers. `Orchestrator.Lsp` lanza el servidor MCP y lo consulta como cliente.
   La jerarquía es orquestador → servidor MCP → language servers, y la razón de que no sea
   plana está en ADR-013: el que sostiene las conexiones LSP tiene que ser el que contesta las
   tool calls.
   El apagado es determinista en los tres niveles — un language server huérfano tras una
   corrida fallida es un bug, no un detalle: mantiene handles sobre `output/`, que ADR-008
   exige poder borrar y regenerar de cero.
   *Verificable con:* `grep -rn "Process.Start" --include=*.cs src/` → en proyectos de
   producción, solo `Orchestrator.Agents`, `Orchestrator.Lsp` y `Orchestrator.LspServer`. El
   arnés `Orchestrator.LspServer.ManualVerification` también lanza procesos y aparece en el
   grep: no es producción ni test, es la verificación manual del Bloque 2, y por eso está
   nombrado así.
   *Si algo quedó vivo:* `pwsh tools/kill-language-servers.ps1`.

3. **El grafo se testea sin invocar un solo agente real.** `FakeAgentRunner` sirve respuestas
   grabadas; `FakeLanguageServer` sirve diagnostics fijos. Ninguna suite de tests lanza
   `claude -p` ni arranca un language server real.
   Esta regla **existe por ADR-001**: el límite de 5 h del plan Pro se agota justo cuando más
   se necesita, depurando la máquina de estados. No es preferencia de estilo — es la
   diferencia entre poder iterar el grafo cien veces por día y no poder.
   *Verificable con:* la suite completa corre sin red, sin `claude` en el `PATH`, y en menos
   de unos segundos. Si un test tarda minutos, está invocando algo real.

4. **Prohibido `DateTime.Now` y `DateTime.UtcNow` fuera de adaptadores.** Todo acceso al
   tiempo va por `IClock`. Acá importa concretamente por dos cosas: los timeouts de
   subproceso (un agente colgado no puede bloquear la corrida para siempre) y la capacidad de
   testear un loop de revisión que reintenta, sin esperar en tiempo real.

## 📂 Estructura de proyectos y responsabilidades

| Proyecto | Contenido | Depende de |
|---|---|---|
| `Orchestrator.Domain` | Modelo del grafo y del pipeline: `NodeId`, `GraphState`, `Diagnostic`, `TaskPlan`, `LayerScope`, `AgentResult`, las transiciones y sus predicados. | Nada |
| `Orchestrator.Application` | `GraphRunner` (la máquina de estados), `SpecAnalyzer`, política del loop de revisión, límites de iteración y detección de no-progreso. | Domain |
| `Orchestrator.Agents` | `ClaudeCodeAgentRunner`: implementa `IAgentRunner` invocando `claude -p` vía `Process`. Construcción de prompts, manejo de timeouts, parseo de la salida. | Domain |
| `Orchestrator.Lsp` | Cliente del servidor MCP: lo lanza como proceso y consume sus tools. Implementa `ILanguageServerGateway`. Traduce diagnostics del contrato al tipo de dominio. | Domain |
| **`Orchestrator.LspServer`** ✅ | **Existe.** El servidor MCP: host ASP.NET Core que expone las cinco tools de `docs/mcp-contract.md` por HTTP y es dueño de los dos language servers. Habla LSP con `StreamJsonRpc`. **No depende de `Domain`** — es agnóstico del proyecto que analiza (ADR-010, ADR-013). | Nada del repo |
| `Orchestrator.Cli` | Host de consola: parseo de argumentos, wiring de dependencias, logging, código de salida. Es el único con `Main` del lado del orquestador. | Todos |
| `Orchestrator.TestSupport` | `FakeAgentRunner`, `FakeLanguageServer`, `FakeClock`, builders de estado del grafo. **Solo lo referencian proyectos de test**, nunca uno de producción. | Domain |

Cada proyecto de producción tiene su `.Tests` correspondiente.

Además, fuera del árbol de producción:

| Proyecto / carpeta | Para qué |
|---|---|
| `Orchestrator.LspServer.ManualVerification` | La verificación manual del Bloque 2, en un comando. **Arranca language servers reales, así que no es un test y no está en la suite** (regla de oro 3). Es la evidencia reproducible del criterio de salida. |
| `fixtures/` | Código roto a propósito —un `.cs` y un `.ts`— contra el que se verifica la capa LSP. No lo compila nadie más. |
| `tools/` | Scripts de operación. Hoy: matar language servers que hayan quedado vivos. |

### La regla de dependencias, dicha al revés
`Orchestrator.Application` conoce `IAgentRunner` e `ILanguageServerGateway` (interfaces que
viven en `Domain`), y no sabe que del otro lado hay un proceso de Claude Code o un servidor
de lenguaje. Podría haber un humano. Ese es el punto: si el grafo no distingue un agente
real de un fake, la regla de oro 3 se cumple sola.

## 🕸️ Modelo del grafo

- **Nodo** — un paso del pipeline: un agente de capa (dominio, API .NET, React), el spec
  analyzer, o una verificación contra el gate de LSP.
- **Arista condicional** — una transición gobernada por un predicado sobre `GraphState`. La
  arista característica del proyecto: *si el gate devuelve diagnostics de error, volver al
  agente de la capa que los produjo, con los diagnostics como input; si no, avanzar.*
- **Estado (`GraphState`)** — inmutable, se reemplaza en cada transición en vez de mutarse.
  Lleva el plan de tareas, el nodo actual, el historial de intentos por nodo y los
  diagnostics de la última verificación. Que sea inmutable es lo que permite loguear la
  traza completa de la corrida sin copias defensivas, y es lo que hace la demo legible.

**Terminación — obligatoria, no opcional.** Todo ciclo del grafo tiene que poder terminar por
tres vías, y las tres se implementan a mano porque no hay framework que las provea
(ADR-003):
- Límite máximo de iteraciones por nodo.
- Detección de no-progreso: el agente devuelve el mismo conjunto de diagnostics dos veces
  seguidas y no está avanzando.
- Fallo terminal explícito, con estado y traza que expliquen dónde se trabó.

Un loop de revisión sin límite contra `claude -p` consume la cuota del plan Pro en una sola
corrida (ADR-001). La terminación es una restricción de costo antes que de elegancia.

## 🩺 Diagnostics

`Diagnostic` es el tipo de intercambio entre la capa LSP y el grafo, y el input del loop de
revisión. Es el contrato más cargado del sistema: lo consumen el `GraphRunner` para decidir
transiciones y el prompt del agente para corregir.

Lo que ya está fijado por `DECISIONS.md`:
- Viene de un language server, no de parsear la salida del compilador (ADR-004): tiene rango,
  severidad y código de error como datos estructurados, no como texto formateado.
- Los agentes acceden además a navegación (`definition`, `references`, `documentSymbol`) vía
  MCP (ADR-005). Los diagnostics son solo la mitad del valor de la capa LSP.

Lo que **no** está decidido todavía y es decisión pendiente del `ROADMAP.md` (Bloque 1):
la forma exacta del tipo, cómo se agrupan por capa, y cómo se recortan para caber en un
prompt sin perder lo que importa.

**Trampa conocida, a tener en cuenta al implementar:** un language server recién arrancado
devuelve diagnostics incompletos mientras indexa. Consultarlo demasiado pronto da un **falso
verde** — el gate aprueba código que no compila, que es peor que no tener gate. Hay que
esperar la señal de proyecto cargado antes de confiar en la primera respuesta (ADR-006).

**El Bloque 2 encontró una segunda vía al mismo falso verde, y conviene tenerla presente
porque no se parece a la primera: la normalización de rutas.** Los dos extremos escriben la
misma ruta distinto —`file:///F:/x/a.ts` contra `file:///f%3A/x/a.ts`— y comparadas como texto
son archivos distintos. Los diagnostics de un archivo quedan archivados bajo una clave que
nadie consulta y **el archivo parece limpio**. La lección general: *cualquier* punto donde una
identidad de archivo se compara como string es un lugar donde puede nacer un falso verde. Toda
conversión pasa por `WorkspacePaths`, y hay test de regresión.

## 🛠️ Convenciones de estilo

Heredadas del repo de trading del autor y aplicadas igual acá:

1. **Zero abbreviations.** Los nombres representan fielmente su propósito; el código se lee
   como prosa técnica. `quantity` no `qty`, `configuration` no `config`, `diagnostic` no
   `diag`. Excepción: el sufijo `Id` en nombres compuestos (`NodeId`, `RunId`) es convención
   estándar de .NET; lo prohibido es `id` suelto.
2. **Campos privados** con `_camelCase`: `private readonly IAgentRunner _agentRunner;`.
3. **Inmutabilidad por defecto.** Value objects como `readonly record struct` o `sealed
   record` con `init`. Colecciones expuestas como `IReadOnlyList<T>`. `GraphState` es
   inmutable por diseño (ver arriba).
4. **Tipado estricto.** Prohibido `dynamic`; `object` solo en adaptadores. Composición sobre
   herencia.
5. **Async.** Sufijo `Async`, `CancellationToken` como último parámetro y propagado. Nada de
   `.Result`, `.Wait()` ni `async void`. Importa concretamente acá: una corrida cancelada
   tiene que matar los subprocesos que lanzó, no dejarlos huérfanos.
6. **XML docs (`///`)** en la lógica de transiciones del grafo y en las políticas de
   terminación. Son las partes cuyo comportamiento no se deduce leyendo la firma.

## ⚠️ Errores y resultados

- **`Result<T>`** para flujos esperados que pueden fallar: un agente que devuelve código que
  no compila, un nodo que agota sus reintentos, un spec que no se puede descomponer. Eso no
  son excepciones — **son estados del grafo**, y el grafo tiene que poder razonar sobre
  ellos para decidir la siguiente arista.
- **Excepciones** para lo genuinamente excepcional: `claude` no está en el `PATH`, el
  language server murió, la configuración es inválida al arranque.
- Prohibido lanzar `Exception` o `ApplicationException` directo. Cada capa define las suyas.
- **Fallar rápido al arrancar.** Verificar que `claude` responde y que los language servers
  levantan *antes* de empezar la corrida. Descubrir en el nodo tres que falta una dependencia
  de entorno desperdicia todo lo consumido hasta ahí.

## 📊 Logging y observabilidad

**Log estructurado JSONL**, mismo patrón que el repo de trading.

Con esta justificación específica: sin UI ni persistencia (ADR-007), **el log es la única
ventana al grafo**, y la demo consiste en proyectarlo mientras el pipeline corre. Eso lo
convierte en una decisión de producto, no de infraestructura: tiene que ser legible por una
persona en vivo *y* parseable después.

Como mínimo se registra: entrada y salida de cada nodo, el veredicto del gate con su conteo
de diagnostics, cada iteración del loop de revisión con qué cambió respecto de la anterior, y
la razón de terminación. El diseño concreto —qué campos, qué nivel de detalle, si hay una
vista de consola aparte del JSONL— es decisión pendiente del `ROADMAP.md`.

## 🚫 Anti-patrones prohibidos

| Anti-patrón | Por qué | Regla |
|---|---|---|
| `Process.Start` fuera de `Orchestrator.Agents` / `Orchestrator.Lsp` / `Orchestrator.LspServer` | Rompe la testeabilidad del grafo | Oro 2 |
| Comparar rutas de archivo como strings sin pasar por `WorkspacePaths` | Dos escrituras de la misma ruta se leen como archivos distintos, y un archivo con errores parece limpio | ADR-010 |
| Un test que invoca `claude -p` o un language server real | Consume cuota del plan Pro y vuelve la suite lenta e inestable | Oro 3 |
| `ANTHROPIC_API_KEY` en cualquier forma | La facturación tiene que correr contra la suscripción | ADR-001 |
| Confiar en que el agente dice que compiló | Es el problema que el proyecto existe para resolver | ADR-004 |
| Un ciclo del grafo sin límite de iteraciones | Agota la cuota en una corrida | ADR-003 |
| Consultar el gate antes de que el language server terminó de indexar | Falso verde: aprueba código que no compila | ADR-006 |
| Editar a mano algo en `output/` para que el pipeline avance | `output/` es desechable por construcción; si hace falta tocarlo, el orquestador no está haciendo su trabajo | ADR-008 |
| `DateTime.UtcNow` fuera de adaptadores | Vuelve no testeables los timeouts y los reintentos | Oro 4 |
