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

## Estado (2026-08-10) — Bloques 0 a 4 cerrados; próximo: Bloque 5

**El pipeline corre con agentes reales.** Los dos adaptadores que faltaban existen:
`Orchestrator.Agents` invoca `claude -p` y prepara el workspace, `Orchestrator.Lsp` es dueño del
proceso del servidor MCP y traduce sus diagnostics al tipo de dominio. El grafo no cambió una
línea — la frontera ya estaba probada del lado de adentro, que era la apuesta del Bloque 3 y
salió bien.

**218 tests**, sin red y sin `claude` en el `PATH`. Y una corrida real, reproducible con
`dotnet run --project src/Orchestrator.PipelineVerification`, en la que un error inyectado a
propósito vuelve al agente de su capa y la iteración siguiente lo corrige.

**R5 está cerrado, y resultó ser tres riesgos, no uno.** Los tres producen el mismo síntoma —un
agente que corre, contesta con seguridad y no tiene servidor de lenguaje— y ninguno levanta un
error. El detalle está abajo, en el riesgo y en la entrada del historial. **ADR-011 pasó a
Aceptada con cuatro correcciones**; era el único que quedaba en `Propuesta`, así que ya no hay
ninguno.

**Deuda D5 cobrada.** El hook `PreToolUse` de alcance de archivos existe, está versionado y tiene
tests que lo corren de verdad.

**Plazo: vie 2026-08-07 → lun 2026-08-24 (17 días).** Es el condicionante que ordena todas
las prioridades de abajo: hay tiempo para un pipeline que funcione de punta a punta sobre un
artefacto chico, y no lo hay para nada más.

**Lo próximo — Bloque 5.** La corrida completa sobre `specs/gestor-tareas.md`, con
`Orchestrator.Cli` como host. Lo que el Bloque 4 dejó explícitamente pendiente y ahora bloquea:
**el orquestador no arma el esqueleto de la solución generada** (`.slnx` y `.csproj`), y Roslyn
carga una solución, no una carpeta de archivos sueltos. Hoy ese esqueleto lo escribe el arnés de
verificación; decidir el layout del proyecto generado es trabajo del Bloque 5. Ver deuda D12.

---

## Plan general

| Bloque | Fechas | Contenido | Criterio de salida | Estado |
|---|---|---|---|---|
| **0** | vie 07/08 | Andamiaje documental (`CLAUDE.md`, `AI.md`, `DECISIONS.md`, `ROADMAP.md`, README stub) + `git init` | Los cinco documentos existen, las referencias cruzadas resuelven, commit inicial hecho | ✅ 07/08 |
| **1** | sáb 08 – lun 10/08 | `specs/gestor-tareas.md` + contrato de tools del servidor MCP + scope de los subagentes | El spec está escrito en formato SDD, el contrato tiene firmas y formato de `Diagnostic` cerrados, y los cuatro subagentes están definidos — con sus tres ADRs | ✅ 07/08 |
| **2** | sáb 09/08 | Servidor MCP de LSP sobre Roslyn LSP + `typescript-language-server` | Una consulta manual al servidor devuelve diagnostics reales de un `.cs` roto a propósito, **y** una consulta de `definition` devuelve la ubicación correcta. ADR-006 pasa a `Aceptada` o se revierte a OmniSharp | ✅ 09/08 |
| **3** | mié 12 – dom 16/08 | Esqueleto del grafo (estado, nodos, transiciones) + Spec Analyzer | El grafo corre end-to-end contra `FakeAgentRunner` y `FakeLanguageServer`, incluido el ciclo de revisión y las tres vías de terminación. La suite corre sin `claude` en el `PATH` | ✅ 09/08 |
| **4** | lun 17 – vie 21/08 | Agentes de capa (dominio, API .NET, React) + loop de revisión contra diagnostics reales | Un error inyectado a propósito hace volver el grafo al agente de esa capa, y la siguiente iteración lo corrige. Visible en el log | ✅ 10/08 |
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
objetivo y pasa a describir código real. *Hecho: la revisión se aplicó y `AI.md` marca ahora
qué proyectos existen y cuáles siguen siendo contrato.*

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

