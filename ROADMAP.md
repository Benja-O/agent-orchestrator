# ROADMAP — Orquestador de agentes con LSP y grafos

> **Propósito:** mantener visibilidad del plan completo entre sesiones de trabajo. Cualquier
> sesión con Claude Code debe leer este archivo primero para entender en qué punto está el
> proyecto y qué corresponde hacer.
>
> **Reglas:**
> - Bloque completado: ✅ con fecha. En curso: 🔄. Pendiente: ⬜. Abortado: ❌ con la razón.
> - **Cada bloque tiene un criterio de salida verificable.** Un bloque no se marca ✅ porque
>   "ya está hecho", sino porque su criterio se cumplió y se puede mostrar.
> - `DECISIONS.md` es lo cerrado; este archivo es lo abierto. Cuando una decisión pendiente
>   se resuelve, se escribe su ADR y se saca de la sección "Decisiones pendientes".
> - Actualizar al retomar el proyecto y al cerrar cada bloque, en el mismo commit que el
>   trabajo.

---

## Estado (2026-08-09) — Bloques 0, 1 y 2 cerrados; próximo: Bloque 3

**La capa LSP funciona contra servidores reales.** `src/Orchestrator.LspServer/` expone las cinco
tools de [docs/mcp-contract.md](docs/mcp-contract.md) por HTTP, envolviendo Roslyn LSP y
`typescript-language-server`. ADR-006 y ADR-010 pasaron a `Aceptada` con evidencia, y ADR-013
cerró la última decisión abierta que quedaba del briefing sobre el stack.

**El Bloque 2 cerró cinco días antes de la fecha y sin usar el plan B.** El disparador de
reversión a OmniSharp era el mié 13/08; no hizo falta. Con eso, **el riesgo técnico concentrado
del proyecto (R3) está cerrado** y el margen acumulado —tres días del Bloque 1 más cinco de
éste— queda disponible para el Bloque 4, que es donde ahora vive el riesgo real (cuota del plan
Pro).

**Plazo: vie 2026-08-07 → lun 2026-08-24 (17 días).** Es el condicionante que ordena todas
las prioridades de abajo: hay tiempo para un pipeline que funcione de punta a punta sobre un
artefacto chico, y no lo hay para nada más.

**Lo próximo — Bloque 3.** El esqueleto del grafo y el Spec Analyzer, construidos contra
`FakeAgentRunner` y `FakeLanguageServer`. Es lo que más se evalúa, y ahora se construye sabiendo
exactamente qué forma tiene el veredicto que consume, porque existe.

---

## Plan general

| Bloque | Fechas | Contenido | Criterio de salida | Estado |
|---|---|---|---|---|
| **0** | vie 07/08 | Andamiaje documental (`CLAUDE.md`, `AI.md`, `DECISIONS.md`, `ROADMAP.md`, README stub) + `git init` | Los cinco documentos existen, las referencias cruzadas resuelven, commit inicial hecho | ✅ 07/08 |
| **1** | sáb 08 – lun 10/08 | `specs/gestor-tareas.md` + contrato de tools del servidor MCP + scope de los subagentes | El spec está escrito en formato SDD, el contrato tiene firmas y formato de `Diagnostic` cerrados, y los cuatro subagentes están definidos — con sus tres ADRs | ✅ 07/08 |
| **2** | sáb 09/08 | Servidor MCP de LSP sobre Roslyn LSP + `typescript-language-server` | Una consulta manual al servidor devuelve diagnostics reales de un `.cs` roto a propósito, **y** una consulta de `definition` devuelve la ubicación correcta. ADR-006 pasa a `Aceptada` o se revierte a OmniSharp | ✅ 09/08 |
| **3** | mié 12 – dom 16/08 | Esqueleto del grafo (estado, nodos, transiciones) + Spec Analyzer | El grafo corre end-to-end contra `FakeAgentRunner` y `FakeLanguageServer`, incluido el ciclo de revisión y las tres vías de terminación. La suite corre sin `claude` en el `PATH` | ⬜ |
| **4** | lun 17 – vie 21/08 | Agentes de capa (dominio, API .NET, React) + loop de revisión contra diagnostics reales | Un error inyectado a propósito hace volver el grafo al agente de esa capa, y la siguiente iteración lo corrige. Visible en el log | ⬜ |
| **5** | vie 21 – sáb 22/08 | Primera corrida completa: spec → app compilable | `output/` se genera de cero, la app compila, y el endpoint que intenta violar la invariante de ADR-009 la rechaza | ⬜ |
| **6** | dom 23 – lun 24/08 | Pulido, README real, `DECISIONS.md` al día, demo ensayada | El README explica la arquitectura del grafo, la integración LSP y la integración con Claude Code. La demo corre de principio a fin sin intervención | ⬜ |

**Los bloques 2 y 3 se solapan a propósito.** El grafo se construye contra
`FakeLanguageServer` mientras el servidor MCP real se termina — es la regla de oro 3 de
`AI.md` pagando dividendos de cronograma, no solo de costo. Si el Bloque 2 se atrasa, el 3
no se bloquea.

### Detalle por bloque

**Bloque 1 — spec y contrato.** Los dos ítems que desbloquean todo lo demás. El spec no se
puede analizar si no existe; el gate no se puede construir si no está definido qué devuelve.
Producen ADR-010 (contrato de tools y formato de diagnostics) y el ADR del formato de spec.

**Bloque 2 — la capa LSP.** El riesgo técnico concentrado del proyecto. Dos servidores de
lenguaje con ciclos de vida distintos, envueltos por un servidor MCP. La trampa a evitar está
escrita en `AI.md`: consultar antes de que termine el indexado devuelve un falso verde.

**Bloque 3 — el grafo.** Lo que más se evalúa. Máquina de estados propia (ADR-003) con las
tres vías de terminación obligatorias. Al cerrarlo, revisar `AI.md`: deja de ser arquitectura
objetivo y pasa a describir código real.

**Bloque 4 — los agentes.** Acá se define el scope de cada subagente y se paga la primera
cuota real del plan Pro. Es el bloque donde el riesgo de límite de uso se materializa.

**Bloque 5 — la corrida completa.** El criterio de salida no es "la app compila" sino "la app
compila **y** sostiene la invariante". Compilar es lo que verifica el gate de LSP; la
invariante es lo que verifica que el pipeline transmitió una regla de negocio a través de tres
capas (ADR-004, consecuencia final).

**Bloque 6 — la entrega.** Dos repos (ADR-008). El README del orquestador explica la
arquitectura; el de la app generada aclara que es output, no trabajo manual.

---

## Decisiones pendientes

De los cinco puntos que el briefing dejó abiertos, tres se cerraron en el Bloque 1 (ADR-010,
ADR-011, ADR-012). Quedan dos.

| # | Decisión | Se resuelve en | Produce |
|---|---|---|---|
| 4 | **Estrategia de testing del propio orquestador** (no de la app generada): cómo se valida que el grafo y el loop de revisión funcionan | Bloque 3 | ADR-014 |
| 5 | **Nivel de logging y observabilidad del grafo** para que la demo muestre el proceso con claridad | Bloque 3 (diseño) / Bloque 6 (pulido) | ADR-015 |

> Los números corrieron: ADR-013 se usó en el Bloque 2 para el lenguaje y la topología del
> servidor MCP, que era la decisión que ADR-002 había dejado abierta "hasta el Bloque 2".

Notas sobre el estado de cada una:

- **#4 está parcialmente resuelta de antemano** por la regla de oro 3 de `AI.md` (fakes, sin
  invocar la CLI real). Lo que falta es el diseño concreto: qué escenarios se testean, cómo
  se graban las respuestas del `FakeAgentRunner`, y cómo se verifica que la suite no está
  invocando nada real. Se le suma un caso concreto salido del Bloque 1: **el gate tiene que
  tratar `status: "indexing"` como "esperar", nunca como aprobación** (ADR-010), y eso se
  testea con `FakeLanguageServer`.
  **El Bloque 2 le dejó el patrón ya probado de un lado:** `FakeLanguageServerSession` sirve
  respuestas en forma de protocolo y toda la superficie de tools se ejercita contra él —
  33 tests en 2 segundos, sin proceso, sin red y sin indexado. La pregunta que queda abierta es
  la del otro lado, `FakeAgentRunner`, que es más difícil porque una respuesta de agente es
  texto libre y no una estructura.
- **#5** dejó de ser una decisión de infraestructura al cerrarse ADR-007: sin UI, el log es la
  única ventana al grafo y es lo que se proyecta en la demo. El Bloque 1 le agregó materia
  prima: los identificadores `RN-nn` / `CA-nn` del spec (ADR-012) hacen que el log pueda
  mostrar **qué regla se está implementando en qué capa**, en vez de solo qué nodo corre.