**Ninguna.** Los cinco puntos que el briefing dejó abiertos están cerrados: tres en el Bloque 1
(ADR-010, ADR-011, ADR-012) y los dos últimos en el Bloque 3 — ADR-014, estrategia de testing
del orquestador, y ADR-015, observabilidad del grafo.

> Los números corrieron: ADR-013 se usó en el Bloque 2 para el lenguaje y la topología del
> servidor MCP, que era la decisión que ADR-002 había dejado abierta "hasta el Bloque 2".

**Y ya no queda ninguno en `Propuesta`.** ADR-011 era el último: el Bloque 4 lo corrió headless,
encontró que cuatro de sus mecanismos no funcionaban como estaban descritos —ninguno de los cuatro
fallando con un error— y lo promovió a `Aceptada` con esas cuatro correcciones escritas. La
decisión de fondo se sostuvo entera; lo que cambió fue cómo se la hace efectiva.

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

**R5 — El agente headless corre sin las tools de LSP, en silencio.** ✅ **Cerrado el 2026-08-10.**
Era real, se reprodujo al primer intento, **y eran tres riesgos con el mismo síntoma en vez de uno**.
La hipótesis escrita —"el `.mcp.json` de proyecto pide aprobación interactiva"— nombraba bien el
síntoma y erraba la causa, así que la mitigación que proponía no alcanzaba.

| Vía | Qué pasa | Evidencia |
|---|---|---|
| Los settings de proyecto no se cargan en `-p` | `enabledMcpjsonServers` nunca se lee, el servidor queda `pending` | `mcp_servers: [{"name":"lsp","status":"pending"}]`, **cero** tools |
| `tools:` del frontmatter filtra las tools MCP | Con `tools: Read, Write, …` el agente ve cero tools de `lsp` aunque el servidor esté conectado | Sin el campo `tools`: las cinco. Con `tools: Read`: ninguna |
| Disponible ≠ permitida | El agente llama a la tool y la llamada pide autorización, que en headless nadie da | *"Necesito tu permiso para ejecutar `mcp__lsp__diagnostics`"* |

*Cómo se cerró:* el servidor se declara en la invocación (`--mcp-config`), los settings de proyecto
se cargan explícitamente (`--setting-sources project`), las plantillas de capa nombran las cinco
`mcp__lsp__*` en su `tools`, y la invocación las permite con `--allowedTools`. **Verificado
end-to-end:** un agente headless llamó a `diagnostics` y recibió el `CS1061` real del fixture roto a
propósito, con `status: ready` y `total: 5`.

*Lo que hay que llevarse, más allá del arreglo:* el diagnóstico se obtuvo del mensaje `init` de
`--output-format stream-json`, que lista servidores y tools **antes** de cualquier inferencia. Es
determinista y no depende de que el modelo reporte bien lo que ve — preguntarle al agente si tiene
tools es exactamente el tipo de evidencia que este proyecto decidió no aceptar en ningún otro lado.

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
| ~~D5~~ | ~~El alcance de archivos por agente es una convención del prompt, no una barrera~~ | ADR-011 | ✅ **Cobrada el 2026-08-10.** El hook vive en `templates/hooks/restrict-to-layer.js`, se pasa por invocación con la carpeta de esa capa, y tiene tests que lo ejecutan de verdad. El orquestador comprueba al arrancar que bloquea |
| D6 | Reabrir una tarea completada abriría un hueco en RN-01 y queda fuera del spec | `specs/gestor-tareas.md`, fuera de alcance | Solo si se amplía el artefacto de juguete, cosa que ADR-009 desaconseja |
| D7 | `workspaceSymbol` puede volver vacío mientras el índice de símbolos se calienta, y el contrato no distingue eso de "no existe" | Bloque 2, ADR-010 | Si un agente de capa concluye que una entidad no existe cuando sí existe. El gate no usa esta tool, así que no bloquea el pipeline |
| D8 | El servidor MCP está fijado a `win-x64`: el paquete de Roslyn LSP es específico por RID | Bloque 2, ADR-006 | Si el proyecto tiene que correr en otra plataforma. Es una línea del `.csproj`, no un rediseño |
| D9 | Volver a una capa anterior re-invoca también a las posteriores desde cero, aunque su código no haya cambiado | Bloque 3, `GraphRunner` | Si el costo por corrida se vuelve el problema. Requiere saber qué archivos tocó cada agente, que hoy el grafo no sabe |
| D10 | La detección de no-progreso es heurística cuando el gate trunca: la huella solo cubre la ventana visible | Bloque 3, ADR-014 | Si aparece una corrida real con cientos de diagnostics. Hoy la respalda el límite de iteraciones y el log registra `truncated` |
| D11 | El formato de salida del Spec Analyzer vive en dos lugares: el prompt de la plantilla y el parser | Bloque 3, ADR-014 | Si el parser empieza a fallar contra respuestas reales. *El Bloque 4 le dio la primera evidencia real y salió bien: el plan del agente parseó al primer intento.* Se cobra con un test que valide la plantilla contra los fixtures |
| **D12** | **El orquestador no arma el esqueleto de la solución generada** (`.slnx`, `.csproj`), y Roslyn carga una solución, no una carpeta de archivos sueltos. Sin esqueleto, el gate no analiza nada — y "no analiza nada" se ve igual que "está limpio" | Bloque 4 | **Bloquea el Bloque 5.** Hoy lo escribe el arnés de verificación; decidir el layout del proyecto generado es trabajo de ese bloque, y adelantarlo acá habría sido decidirlo de casualidad |
| D13 | El servidor de TypeScript se apaga en la verificación del Bloque 4: necesita estar instalado en el `node_modules` del workspace analizado, que en una app recién generada no existe | Bloque 4 | Bloque 5, junto con D12. Ojo con la interacción: el contrato contesta `indexing` para todo el scope mientras **algún** servidor indexa, así que un servidor que nunca puede quedar listo impide que el gate dé veredicto sobre el código que sí lo está |

---

## Historial completado

### ✅ Bloque 4 — Los adaptadores y la primera corrida con agentes reales (2026-08-10)

El bloque donde el proyecto deja de correr contra fakes. Dos adaptadores, 94 tests nuevos, y el
riesgo que venía arrastrándose desde el Bloque 1.

**Criterio de salida, cumplido.** Se pedía que un error inyectado a propósito hiciera volver el
grafo al agente de esa capa y que la iteración siguiente lo corrigiera, visible en el log.
Reproducible con `dotnet run --project src/Orchestrator.PipelineVerification`.

**La decisión de diseño del arnés vale más que el arnés.** El error no se planta a mano en
`output/` —`CLAUDE.md` lo prohíbe, y con razón: un error puesto por una persona es un error que el
pipeline nunca tuvo que sobrevivir— sino con un decorador sobre el runner real, que lo escribe
**después del primer turno del agente de dominio**, en un archivo que el gate está por leer. Llega
por la misma puerta por la que llegaría uno de verdad. Sembrarlo antes de la corrida habría probado
menos: el agente podía notarlo y arreglarlo en la primera pasada, y entonces el loop de revisión —lo
único que el arnés existe para mostrar— no habría corrido nunca.

**R5 era tres riesgos, no uno, y la hipótesis escrita tenía mal la causa.** Está desarrollado arriba,
en la sección de riesgos. Lo que conviene retener es la forma del hallazgo, no los tres arreglos:
**todos los mecanismos de seguridad de esta integración fallan abiertos**. Un servidor MCP sin
aprobar, una tool disponible pero sin permiso, un hook cuyo intérprete no está instalado: cada uno
degrada en silencio a "sin protección", y los tres se ven exactamente igual que el éxito. De ahí que
el orquestador ahora **sondee cada uno al arrancar** en vez de confiarse.