---

## Riesgos

**R1 — Límite de 5 h del plan Pro.** *El riesgo real de cronograma.* Un loop de revisión que
regenera código contra diagnostics consume cuota rápido, y se agota justo cuando más se
necesita: depurando. Se materializa en el Bloque 4.

- *Mitigación en firme:* regla de oro 3 de `AI.md` (la suite completa corre con fakes) más
  los límites de iteración de ADR-003. Todo lo que se pueda depurar sin agente real, se
  depura sin agente real.
- *Disparador de escalada:* si durante la semana 1 se pega contra el techo de forma sostenida
  —no una vez aislada— evaluar créditos de uso o subir a Max. La decisión se toma con el dato,
  no por precaución.

**R2 — Deriva de alcance de la app generada.** El artefacto de juguete es chico a propósito
(ADR-009). Agregarle features es la forma más fácil de perder el Bloque 5, porque cada
invariante extra es superficie nueva donde el agente puede fallar por razones que no son
culpa del orquestador. *Mitigación:* el alcance de la app está congelado en ADR-009; ampliarlo
requiere un ADR que lo justifique contra el cronograma.

**R3 — La integración de Roslyn LSP resulta más difícil de lo previsto.** ✅ **Cerrado el
2026-08-09.** No se necesitó el plan B: el paquete se obtiene del feed público de Visual Studio,
trae un ejecutable standalone, y `--stdio` funciona. ADR-006 pasó a `Aceptada` con la evidencia.
Lo que efectivamente costó tiempo no fue Roslyn sino la deserialización de parámetros de
JSON-RPC (ADR-013, última consecuencia) — un modo de fallo que no estaba en ninguna lista de
riesgos porque es silencioso.

**R5 — El agente headless corre sin las tools de LSP, en silencio.** Los servidores declarados
en un `.mcp.json` con scope de proyecto **piden aprobación interactiva** la primera vez. En
`claude -p` no hay quién apruebe, y el fallo no levanta un error: el agente simplemente trabaja
sin el servidor MCP y el pipeline degrada a generación a ciegas — precisamente lo que el
proyecto existe para evitar. Sería un fallo caro de diagnosticar porque todo *parece* funcionar.
*Mitigación:* el orquestador agrega el servidor a `enabledMcpjsonServers` en el `settings.json`
del workspace generado, y **verifica al arrancar que las tools están disponibles** en vez de
asumirlo (fallar rápido, `AI.md`). El endpoint `/health` del servidor MCP existe para esa
verificación.
*Se movió al Bloque 4.* Comprobarlo de verdad exige lanzar `claude -p`, y la regla de costo del
Bloque 2 lo prohibía explícitamente. El Bloque 4 corre agentes reales de todos modos: ahí se
paga una sola vez. **Sigue siendo el riesgo abierto más caro de diagnosticar del proyecto**, y
la primera cosa a comprobar del bloque, no la última.

**R4 — El gate verifica compilación, no corrección.** Una regla de negocio puede estar
ausente y el código compilar perfecto (ADR-004, consecuencia final). El proyecto ya lo tiene
contemplado —el endpoint que intenta violar la invariante de ADR-009 es exactamente esa
verificación— pero conviene que no se pierda de vista al llegar apurado al Bloque 5. Es la
diferencia entre "el pipeline produce código que compila" y "el pipeline produce la app
pedida".

---

## Deudas

Cosas conscientemente postergadas. Ninguna bloquea la entrega; todas son lo primero a
retomar si el proyecto continuara.