**El hallazgo más valioso del bloque lo produjo la corrida que falló.** La primera verificación
cumplió tres de sus cuatro puntos y se cayó en el último: el agente recibió el diagnostic, **corrigió
el archivo** —se puede ver en disco— y el gate siguió reportando el mismo error. El grafo terminó por
no-progreso habiendo progresado.

La causa era un defecto del Bloque 2 que solo un loop real podía exponer. Un language server
contesta sobre el texto que **le dieron**, no sobre el archivo: la sesión mandaba `textDocument/didOpen`
una sola vez por documento y no volvía a hablarle nunca, así que nada de lo que pasara en disco lo
alcanzaba. Es correcto para cualquier escenario que lea un archivo una vez —y la verificación manual
del Bloque 2 hacía exactamente eso—, y está roto en silencio para un loop de revisión.

**Es el falso verde al revés, y merece nombre propio: un falso rojo.** No aprueba código roto, así
que el instinto de "falla del lado seguro" lo deja pasar. Y es el mismo defecto: **el gate afirmando
algo que no es cierto**, esta vez quemando un turno pago en rehacer trabajo ya hecho, y —peor— dando
por agotado a un agente que estaba funcionando bien.

**El arreglo tenía una segunda trampa abajo, y es la firma de Roslyn: el silencio.** El protocolo
permite un `didChange` sin `range`, que significa "este es el documento nuevo entero". Roslyn no lo
implementa —desreferencia el `range` igual, tira `NullReferenceException` dentro de su cola de
requests— y a partir de ahí **deja de contestar todo, para siempre, sin cerrar la conexión ni
devolver un error**. Con el arreglo ingenuo puesto, la corrida se veía peor que sin arreglar. Lo
diagnosticó `--LspServer:TraceProtocol=true`, que es exactamente para lo que el Bloque 2 lo había
construido: es la tercera vez que este proyecto se topa con que **Roslyn no falla, se calla**.

Una reescritura completa se manda entonces como una edición incremental que cubre todo el texto
anterior. La lógica vive en `DocumentSynchronizer`, aparte, para testearla sin servidor real, y el
arnés del Bloque 2 ganó un paso 5 —arreglar el archivo en disco y volver a preguntar— que cierra la
regresión contra los dos servidores reales sin gastar cuota.

**Y abajo de esa había una tercera, que sí es un falso verde y es la peor de las tres.** La corrida
siguiente mostró el gate contestando `clean` con el error inyectado presente en disco. Causa:
**un archivo creado después de que la solución se cargó no está en el sistema de proyectos**, y
`textDocument/didOpen` no lo mete ahí — el servidor lo analiza suelto, o no lo analiza. Un archivo
que nadie analiza no reporta errores, y eso llega al gate como "compila". Es *el* modo de fallo que
el proyecto entero existe para evitar, y aparece exactamente en el escenario que más importa: **los
agentes crean archivos todo el tiempo.** Se cierra anunciando el alta con
`workspace/didChangeWatchedFiles` antes del `didOpen`, y tiene su paso 6 en el arnés del Bloque 2.

Vale registrar que la corrida lo disimuló sola y por eso casi se pasa por alto: el watcher propio de
Roslyn terminó viendo el archivo, y el gate siguiente sí reportó el error. O sea que era una
**carrera**, no una falla determinista — la peor forma de tener este bug, porque se arregla solo lo
suficiente como para que nadie lo mire.

**Y una cuarta, en el verificador mismo, que conviene dejar escrita porque es la más incómoda.** El
arnés daba por probado que "el gate vio el error inyectado" buscándolo en `blockingSample` del
evento del gate. Ese campo trae **tres** items elegidos sobre todo el workspace y ordenados por
ruta, así que cualquier cosa bajo `src/Api` ordena antes que `src/Domain` y el archivo inyectado no
aparecía nunca. El arnés reportaba `MAL` un punto que sí se cumplía. Es un recordatorio barato de
algo caro: **una verificación también puede estar rota, y una que falla de más entrena a ignorarla**
igual de rápido que una que aprueba de más. Ahora lee los contadores de la propia iteración de
revisión; `blockingSample` vuelve a ser lo que era, una comodidad para quien lee el log.

**Cuatro hallazgos más que ningún documento anticipaba:**

1. **El `tools:` del frontmatter de un subagente también filtra las tools MCP.** Las tres plantillas
   de capa decían `tools: Read, Write, Edit, Glob, Grep` y habrían producido agentes ciegos con el
   servidor conectado al lado. Es la clase de error que no se encuentra leyendo documentación: el
   campo se llama igual que el de las herramientas built-in y se comporta distinto de lo que uno
   asume.
2. **Ver una tool y poder ejecutarla son dos interruptores distintos.** El agente la llamaba y
   contestaba que necesitaba permiso — que en un transcripto se parece muchísimo a que la tool no
   exista.
3. **El campo `hooks` del frontmatter no se aplica; los hooks de `settings.json` sí.** ADR-011 había
   descartado explícitamente las reglas de sesión porque "no distinguen al agente de dominio del de
   API". El argumento era correcto y dejó de aplicar solo: el orquestador corre **un agente por
   proceso**, así que la sesión *es* el agente. La objeción no se pasó por alto, se disolvió al
   cambiar cómo se invoca.
4. **`pwsh` no está instalado en la máquina de desarrollo, y un hook que no se puede lanzar deja
   pasar la escritura.** La primera versión del hook era PowerShell y no bloqueó nada, sin ruido. El
   hook pasó a `node`, que ya era dependencia dura. De paso: el `pwsh tools/kill-language-servers.ps1`
   que `CLAUDE.md` documenta como red de seguridad tampoco correría en esta máquina.

**Lo que confirmó una apuesta anterior:** el grafo no cambió una sola línea para que los adaptadores
encajaran. La frontera estaba probada desde adentro con 124 tests, y del otro lado apareció un
proceso real sin que `Orchestrator.Application` se enterara. Es la regla de oro 3 cobrando su
segundo dividendo, después del de cronograma del Bloque 2.

**Y un dato que despeja D11:** el spec analyzer real produjo un plan que el parser leyó al primer
intento —6 tareas, todos los criterios cubiertos— contra un formato que hasta ahora solo se había
ejercitado con fixtures escritos a mano.

**Lo que quedó abierto, dicho:** D12 (el orquestador no arma el esqueleto de la solución generada) y
D13 (el servidor de TypeScript necesita un `node_modules` que todavía no existe). Las dos bloquean el
Bloque 5 y las dos son suyas: definen el layout del proyecto generado, que es precisamente lo que ese
bloque tiene que decidir.

### ✅ Bloque 3 — El grafo y el Spec Analyzer (2026-08-09)

Lo que más se evalúa del proyecto, cerrado antes de que empezara su ventana. Cinco proyectos
nuevos, 91 tests nuevos, y las dos últimas decisiones del briefing.

**Criterio de salida, cumplido.** Se pedía que el grafo corriera end-to-end contra fakes,
incluido el ciclo de revisión y las tres vías de terminación, y que la suite corriera sin
`claude` en el `PATH`. Las tres vías tienen su test, y hay una cuarta que ADR-003 no había
enumerado:

| Vía de terminación | Cómo se ejercita |
|---|---|
| Límite de iteraciones por nodo | El agente produce errores distintos hasta agotar el techo |
| No-progreso | El agente devuelve el mismo conjunto de diagnostics dos veces |
| Fallo terminal | Agente que no termina, plan que no parsea, diagnostic sin capa dueña |
| **Gate que nunca sale de `indexing`** | La cuarta, que no estaba en ADR-003 y que el Bloque 2 produjo de verdad |