| # | Deuda | Origen | Trigger para cobrarla |
|---|---|---|---|
| D1 | Sin persistencia de corridas: una corrida interrumpida se reinicia desde cero | ADR-007 | Si el tiempo de una corrida completa crece a punto de volver caro reiniciar |
| D2 | Sin paralelismo entre agentes de capa: el grafo es estrictamente secuencial | ADR-003 | Si el tiempo de corrida se vuelve el cuello de botella de la iteración |
| D3 | El grafo es código, no configuración: cambiar el pipeline requiere recompilar | ADR-003 (alternativa descartada) | Cuando exista un segundo pipeline real que justifique la generalización |
| D4 | Sin gate de tests sobre la app generada, solo gate de compilación | ADR-004 (alternativa descartada) | Fuera del alcance de 2.5 semanas; es la extensión natural del gate |
| D5 | El alcance de archivos por agente es una convención del prompt, no una barrera | ADR-011 | **Tiene fecha: el hook `PreToolUse` se implementa en el Bloque 4.** Hasta entonces, un agente puede escribir fuera de su capa y nada lo detiene |
| D6 | Reabrir una tarea completada abriría un hueco en RN-01 y queda fuera del spec | `specs/gestor-tareas.md`, fuera de alcance | Solo si se amplía el artefacto de juguete, cosa que ADR-009 desaconseja |
| D7 | `workspaceSymbol` puede volver vacío mientras el índice de símbolos se calienta, y el contrato no distingue eso de "no existe" | Bloque 2, ADR-010 | Si un agente de capa concluye que una entidad no existe cuando sí existe. El gate no usa esta tool, así que no bloquea el pipeline |
| D8 | El servidor MCP está fijado a `win-x64`: el paquete de Roslyn LSP es específico por RID | Bloque 2, ADR-006 | Si el proyecto tiene que correr en otra plataforma. Es una línea del `.csproj`, no un rediseño |

---

## Historial completado

### ✅ Bloque 2 — La capa LSP detrás del servidor MCP (2026-08-09)

El bloque de riesgo técnico concentrado, cerrado cinco días antes de la fecha y sin usar el plan
B. La primera línea de código del proyecto, y la que decide si el resto tiene sentido.

**Criterio de salida, cumplido y superado.** Se pedían diagnostics reales de un `.cs` roto **y**
un `definition` correcto. Se obtuvo eso y además lo mismo del lado TypeScript:

| | C# — Roslyn | TypeScript — `typescript-language-server` |
|---|---|---|
| `diagnostics` | `CS1061 'Tarea' does not contain a definition for 'Cerrar'` en `Api/TareasController.cs:27` | `2339` en `src/tareasView.ts:13` |
| `definition` | `Domain/Tarea.cs:19`, **cruzando la frontera entre dos proyectos**, con firma `bool Tarea.Completar(IReadOnlyList<Tarea> prerequisitos)` | `src/tarea.ts:11`, con firma |
| `status` en frío | `indexing`, no una lista vacía | `indexing`, no una lista vacía |

Reproducible con `dotnet run --project src/Orchestrator.LspServer.ManualVerification`, que
arranca los servidores contra los fixtures rotos a propósito y comprueba las respuestas.

**Las dos decisiones abiertas, cerradas.** ADR-013: el servidor MCP en .NET con el SDK oficial,
como proceso propio dueño de los language servers, hablando LSP con `StreamJsonRpc`. ADR-006 y
ADR-010 promovidos a `Aceptada` con la evidencia escrita, no con una afirmación.

**Tres hallazgos que ningún documento anticipaba:**

1. **El modo de fallo real de esta integración es el silencio.** LSP pasa *un* objeto como todo
   el juego de parámetros, y JSON-RPC por defecto mapea propiedades a parámetros por nombre. Sin
   `UseSingleObjectParameterDeserialization`, StreamJsonRpc rechaza `workspace/configuration` —
   y Roslyn no falla: anota el error en su cola, nunca termina de cargar la solución, y el
   contrato contesta `indexing` para siempre sin que nada diga por qué. De ahí salió
   `--LspServer:TraceProtocol=true`: la única forma de distinguir *"no lo mandó"* de *"no lo
   enganchamos"*.
2. **Hay una segunda vía al falso verde y no tiene que ver con el timing: la normalización de
   rutas.** Nosotros emitimos `file:///F:/x/a.ts`, `typescript-language-server` contesta sobre
   `file:///f%3A/x/a.ts`. Como texto son archivos distintos, así que los diagnostics quedan
   archivados bajo una clave que nadie consulta **y el archivo parece limpio**. Generalizado
   como anti-patrón en `AI.md`, con test de regresión.