Verificación literal: `dotnet test src/Orchestrator.slnx` con `claude` sacado del `PATH` →
124 tests, todo en verde, ninguna tanda por encima de un segundo.

**Las dos decisiones abiertas, cerradas.** ADR-014 y ADR-015. Con eso el briefing no deja
ningún punto pendiente.

**Tres hallazgos de diseño que valen más que el código:**

1. **El texto libre de los agentes casi no existe como problema, una vez visto de dónde
   viene.** Un agente de capa no le reporta al grafo: escribe archivos, y quien habla es el
   gate. Así que `IAgentRunner` devuelve cómo terminó la invocación, no qué dijo, y el
   transcripto queda para el log. **Prosa hay en un solo nodo** —el Spec Analyzer, cuyo plan
   *es* su producto— y ahí se concentra todo el parseo, en una función pura con respuestas
   grabadas. Era el problema que el prompt del bloque marcaba como el lado difícil; la salida
   fue no resolverlo sino disolverlo.
2. **Guionar el agente y el gate por separado es una trampa cómoda.** Dos guiones
   independientes pueden contradecirse, y entonces un test pasa describiendo una corrida
   imposible. Los dos fakes comparten un `FakeWorkspace`: el agente lo muta, el gate lo
   reporta. El grafo converge porque el agente reparó algo. Y "el agente no cambió nada" deja
   de ser un veredicto guionado y pasa a ser la definición literal del test de no-progreso.
3. **`indexing` obliga a esperar, y esperar obliga a un techo que ADR-003 no había pedido.**
   Tratar `indexing` como aprobación es el falso verde; tratarlo como "esperar y reconsultar"
   sin límite es un cuelgue — que es exactamente el fallo que el Bloque 2 produjo con Roslyn
   (ADR-013). Agotar el techo es fallo terminal, y lleva el `statusDetail` del propio servidor
   en la traza para que se pueda diagnosticar.

**Dos decisiones que cambiaron documentos existentes:**

- **El gate se consulta siempre sobre el workspace entero, no sobre la capa que acaba de
  trabajar.** Scopear por capa haría la corrida más prolija y escondería el caso que el
  proyecto existe para atrapar: el agente de API llamando a un método de dominio que no
  existe. Con veredicto global más `LayerMap`, la arista condicional puede devolverle el
  trabajo a la capa que realmente lo posee — hay test de que un error en `src/Domain/**`
  durante la etapa de API vuelve al agente de dominio.
- **`AI.md` enmendó su regla de oro 4:** el reloj es `TimeProvider` del BCL, no un `IClock`
  propio. Un `IClock` de solo `UtcNow` no cubría la espera entre reconsultas del gate.

**Un defecto encontrado en documentación propia, y corregido.** El encabezado de
`specs/gestor-tareas.md` afirmaba que *"todo `CA-nn` cita al menos una `RN-nn` existente"* y
ADR-012 repetía la afirmación — pero la tabla del propio spec tiene cinco criterios con `—` en
la columna *Verifica*, y su sección 6 los declara legítimos. El spec se contradecía a sí mismo,
y el ADR sostenía la versión equivocada. Los dos documentos ahora enuncian la invariante que se
sostiene y que además es la que puede romperse en silencio: **ninguna cita apunta a una regla
inexistente**, más identificadores únicos y correlativos. Lo verifica `SpecParser` contra el
spec real del repo, enlazado desde el `.csproj` de la suite. Encontrarlo al escribir el
validador es lo que ADR-012 esperaba del formato.

**Lo que quedó abierto, dicho:** deudas D9 (volver atrás re-invoca las capas posteriores),
D10 (no-progreso es heurístico cuando el gate trunca) y D11 (el formato del plan vive en el
prompt y en el parser).

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