3. **El idioma de los diagnostics es una decisión de producto, no cosmética.** Roslyn los emite
   en el idioma de la máquina; esos mensajes no se quedan en el log, se pegan en el prompt del
   agente que tiene que arreglar el código. Se fijan con
   `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

**Verificación hecha:** la suite corre en 2 segundos, 33 tests, sin red, sin `claude` en el
`PATH` y sin arrancar ningún language server — la regla de oro 3 de `AI.md` verificada como
está escrita, no asumida. El arnés que sí arranca servidores reales vive fuera de la suite y se
llama para que eso sea evidente.

**Lo que quedó abierto, dicho:** R5 (aprobación de `.mcp.json` en headless) se movió al Bloque 4
porque comprobarlo exige `claude -p` y la regla de costo del bloque lo prohibía; deudas D7
(`workspaceSymbol` en frío) y D8 (fijado a `win-x64`).

### ✅ Bloque 1 — Spec, contrato MCP y scope de subagentes (2026-08-07)

Las tres decisiones que bloqueaban todo lo demás, cerradas tres días antes de la fecha. Cuatro
artefactos y tres ADRs.

- **[specs/gestor-tareas.md](specs/gestor-tareas.md)** — el spec SDD de entrada, en markdown
  humano con identificadores `RN-nn` / `CA-nn` (ADR-012). Escribirlo expuso una **ambigüedad
  del briefing**: *"no se puede completar una tarea si tiene una tarea dependiente sin
  completar"* invierte la regla leído literal y la vuelve absurda. El spec fija la lectura
  correcta —lo que bloquea a una tarea son sus prerrequisitos, no sus dependientes— con un
  ejemplo numerado. Se agregaron RN-02 (sin ciclos) y RN-03 (no eliminar tareas con
  dependientes); el caso de reabrir una tarea quedó explícitamente fuera de alcance, con la
  razón escrita.
- **[docs/mcp-contract.md](docs/mcp-contract.md)** — cinco tools, transporte HTTP y el esquema
  de `Diagnostic` (ADR-010). El campo más importante resultó ser `status: "ready" | "indexing"`:
  sin él, un language server indexando devuelve lista vacía y el gate aprueba código roto.
- **[templates/agents/](templates/agents/)** — los cuatro subagentes (ADR-011). El hallazgo que
  cambió el diseño: **el frontmatter no tiene campo de rutas**, así que el alcance de archivos
  se enforcea con un hook `PreToolUse`, no con `tools`. A cambio aparecieron `model` y
  `maxTurns` por agente, dos palancas directas sobre el riesgo de cuota.
- **[templates/generated-app-CLAUDE.md](templates/generated-app-CLAUDE.md)** — el `CLAUDE.md`
  que el orquestador inyecta en `output/`.

ADR-010 y ADR-011 quedaron en `Propuesta`: los hechos están verificados contra la documentación,
pero el conjunto corriendo headless no se probó en esta máquina. ADR-012 pasó directo a
`Aceptada` — el formato del spec es una decisión propia que ningún hecho externo puede refutar.

Verificación hecha: los identificadores del spec son únicos y correlativos y cada `CA-nn` cita
un `RN-nn` existente; los tres ADR nuevos resuelven contra el índice de `DECISIONS.md`; las
decisiones pendientes bajaron de cinco a dos.

### ✅ Bloque 0 — Andamiaje documental (2026-08-07)

Cinco documentos y el repo git, antes de escribir código. La razón de invertir el primer día
acá: en este proyecto el registro de decisiones **es parte del entregable evaluado**, no
documentación de apoyo.

- **`DECISIONS.md`** — ADR-001..009, las decisiones que ya venían tomadas del briefing. Se
  declaró en el encabezado la excepción a la regla de casa de "no hay ADR sin código
  asociado", con sus tres razones. ADR-006 quedó en `Propuesta`, no `Aceptada`: la
  superioridad de Roslyn LSP sobre OmniSharp es conocimiento del ecosistema, no algo
  verificado en este proyecto.
- **`AI.md`** — arquitectura objetivo y cuatro reglas de oro, cada una con su forma de
  verificarse. Explícitamente marcado como contrato a cumplir, no descripción de algo
  existente; se revisa al cierre del Bloque 3.
- **`CLAUDE.md`** — comportamiento, control de costo, y la distinción entre este archivo y la
  plantilla `templates/generated-app-CLAUDE.md` que el orquestador inyecta en el workspace
  generado. Son dos cosas distintas con el mismo nombre y confundirlas es un error
  conceptual.
- **`ROADMAP.md`** — siete bloques fechados con criterio de salida verificable, las cinco
  decisiones pendientes del briefing, cuatro riesgos y cuatro deudas.
- **`README.md`** — stub. Se completa en el Bloque 6, cuando haya arquitectura real.

Verificación hecha: los ADR citados desde otros documentos resuelven contra `DECISIONS.md`
(ADR-010..014 aparecen solo como referencias hacia adelante en la tabla de decisiones
pendientes); los cinco puntos abiertos del briefing están en este archivo y en ninguno de los
otros; `git status` limpio.
