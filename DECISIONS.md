# DECISIONS — Architecture Decision Records

> **Propósito:** registro de decisiones arquitectónicas del orquestador de agentes. Cada
> entrada explica QUÉ se decidió, POR QUÉ, y QUÉ alternativas se consideraron y descartaron.
> Complementa `AI.md` (el "cómo" técnico: arquitectura, convenciones, anti-patrones) y
> `CLAUDE.md` (el "cómo" operativo); acá va el "por qué".
>
> **Cuándo escribir un ADR:** cuando hay código o configuración en el repo cuya forma o
> existencia no se entendería sin el ADR — una decisión de diseño con consecuencias en
> varios componentes, un patrón que se repite, una divergencia deliberada.
>
> **Excepción declarada de este repo — los ADR-001..009 se escribieron antes del código.**
> La regla de arriba, heredada de otros repos del autor, dice que un ADR sin cambio asociado
> en el repo probablemente no es un ADR. Acá se rompe a propósito y por tres razones: (1) en
> este proyecto el registro de decisiones **es parte del entregable evaluado**, no
> documentación de apoyo; (2) las nueve decisiones ya estaban tomadas antes de abrir el
> repo, con alternativas descartadas reales, así que no son especulación; (3) fijarlas por
> adelantado es justamente lo que impide que el código las erosione en silencio bajo presión
> de cronograma. A partir de ADR-010 rige la regla normal.
>
> **Reglas:**
> - Entradas en orden cronológico inverso (la más reciente primero).
> - Cada entrada tiene fecha, estado, contexto, decisión, alternativas, consecuencias.
> - Estados posibles: `Propuesta` (decidida en el papel, sin verificar contra la realidad),
>   `Aceptada`, `Revertida/Supersedida en ADR-NNN`. Las revertidas no se borran.
> - Identificador correlativo `ADR-NNN`.
> - Al agregar un ADR, actualizar también la tabla de índice.

## Índice

| ADR | Título corto | Área | Estado |
|---|---|---|---|
| ADR-015 | Observabilidad del grafo: eventos tipados con doble lectura, JSONL y consola | Observabilidad / Producto | Aceptada |
| ADR-014 | Estrategia de testing del orquestador: escenario compartido y una sola frontera de texto libre | Testing / Costo | Aceptada |
| ADR-013 | El servidor MCP en .NET, como proceso propio dueño de los language servers | LSP / Stack | Aceptada |
| ADR-012 | Formato del spec SDD: markdown humano con identificadores estables | Spec / Entrada | Aceptada |
| ADR-011 | Scope de los subagentes de capa: tools, modelo, límites y alcance de archivos | Agentes / Costo | **Propuesta** |
| ADR-010 | Contrato del servidor MCP de LSP: tools, transporte HTTP y formato de `Diagnostic` | LSP / Integración | Aceptada |
| ADR-009 | Artefacto de juguete: gestor de tareas con dependencias, no un CRUD vacío | Alcance / Validación | Aceptada |
| ADR-008 | Dos repos separados: el orquestador y la app que genera | Entrega / Repositorio | Aceptada |
| ADR-007 | El spec entra por argumento de CLI; sin UI ni persistencia | Interfaz / Alcance | Aceptada |
| ADR-006 | Servidor de lenguaje C#: Roslyn LSP en lugar de OmniSharp | LSP / Infraestructura | **Propuesta** |
| ADR-005 | El LSP se expone a los agentes como servidor MCP, no como tool interna | LSP / Integración | Aceptada |
| ADR-004 | LSP como fuente de verdad del gate, no la salida de `dotnet build` | LSP / Arquitectura | Aceptada |
| ADR-003 | Máquina de estados propia, no LangGraph ni framework de grafos | Grafo / Arquitectura | Aceptada |
| ADR-002 | Orquestador en .NET, no Python ni TypeScript | Lenguaje / Stack | Aceptada |
| ADR-001 | Claude Code CLI headless (`claude -p`), no la API de Anthropic | Agentes / Costo | Aceptada |

---

## ADR-015 — Observabilidad del grafo: eventos tipados con doble lectura, JSONL y consola
**Fecha:** 2026-08-09
**Estado:** Aceptada
**ADRs relacionados:** ADR-007 (sin UI, el log es la única ventana), ADR-012 (los identificadores que lo vuelven trazable), ADR-003 (las razones de terminación que registra), ADR-014 (cómo se testea).

### Contexto
Era la última decisión abierta del briefing junto con ADR-014, y la que menos parecía una decisión de arquitectura. Lo es por una razón concreta que ADR-007 dejó escrita: **sin UI y sin persistencia, el log es el único lugar donde una corrida existe**, y es lo que se proyecta en la demo. Eso lo convierte en decisión de producto.

El requisito tiene dos mitades que tiran en direcciones opuestas. Una persona mirando la pantalla mientras el pipeline corre necesita líneas cortas y jerarquizadas. Un análisis posterior —por qué se trabó, cuántas iteraciones tomó cada capa— necesita campos tipados. Un log que sirve bien a una de las dos mitades suele servir mal a la otra, y un log que intenta las dos suele terminar con las dos versiones describiendo corridas distintas.

### Decisión

**1. Un jerarquía de eventos tipados en `Orchestrator.Domain`, y cada evento sabe renderizarse.** `RunEvent` obliga a dos cosas: `Event`, el nombre estable que lee una máquina, y `Summary`, la línea que lee una persona. Las dos salen del mismo objeto.

Eso es lo que impide la deriva. La alternativa habitual —un logger estructurado por un lado y `Console.WriteLine` por el otro— deja dos descripciones del mismo hecho mantenidas en dos lugares, y la de consola es siempre la que queda vieja. Acá no se pueden desincronizar porque no son dos.

**2. Once eventos, elegidos por lo que hay que poder responder después:**

| Evento | La pregunta que contesta |
|---|---|
| `run-started` | Qué spec, cuántas reglas y criterios |
| `plan-produced` | En cuántas tareas por capa se descompuso, **y qué criterios quedaron sin cubrir** |
| `node-entered` | Qué nodo, qué intento, **y qué `RN-nn` está implementando esa capa** |
| `agent-invoked` / `agent-returned` | A quién se llamó, con cuántos diagnostics, cómo terminó, cuánto tardó |
| `gate-waiting-for-index` | Que el gate esperó en vez de aprobar, y qué dijo el servidor |
| `gate-evaluated` | El veredicto: total, truncado, errores, warnings, la huella y una muestra |
| `review-iteration` | **Qué cambió respecto de la iteración anterior** |
| `run-terminated` | Por qué paró, dónde, y la traza completa de nodos |

**3. El evento que justifica el diseño es `review-iteration`.** "El agente corrió otra vez" no dice nada; "el agente resolvió cuatro errores e introdujo uno" es el pipeline funcionando —o no— de forma visible. Se registra `resolved`, `introduced` y `persisting`, que salen de comparar la huella del veredicto con la anterior. Es la misma comparación que alimenta la detección de no-progreso, así que el log muestra exactamente el dato sobre el que el grafo decidió.

**4. Los identificadores del spec viajan en el log.** ADR-012 fijó `RN-nn` / `CA-nn` argumentando que hacen trazable el pipeline; acá se cobra: `node-entered` lleva las reglas de la capa y `plan-produced` lleva los criterios que ninguna tarea reclamó. La demo puede decir *"el agente de dominio está implementando RN-01, RN-02 y RN-03"* en vez de *"corriendo nodo domain-implementation"*.

**5. Dos observadores sobre la misma secuencia**, en `Orchestrator.Observability`: `JsonlRunObserver` escribe una línea JSON por evento con `timestamp`, `run` y `event` siempre primero y siempre en ese orden; `ConsoleRunObserver` escribe `Summary`. `CompositeRunObserver` los combina. El JSONL descarta `summary` —es la misma información en otra forma— y las duraciones van en milisegundos numéricos, con la clave nombrada por su unidad.

**6. La consola filtra, el archivo no.** Las primeras dos esperas de indexado son normales y no se muestran; de la tercera en adelante sí. El JSONL las registra todas. Un `indexing` eterno es el fallo silencioso más caro del proyecto (ADR-013): en el archivo tiene que estar entero, en la pantalla tiene que aparecer cuando deja de ser rutina.

### Alternativas
- **Un logger estructurado de librería (Serilog, `Microsoft.Extensions.Logging`)** → descartado, y es la alternativa que más cerca estuvo. Da sinks, niveles y enriquecimiento gratis. Se descartó porque lo que este proyecto necesita del log no son niveles sino **un vocabulario cerrado de hechos del grafo**: con un logger genérico, `review-iteration` es una plantilla de string con parámetros y nada garantiza que se emita completa, mientras que como tipo el compilador lo exige. El costo real de la decisión es que no hay sinks: si en el Bloque 6 hiciera falta uno, `IRunObserver` es la costura donde entraría.
- **Solo JSONL, y que la demo se mire con `jq`** → descartado: la demo se proyecta en vivo y nadie lee JSON crudo en una pantalla compartida.
- **Solo consola, y reconstruir después leyendo el texto** → descartado por lo mismo al revés: convierte el análisis posterior en parseo de prosa, que es justamente lo que ADR-014 logró eliminar de todo el resto del sistema.
- **Registrar el transcripto completo de cada agente en el log** → descartado. Está en `AgentOutcome` y disponible para quien depure, pero volcarlo al JSONL haría el archivo ilegible y a la vez inútil: el texto del agente es lo único sobre lo que el grafo **no** decide (ADR-014), así que no explica ninguna transición.

### Consecuencias
- **El log es un entregable y se testea como tal.** Hay tests sobre el orden de las claves, sobre que los identificadores salgan como strings y no como objetos, sobre que `summary` no se duplique, y sobre que cada evento produzca una línea de consola no vacía. Un log que se rompe en silencio es un log que no está.
- **`Orchestrator.Application` no escribe archivos.** Los eventos son del dominio; escribirlos es del adaptador. Por eso existe `Orchestrator.Observability` como proyecto propio en vez de un `StreamWriter` dentro del `GraphRunner`.
- **El `GraphRunner` recibe `IRunObserver` por constructor y nunca `null`.** `NullRunObserver` existe para el caso de no querer log; que sea explícito evita el patrón de comprobar nulos en cada punto de emisión.
- El pulido —colores, agrupamiento, quizá un resumen final— queda para el Bloque 6, que es donde el ROADMAP lo tenía previsto. Lo que este bloque cierra es el vocabulario, que es lo que después no se puede cambiar sin romper el análisis.

## ADR-014 — Estrategia de testing del orquestador: escenario compartido y una sola frontera de texto libre
**Fecha:** 2026-08-09
**Estado:** Aceptada
**ADRs relacionados:** ADR-001 (por qué la cuota es una restricción de diseño), ADR-003 (las tres vías de terminación que hay que poder testear), ADR-010 (el `status` que el gate no puede malinterpretar), ADR-012 (los identificadores que el spec tiene que sostener).

### Contexto
La regla de oro 3 de `AI.md` ya fijaba el **qué** desde el Bloque 0 —fakes, sin invocar la CLI real— con una razón de costo y no de estilo: el límite de 5 h del plan Pro se agota justo depurando la máquina de estados (ADR-001). Faltaba el **cómo**: qué escenarios se testean, cómo se graban las respuestas del `FakeAgentRunner`, y cómo se verifica que la suite no está invocando nada real.

El Bloque 2 dejó resuelto un lado y probado que el patrón funciona: `FakeLanguageServerSession` sirve respuestas en forma de protocolo, y toda la superficie de tools se ejercita contra él en 33 tests y 2 segundos. El lado difícil era el otro, y la dificultad es concreta: **una respuesta de agente es texto libre, no una estructura.** No hay una forma de protocolo que un fake pueda servir.

### Decisión

**1. El texto libre cruza a estructura en exactamente un lugar, y ese lugar es una función pura.**

La salida al problema fue notar que el grafo casi nunca lee lo que el agente dice. Un agente de capa **no le reporta al grafo**: escribe archivos, y quien habla es el gate (ADR-004). Así que `IAgentRunner` devuelve un `AgentOutcome` con cómo terminó la invocación —completó, agotó turnos, timeout, falló— y el transcripto **para el log, no para decidir**.

Queda un solo nodo cuya salida *es* prosa: el spec analyzer, cuyo plan es su producto. Ahí sí hay un parser, `PlanParser`, y ahí sí hay respuestas grabadas. Todo el problema del texto libre queda concentrado en una función sin estado, que se testea contra archivos.

Consecuencia práctica: `FakeAgentRunner` no tiene que fabricar prosa creíble para nueve de cada diez turnos. Solo tiene que hacer lo que hace un agente real: **tocar el workspace**.

**2. Las respuestas se graban como archivos de texto, una por escenario, incluidas las malformadas.**

Viven en `src/Orchestrator.Application.Tests/Fixtures/spec-analyzer/`. Hay un plan bien formado que cubre los trece `CA-nn` del spec real, y cuatro formas de estar mal que valía la pena grabar porque son las que un modelo produce de verdad: inventar una capa (`persistencia`), citar identificadores que no existen, proponer una tarea que no se atribuye a nada, y contestar con una pregunta en vez de un plan. Un quinto archivo graba la variación inocua —bloque de código, viñetas con asterisco, guion corto, capa en mayúscula— que el parser tiene que tolerar sin quejarse.

**El spec no se copia: se enlaza.** El `.csproj` referencia `specs/gestor-tareas.md` del repo, así que un cambio que rompa las invariantes de ADR-012 rompe la suite en vez de alejarse en silencio de aquello contra lo que el pipeline está testeado.

**3. Los dos fakes comparten un escenario, y esa es la decisión central.**

La forma obvia de testear un loop de revisión es guionar el agente y el gate por separado: el agente devuelve una secuencia de respuestas, el gate una secuencia de veredictos. **Es una trampa**, y vale nombrarla porque es cómoda: los dos guiones pueden contradecirse, así que un test puede pasar describiendo una corrida que no podría ocurrir — un agente que "arregló" algo que el gate nunca vio roto.

En su lugar hay un `FakeWorkspace`. `FakeAgentRunner` lo muta como lo mutaría un agente real y `FakeLanguageServer` reporta lo que hay adentro. El grafo converge porque el agente reparó algo, no porque el guion dijera que el próximo veredicto era limpio. Y **"el agente no cambió nada" deja de ser un veredicto guionado y pasa a ser la definición literal del test de no-progreso** — que es, además, la falla que todo el proyecto existe para atrapar (ADR-004).

**4. Los escenarios que la suite cubre**, elegidos por riesgo y no por cobertura:

| Escenario | Qué protege |
|---|---|
| Las tres capas en orden, todo limpio | El camino feliz, y que el orden de capas se respete |
| El gate encuentra errores y la siguiente iteración los corrige | El ciclo de revisión completo, con los diagnostics llegando al prompt |
| Un error en `src/Domain/**` durante la etapa de API | **La arista característica**: vuelve al agente de dominio, no al que corría |
| El agente devuelve los mismos diagnostics dos veces | Terminación por no-progreso (ADR-003) |
| El agente produce errores distintos hasta el techo | Terminación por límite de iteraciones (ADR-003) |
| El agente no termina: error, `maxTurns`, timeout | Terminación por fallo terminal, con traza |
| **El gate contesta `indexing` con lista vacía sobre un workspace roto** | El falso verde. El test más importante del bloque |
| El gate contesta `indexing` con una lista parcial | La segunda forma del mismo error |
| El gate contesta `indexing` para siempre | Que esperar tenga techo y la corrida se detenga con el `statusDetail` del servidor |
| Un diagnostic en un archivo de ninguna capa | Que no se descarte en silencio |
| El plan no parsea, y parsea al segundo intento | El reintento del único nodo que devuelve prosa |
| El spec se contradice a sí mismo | Que el input roto se detecte antes de gastar un turno |

**5. Cómo se verifica que la suite no invoca nada real**, que era la tercera pregunta abierta. Tres mecanismos, de más débil a más fuerte:

- **Tests de arquitectura**, en `ArchitectureTests`. Las reglas de oro dejan de ser greps que alguien tiene que acordarse de correr y pasan a fallar el build: `Domain` y `Application` no referencian ningún otro ensamblado del repo; ningún tipo suyo menciona `System.Diagnostics.Process` en su superficie; **toda implementación de `IAgentRunner` e `ILanguageServerGateway` alcanzable desde la suite vive en `Orchestrator.TestSupport`**; y el `GraphRunner` recibe su reloj por constructor.
- **El tiempo total.** 124 tests, ninguna suite por encima de un segundo. Una suite que tarda minutos está invocando algo real, y eso se ve sin analizar nada.
- **Correr la suite con `claude` fuera del `PATH`.** Es la verificación literal de la regla, hecha en vez de asumida.

**6. El reloj es `TimeProvider` del BCL, no un `IClock` propio.** Esto **enmienda la regla de oro 4 de `AI.md`**, que nombraba una interfaz propia. `TimeProvider` es la abstracción estándar desde .NET 8, cubre lectura y espera en la misma pieza —un `IClock` de solo `UtcNow` no habría cubierto la espera entre reconsultas del gate— y no hay que mantenerla. Lo que la regla protege no cambia: nada de `DateTime.UtcNow` fuera de adaptadores.

El fake es propio y mínimo: `SteppingTimeProvider` avanza un paso fijo en cada lectura, lo que da timestamps deterministas y duraciones no nulas en el log sin esperar. Deliberadamente **no** falsea temporizadores. La única espera real del sistema —el gate reconsultando mientras un servidor indexa— se ejercita con el delay en cero, porque lo que hay que testear ahí es el techo de intentos; que la espera efectivamente ocurra se cubre aparte, con el reloj real y un presupuesto de milisegundos.

### Alternativas
- **Guionar el agente y el gate por separado** → descartado arriba, con la razón. Es más simple de escribir y permite tests que describen corridas imposibles.
- **Que `FakeAgentRunner` escriba archivos de verdad en un directorio temporal, y que el fake del gate los lea** → descartado. Es más fiel y bastante más caro: obliga a que el fake del gate entienda algo de C# o de TypeScript para producir diagnostics, o a inventar un lenguaje de juguete. El `FakeWorkspace` en memoria da la misma garantía de consistencia —una sola fuente de verdad compartida— sin ese costo. La fidelidad que falta la cubre la verificación manual del Bloque 2, que sí usa servidores reales.
- **Grabar respuestas de agente reales, corriendo `claude -p` una vez y guardando la salida** → descartado *para este bloque*, no en general. Es lo que hace un test de contrato honesto, y la regla de costo del bloque lo prohibía explícitamente. Cuando el Bloque 4 corra agentes de verdad, la salida real del spec analyzer debería reemplazar al fixture escrito a mano — y si difiere, eso es un hallazgo, no un ajuste.
- **Una librería de mocking** → descartado: los fakes acá tienen comportamiento —el escenario compartido es el punto— y eso es una clase, no una expectativa configurada.
- **`Microsoft.Extensions.TimeProvider.Testing` para el `FakeTimeProvider`** → descartado por una razón práctica: su modelo obliga a que alguien avance el reloj para que un temporizador dispare, y con el grafo esperando dentro de un `await` eso significa correr la corrida en paralelo y bombear el reloj desde afuera. Treinta líneas propias sin temporizadores dan tests deterministas y legibles.

### Consecuencias
- **El grafo no puede distinguir un agente real de un fake, y eso es la garantía.** La regla de oro 3 se cumple por construcción, no por disciplina: no hay nada en `Application` que sepa que del otro lado hay un proceso.
- **El transcripto del agente queda como dato de log y de nada más.** Está en `AgentOutcome` y se puede leer al depurar, pero ninguna transición depende de él. Si alguna vez el grafo empieza a parsear texto de un agente de capa, esta decisión se rompió.
- **La suite tiene un costo fijo nuevo: mantener el formato de salida del spec analyzer en dos lugares.** El parser lo espera y `templates/agents/spec-analyzer.md` lo instruye. Es la clase de duplicación que se desincroniza, y la mitigación es que los fixtures son literalmente lo que el prompt pide.
- **Los tests de arquitectura son débiles en un punto y conviene decirlo:** verifican la superficie de los tipos, no el cuerpo de los métodos. Una llamada a `Process.Start` escondida dentro de un método de `Application` no la detectarían. Lo que sí la detecta es que `Orchestrator.Application` no referencia nada que la haga posible.
- Aparece `Orchestrator.TestSupport` como proyecto de producción-que-no-es-producción. Solo lo referencian proyectos de test; el test de arquitectura que exige que todas las implementaciones de las interfaces vivan ahí es, de paso, el que detectaría si eso dejara de ser cierto.

## ADR-013 — El servidor MCP en .NET, como proceso propio dueño de los language servers
**Fecha:** 2026-08-09
**Estado:** Aceptada
**ADRs relacionados:** ADR-002 (dejó esta decisión explícitamente abierta "hasta el Bloque 2"), ADR-005 (por qué hay un servidor MCP), ADR-010 (qué contrato implementa), ADR-006 (qué language servers envuelve).

### Contexto
ADR-002 eligió .NET para el orquestador y dejó abierto el lenguaje del servidor MCP, con un argumento correcto: *"es un proceso separado que habla un protocolo, no una dependencia de compilación"*. La decisión se aplazó hasta tener el dato que faltaba —qué SDK de MCP hace el trabajo más simple— y ese dato apareció al empezar el Bloque 2.

Junto con el lenguaje había que cerrar dos cosas que ADR-010 dejó implícitas y que el código obligó a explicitar: **qué proceso es dueño de los language servers** y **con qué librería se les habla LSP**.

### Decisión

**1. El servidor MCP se escribe en .NET**, con el SDK oficial `ModelContextProtocol` 2.1.0 y `ModelContextProtocol.AspNetCore` 2.1.0, que da el transporte HTTP que ADR-010 exige sin adaptadores intermedios. Vive en `src/Orchestrator.LspServer/`.

No hay ninguna ventaja que compense traer una segunda toolchain: el SDK de .NET es de primera clase (lo mantiene Microsoft junto con Anthropic), y mantener un solo lenguaje deja las convenciones de `AI.md` valiendo en todo el repo.

**2. El servidor MCP es un proceso propio y es el dueño de los dos language servers.** `Orchestrator.Lsp` —del lado del orquestador— lo lanza y lo consulta como cliente MCP.

Esto **enmienda la consecuencia de ADR-010** que decía "tres procesos que administrar en `Orchestrator.Lsp`". No pueden ser tres procesos administrados desde ahí: el que sostiene las conexiones LSP es quien contesta las tool calls, y ése es el servidor MCP. La jerarquía real es orquestador → servidor MCP → los dos language servers. La obligación de apagado determinista no cambia de dueño, cambia de lugar.

Se consideró **hospedarlo in-process** dentro del CLI (Kestrel en el proceso del orquestador). Habría dado una garantía más fuerte de que el gate y el agente ven lo mismo —serían el mismo objeto, no el mismo servidor— y un proceso menos que apagar. Se descartó por dos razones: mete ASP.NET Core dentro de `Orchestrator.Cli`, y sobre todo **el Bloque 2 tenía que poder verificarse a mano antes de que el orquestador existiera**. Un servidor que se puede arrancar solo y consultar solo es más fácil de depurar, y depurarlo fue exactamente el trabajo de este bloque.

**3. El cliente LSP es `StreamJsonRpc`, con los tipos del protocolo escritos a mano.**

El candidato obvio era `OmniSharp.Extensions.LanguageClient`, que da modelos LSP tipados. Se descartó con un dato: **su último release es de septiembre de 2023.** Es una librería distinta del servidor `omnisharp-roslyn` que ADR-006 descartó, pero viene de la misma organización y está igual de detenida — construir la pieza central del proyecto sobre eso reproduce el problema que ADR-006 quiso evitar, y habría que defenderlo en la entrevista.

`StreamJsonRpc` es de Microsoft, está activa, y `HeaderDelimitedMessageHandler` da el framing `Content-Length` de LSP directamente. El argumento que cierra la discusión: **el propio Roslyn LSP la trae adentro** (`StreamJsonRpc.dll` está en su payload), así que los dos extremos del pipe hablan la misma implementación de JSON-RPC. El costo es tipar a mano los mensajes que usamos, que son pocos — y los métodos custom de Roslyn (`solution/open`, `workspace/projectInitializationComplete`) había que declararlos a mano con cualquier librería.

### Alternativas
- **TypeScript/Node para el servidor MCP** → descartado. El SDK de MCP en TS es el de referencia y el ecosistema de language servers vive más cerca de Node, pero la mitad difícil del problema es C#, y partir el sistema en dos lenguajes para ahorrar nada de fricción no se justifica.
- **Python** → descartado por lo mismo, con menos argumentos a favor todavía.
- **Servidor MCP hospedado in-process en el CLI** → descartado arriba, con la razón. Es la alternativa que más cerca estuvo.
- **`OmniSharp.Extensions.LanguageClient`** → descartado arriba, con la fecha.
- **Un cliente LSP escrito desde cero sobre `Stream`** → descartado: JSON-RPC con correlación de ids, cancelación y manejo de errores es exactamente lo que `StreamJsonRpc` ya resuelve bien.

### Consecuencias
- **`Process.Start` aparece ahora en dos proyectos**, no en uno: `Orchestrator.Agents` (la CLI de Claude Code) y `Orchestrator.LspServer` (los language servers). La regla de oro 2 de `AI.md` se actualizó para decirlo. Lo que no cambia es lo que la regla protege: `Domain` y `Application` siguen sin conocer ningún proceso.
- **El servidor MCP no depende de `Orchestrator.Domain`.** Es agnóstico del proyecto que analiza, como ADR-010 pidió al sacarle el campo `layer`. Eso lo vuelve reutilizable y, más concretamente, testeable solo.
- **La trampa de `UseSingleObjectParameterDeserialization`.** LSP pasa **un** objeto como todo el juego de parámetros; JSON-RPC por defecto mapea las propiedades del objeto a parámetros por nombre. Sin ese flag en cada método que el servidor nos llama a nosotros, StreamJsonRpc rechaza la llamada con *"an argument was not supplied for a required parameter"*. **Y el fallo es silencioso donde importa:** rechazar `workspace/configuration` no rompe nada visible — Roslyn anota un error en su propia cola y nunca termina de cargar la solución, así que el contrato contesta `indexing` para siempre y nada dice por qué. Costó la mayor parte del tiempo de depuración del bloque. De ahí salió `--LspServer:TraceProtocol=true`, que vuelca el tráfico LSP crudo: es la única forma de distinguir "no lo mandó" de "no lo enganchamos".

## ADR-012 — Formato del spec SDD: markdown humano con identificadores estables
**Fecha:** 2026-08-07
**Estado:** Aceptada
**ADRs relacionados:** ADR-009 (qué describe el spec), ADR-007 (cómo entra al orquestador).

### Contexto
El Spec Analyzer parsea un documento de entrada. La forma de ese documento decide qué clase de nodo es: si el spec ya viene estructurado campo por campo, "analizarlo" es deserializarlo.

Hay una tensión real entre dos requisitos que tiran en direcciones opuestas:

- **SDD parte de que el spec es un documento humano.** Es el artefacto que una persona escribe y discute; convertirlo en un archivo de configuración traiciona la premisa.
- **El pipeline tiene que ser verificable.** Si el spec es prosa libre, no hay forma de comprobar que la app generada implementó lo que pedía — solo que compiló. Y compilar es lo que ya verifica el gate de LSP (ADR-004).

### Decisión
**Markdown humano, con identificadores estables como única convención mecánica.** Las reglas de negocio se numeran `RN-nn`, los criterios de aceptación `CA-nn`, y cada criterio cita la regla que verifica.

El spec resultante se lee como prosa —secciones de propósito, actores, modelo conceptual, reglas, criterios, restricciones, fuera de alcance— y a la vez expone una estructura que un regex puede validar. El spec de este proyecto está en [specs/gestor-tareas.md](specs/gestor-tareas.md).

Los identificadores son lo que hace **trazable** el pipeline: el plan de tareas cita el `RN-nn` que implementa, el log muestra qué regla se está trabajando en qué capa, y la verificación final se corre contra la lista de `CA-nn`. Sin ellos, "el pipeline funcionó" no es una afirmación comprobable.

### Alternativas
- **YAML o JSON estructurado** → descartado. Da parseo determinista y cero ambigüedad, pero degrada el Spec Analyzer a un deserializador: el nodo más visible del grafo dejaría de hacer trabajo interesante justo donde se lo mira. Y contradice la premisa de SDD.
- **Markdown libre, sin convenciones** → descartado. Es el más fiel a la filosofía y el menos verificable: no hay forma de comprobar que una regla se implementó, ni de recortar el plan contra el spec. Deja al proyecto sin criterio de éxito.
- **Dos archivos, uno humano y otro derivado y estructurado** → descartado por ahora: introduce un paso de sincronización que puede desincronizarse, para un spec de una página.

### Consecuencias
- **El spec tiene una invariante propia, comprobable:** los identificadores son únicos y correlativos, y **todo `CA-nn` cita al menos un `RN-nn` que existe**. Se puede verificar con un grep hoy y con un test en el Bloque 3. Un spec internamente inconsistente es un input roto que ensucia todo lo que viene después.
- **El Spec Analyzer sigue siendo un nodo LLM real:** su trabajo es descomponer prosa en un plan por capa, no leer campos.
- Escribir el spec del gestor de tareas expuso una ambigüedad del briefing que valía la pena resolver: *"no se puede completar una tarea si tiene una tarea **dependiente** sin completar"* invierte la regla si se lee literal, y la vuelve absurda (nada con dependientes sería completable). El spec fija la lectura correcta —lo que bloquea a una tarea son sus prerrequisitos, no sus dependientes— con un ejemplo numerado. **Es exactamente la clase de defecto que un spec SDD existe para eliminar**, y encontrarlo al escribirlo es evidencia de que el formato hace su trabajo.
- La convención es una carga sobre quien escribe specs futuros. Para un proyecto de 2.5 semanas con un solo spec es trivial; a escala haría falta una plantilla o un generador.

## ADR-011 — Scope de los subagentes de capa: tools, modelo, límites y alcance de archivos
**Fecha:** 2026-08-07
**Estado:** **Propuesta** — el conjunto se valida en el Bloque 4 del `ROADMAP.md`
**ADRs relacionados:** ADR-001 (los agentes son subagentes de Claude Code), ADR-010 (el servidor MCP que consumen), ADR-004 (por qué consultan el gate).

### Contexto
ADR-001 dejó fijado que los agentes de capa se definen como subagentes de Claude Code, no como prompts sueltos. Falta decidir el scope de cada uno: qué herramientas tiene, con qué modelo corre, qué límites lo acotan y qué archivos puede tocar.

Al relevar la documentación de Claude Code aparecieron tres hechos que cambian el diseño previsto:

1. **El frontmatter de un subagente no tiene campo de rutas.** Los campos son `name`, `description`, `tools`, `disallowedTools`, `model`, `permissionMode`, `mcpServers`, `hooks`, `maxTurns`. `tools` es un allowlist de **herramientas**, no de paths. "Cada agente solo toca su capa" no es expresable en frontmatter.
2. **`mcpServers` distingue dos formas con consecuencias muy distintas.** Una **referencia por nombre** comparte la conexión de la sesión padre; una **definición inline** se conecta al arrancar el subagente y se desconecta al terminar.
3. **Existen `model` y `maxTurns` por subagente**, aplicados por Claude Code.

### Decisión
Cuatro subagentes, versionados como plantillas en [templates/agents/](templates/agents/) y copiados a `.claude/agents/` del workspace generado al prepararlo.

| Agente | `tools` | `model` | `maxTurns` | Alcance |
|---|---|---|---|---|
| `spec-analyzer` | `Read`, `Grep`, `Glob` | `sonnet` | 15 | Produce el plan de tareas. **Sin permiso de escritura** |
| `domain` | + `Write`, `Edit`, MCP `lsp` | `sonnet` | 40 | Entidades e invariantes |
| `api` | ídem | `haiku` | 40 | Endpoints y persistencia |
| `frontend` | ídem | `haiku` | 40 | Interfaz React |

Cuatro decisiones dentro de esa:

- **`mcpServers` por referencia de nombre, nunca inline.** Con una definición inline —y sobre todo con transporte stdio— cada spawn de subagente levantaría su propio language server y pagaría el indexado desde cero, que es justo la ventana en la que el gate devuelve falsos verdes (ADR-006). La referencia comparte la conexión y los servidores indexan una vez por corrida.
- **`model` como palanca de costo, no de estilo.** La API y el frontend son trabajo mecánico sobre un dominio ya definido: `haiku` alcanza. El dominio, donde se interpreta la regla de negocio y donde un error se propaga a las tres capas, se queda en `sonnet`. Es una mitigación concreta del riesgo R1 del `ROADMAP.md` (límite de 5 h del plan Pro).
- **`maxTurns` por agente.** Techo duro aplicado por Claude Code, no por nuestro código. Cubre parte de la obligación de terminación de ADR-003 sin que tengamos que escribirla; el límite de iteraciones del grafo sigue siendo nuestro y opera por encima. Dos techos independientes, porque un agente colgado y un ciclo del grafo que no converge son fallas distintas.
- **El alcance de archivos se enforcea con un hook `PreToolUse` por subagente**, que rechaza un `Write` o `Edit` fuera de la carpeta de la capa. **Se diseña acá y se implementa en el Bloque 4.** Mientras tanto, el alcance está declarado en el prompt de cada agente: instrucción, no barrera.

### Alternativas
- **Un solo agente para las tres capas** → descartado: sin fronteras, el gate no puede atribuir un diagnostic a un responsable y el loop de revisión no sabe a quién volver. La partición por capa es lo que hace que la arista condicional del grafo tenga a dónde ir.
- **Restringir archivos con `permissions.deny` en `settings.json`** → descartado como mecanismo principal: esas reglas son **de sesión, no por agente**, así que no distinguen al agente de dominio del de API. Sirven como red adicional, no como la frontera.
- **Todo en `haiku` para minimizar costo** → descartado: el dominio es donde una interpretación equivocada del spec se paga en las tres capas. Ahorrar ahí es el peor lugar donde ahorrar.
- **Sin `maxTurns`, confiando solo en el límite del grafo** → descartado: un agente que se cuelga dentro de su propio turno no llega nunca a devolver control al grafo, así que el límite del grafo no lo alcanza.

### Consecuencias
- **Hasta que exista el hook, el alcance de archivos es una convención.** Un agente puede escribir fuera de su capa y nada lo detiene salvo el prompt. Aceptado para los Bloques 2 y 3, cerrado en el 4. Es una deuda con fecha, no un descuido.
- **El agente de API depende de poder consultar el dominio.** No lo escribió él y no debe asumir firmas: `workspaceSymbol`, `documentSymbol` y `definition` son su forma de preguntar. Es la función (2) de ADR-004 en uso concreto — y si esos tools no existieran, este agente estaría adivinando.
- **`spec-analyzer` sin permiso de escritura es deliberado.** Su salida es un plan; si pudiera escribir código, la frontera entre planificar y ejecutar se disolvería en el primer turno.
- Los cuatro prompts repiten la misma instrucción sobre `status: "indexing"`. Es duplicación consciente: es la trampa más cara del proyecto y cada agente la enfrenta solo.
- **Queda pendiente de verificación** que el conjunto funcione headless: referencia por nombre desde el frontmatter de un subagente, con el servidor pre-aprobado. Si algo falla, este ADR se actualiza con la razón.

## ADR-010 — Contrato del servidor MCP de LSP: tools, transporte HTTP y formato de `Diagnostic`
**Fecha:** 2026-08-07 · **Verificado y promovido a Aceptada:** 2026-08-09 (Bloque 2)
**Estado:** Aceptada
**ADRs relacionados:** ADR-004 (por qué LSP es la fuente de verdad), ADR-005 (por qué se expone como MCP), ADR-006 (qué language servers envuelve), ADR-013 (en qué se implementó).

### Contexto
ADR-005 decidió exponer el LSP como servidor MCP con **dos** consumidores: los agentes de capa, que quieren navegación durante su turno, y el orquestador, que quiere un veredicto para decidir la arista del grafo. Falta el contrato: qué tools, con qué firmas, qué transporte y qué forma tiene un diagnostic.

### Decisión
El contrato completo, con firmas y ejemplos, está en [docs/mcp-contract.md](docs/mcp-contract.md). Las decisiones que lo gobiernan:

**Transporte HTTP, un solo servidor, referenciado por nombre.** No stdio. Tres razones en orden de peso: (1) un solo servidor garantiza que el gate y el agente vean los mismos diagnostics — con instancias separadas el grafo decidiría sobre una realidad que el agente no comparte; (2) los language servers arrancan e indexan una vez por corrida, no una vez por spawn de subagente; (3) HTTP reconecta solo con backoff, stdio no.

**Cinco tools:** `diagnostics` (los dos consumidores), y `definition`, `references`, `documentSymbol`, `workspaceSymbol` (los agentes). Las cuatro de navegación son lo que sostiene el criterio de falsación de ADR-004.

**`status: "ready" | "indexing"` en la respuesta de `diagnostics`.** Es el campo más importante del contrato. Un language server recién arrancado devuelve lista vacía mientras indexa; sin este campo, el gate lee eso como "compila limpio" y **aprueba código roto**. `"ready"` con lista vacía significa *no hay errores*; `"indexing"` significa *todavía no sé*. Hacer imposible confundirlos es una obligación del contrato, no del consumidor.

**Truncado explícito y orden fijo.** La respuesta lleva `total`, `truncated` e `items` acotado, ordenado por severidad, después archivo, después línea. El recorte se hace por el final, así que lo que sobrevive es siempre lo que bloquea la compilación.

**`range` en 1-based**, convertido en el servidor. LSP cuenta desde cero; los compiladores, los editores y las personas desde uno.

**Sin campo `layer`.** Mapear ruta a capa es concern del orquestador. Deja el servidor agnóstico del proyecto y la decisión de a qué agente volver en `Orchestrator.Application`, donde se testea con fakes.

### Alternativas
- **Transporte stdio** → descartado por lo dicho arriba. Es el default y el más simple de arrancar; su costo es reindexado por spawn y ninguna garantía de que los dos consumidores vean lo mismo.
- **Solo `diagnostics`, sin tools de navegación** → descartado: es literalmente el escenario que ADR-004 declaró como falsación de sí mismo.
- **Un `Diagnostic` que replique el del protocolo LSP tal cual** → descartado: expondría 0-based y el ruido del protocolo a un consumidor que es un prompt. El servidor traduce.
- **Que el orquestador use su propio cliente LSP directo, sin pasar por MCP** → descartado: serían dos implementaciones del mismo wrapping, y dos implementaciones divergen. Peor: divergirían justo en el punto donde el proyecto afirma que el agente y el gate ven la misma verdad.

### Consecuencias
- **Los servidores de `.mcp.json` con scope de proyecto piden aprobación interactiva.** En `claude -p` headless no hay quién apruebe, y el fallo no es un error: el agente corre **sin las tools de LSP, en silencio**, y el pipeline degrada a generación a ciegas — exactamente lo que el proyecto existe para evitar. El orquestador tiene que agregar el servidor a `enabledMcpjsonServers` en el `settings.json` del workspace generado, y **verificar al arrancar que las tools están disponibles** en vez de asumirlo. Registrado como riesgo en `ROADMAP.md`.
- **Tres procesos que administrar** en `Orchestrator.Lsp`: el servidor MCP y los dos language servers. Apagado determinista obligatorio: un language server huérfano mantiene handles sobre `output/`, que ADR-008 exige poder borrar y regenerar de cero.
- Un fallo del servidor devuelve error de MCP, nunca una respuesta vacía. Devolver `items: []` ante un servidor caído reintroduciría el falso verde por la puerta de atrás.

### Verificación del Bloque 2 (2026-08-09)

El contrato se implementó y se consultó contra **los dos servidores reales**. Las cinco tools responden y las firmas se sostuvieron sin cambios. Lo que el bloque agregó:

- **Campo nuevo: `statusDetail`.** Opcional, presente cuando `status` es `"indexing"`, con qué se está esperando (`"Roslyn is loading the solution 'App.slnx'"`). No estaba en el diseño en papel y se agregó por una razón concreta: durante la depuración, un `indexing` eterno y mudo es indistinguible de un servidor colgado. Un estado que no se puede diagnosticar termina siendo un estado que alguien decide ignorar.
- **`status` quedó atado a una señal, no a un temporizador.** Roslyn emite `workspace/projectInitializationComplete` cuando termina de cargar la solución; `typescript-language-server` no tiene fase de carga equivalente y su garantía se hace por documento, esperando su primera publicación de diagnostics. En ningún caso hay un `sleep` estimado, que era el riesgo real de este campo.
- **Segunda vía al falso verde, encontrada y cerrada: la normalización de rutas.** Nosotros emitimos `file:///F:/proyecto/src/tarea.ts`; `typescript-language-server` contesta sobre `file:///f%3A/proyecto/src/tarea.ts`. Mismo archivo, dos escrituras. Comparadas como texto son archivos distintos, y el daño es preciso: los diagnostics publicados quedan archivados bajo una clave que nadie consulta, **y el archivo parece limpio**. Es el mismo falso verde llegando por normalización en vez de por timing. Hay test de regresión.
- **`workspaceSymbol` tiene una ventana de calentamiento propia.** Un language server puede reportar el workspace cargado mientras su índice de símbolos todavía se arma, y una consulta temprana vuelve vacía — indistinguible, en el contrato, de "ese símbolo no existe". No se corrigió en el contrato: queda registrado como deuda D7 en `ROADMAP.md`, porque el consumidor real (el agente de capa, no el gate) tolera reintentar y el gate no depende de esta tool.
- **Endpoint `/health` fuera del contrato MCP.** Devuelve el estado de indexado de cada servidor. Existe para que el orquestador pueda *verificar* que la capa LSP está viva al arrancar en vez de asumirlo (fallar rápido, `AI.md`), sin abrir una sesión MCP para preguntarlo.

## ADR-009 — Artefacto de juguete: gestor de tareas con dependencias, no un CRUD vacío
**Fecha:** 2026-08-07
**Estado:** Aceptada
**ADRs relacionados:** ADR-008 (dónde vive el artefacto), ADR-007 (cómo entra su spec).

### Contexto
El orquestador tiene que producir *algo*. La elección de ese algo no es cosmética: determina
qué prueba efectivamente el pipeline. Un CRUD sin lógica se puede generar bien aunque el
grafo, el loop de revisión y el gate de LSP no funcionen — cualquier LLM escribe un
controller con cuatro endpoints de una sola pasada. Si el artefacto es trivial, una corrida
exitosa no es evidencia de nada.

El requisito real del desafío es una solución web en .NET + React generada a partir de un
spec SDD. Dentro de ese marco, la variable libre es cuánta lógica de negocio tiene el
dominio.

### Decisión
**Gestor de tareas con dependencias**, con una única regla de negocio no trivial:

- Entidades: `Tarea` (título, estado, fecha límite) y una relación "depende de" entre tareas.
- **Invariante:** no se puede marcar una tarea como completada si tiene una tarea
  dependiente sin completar.
- CRUD básico, más un endpoint que intente violar la regla a propósito, para verificar que
  el agente la implementó de verdad y no la asumió.
- Frontend: lista con checkbox y bloqueo visual / mensaje de error al intentar violar la regla.

El criterio de tamaño: **chico para escribir el spec en una tarde y correr el pipeline
completo dentro de la semana 1, pero con estado e invariantes suficientes para saber si el
orquestador sostiene una regla de dominio real.**

### Alternativas
- **Un CRUD sin reglas (lista de contactos, catálogo de productos)** → descartado: se genera
  bien aunque el pipeline esté roto. No discrimina.
- **Algo con más dominio (reservas con solapamiento de horarios, carrito con stock y
  descuentos)** → descartado por cronograma: más invariantes significa más superficie donde
  el agente puede fallar por razones que no son culpa del orquestador, y depurar eso consume
  la semana 2 completa. El objetivo es evaluar el orquestador, no la capacidad del modelo de
  modelar un dominio complejo.
- **Dejar que el spec lo defina el cliente** → descartado: no hay tiempo de ida y vuelta, y
  el artefacto tiene que estar fijo para poder iterar el pipeline contra un blanco estable.

### Consecuencias
- La invariante es un **grafo dirigido de dependencias**, lo que la hace comprobable de forma
  binaria: o la tarea se completa o no. El endpoint de violación es el test de aceptación del
  pipeline entero, no solo de la app.
- La regla toca las tres capas a la vez (dominio la define, API la expone y la rechaza,
  frontend la refleja), así que ejercita el paso de contexto entre agentes de capa — que es
  exactamente donde el gate de LSP tiene que demostrar su valor (ADR-004).
- **Riesgo asociado:** ampliar el alcance de la app es la forma más fácil de perder el
  cronograma. Cualquier feature que se le agregue al artefacto compite directamente con
  tiempo de orquestador. Registrado como riesgo en `ROADMAP.md`.
- Queda pendiente el formato exacto del `spec.md` bajo filosofía SDD — no es una decisión
  cerrada todavía, va en `ROADMAP.md`.

## ADR-008 — Dos repos separados: el orquestador y la app que genera
**Fecha:** 2026-08-07
**Estado:** Aceptada
**ADRs relacionados:** ADR-009 (qué es la app generada), ADR-007 (el orquestador no persiste estado).

### Contexto
El desafío produce dos cosas de naturaleza distinta: el orquestador, que es el trabajo que
se evalúa, y la app .NET + React, que es su salida. Meterlas en el mismo repo mezcla trabajo
manual con trabajo generado en un mismo historial de git, y vuelve ambiguo qué escribió una
persona y qué escribió un agente — que es precisamente la pregunta que el evaluador va a
hacerse.

### Decisión
Dos repos separados:

1. **Repo del orquestador** — este. Es el proyecto evaluado.
2. **Repo de la app generada** — presentado explícitamente como *output*, con una aclaración
   en su README de que fue generada por el orquestador y no escrita a mano.

Durante el desarrollo, el orquestador escribe en `output/`, que está en `.gitignore`. Ese
directorio **se borra y se regenera de cero en cada corrida**; el repo de entrega de la app
se produce a partir de una corrida final, no se mantiene incrementalmente.

### Alternativas
- **Monorepo con la app en un subdirectorio versionado** → descartado: cada corrida del
  pipeline produciría un diff enorme de código generado sobre el historial del orquestador,
  enterrando los commits que importan. Y borra la distinción manual/generado.
- **Monorepo con la app en `.gitignore` y sin repo de entrega** → descartado: el output es
  parte de lo que hay que mostrar en la demo; que no exista como repo lo vuelve un artefacto
  efímero difícil de presentar.

### Consecuencias
- **`output/` tiene que ser desechable por construcción.** Si en algún momento hace falta
  editar a mano algo ahí para que el pipeline avance, eso es una señal de que el orquestador
  no está haciendo su trabajo, no un atajo aceptable. La regeneración de cero es también el
  test de reproducibilidad del pipeline.
- El README de la app generada es el único archivo de ese repo escrito a mano. Conviene que
  lo diga.
- En la demo, el orden de presentación se sigue de esto: primero el orquestador corriendo en
  vivo contra el spec, con los loops de corrección visibles; el resultado generado al final.
  El foco es el proceso.

## ADR-007 — El spec entra por argumento de CLI; sin UI ni persistencia
**Fecha:** 2026-08-07
**Estado:** Aceptada
**ADRs relacionados:** ADR-002 (el orquestador es una consola .NET), ADR-008 (dónde escribe la salida).

### Contexto
El input del orquestador es un spec SDD en markdown. Hay que decidir cómo entra: archivo por
línea de comandos, endpoint HTTP, UI, o registro en una base de datos de corridas.

El dato que ordena la decisión: **el spec es input de arranque, no algo que cambia en
runtime.** Una corrida toma un spec, lo descompone y ejecuta el grafo hasta terminar. No hay
un caso de uso donde el spec se edite mientras el grafo corre.

### Decisión
El spec entra como argumento de línea de comandos:

```
Orchestrator.Cli --spec specs/gestor-tareas.md --output output/
```

Sin UI, sin API HTTP, sin base de datos. El estado de la corrida vive en memoria mientras
dura el proceso; la traza queda en el log estructurado en disco.

### Alternativas
- **UI web para cargar el spec y ver el grafo avanzar** → descartado: es la parte más visible
  y menos evaluada del proyecto. Consumiría días de la semana 2 construyendo algo que no
  demuestra nada sobre orquestación, y el desafío es sobre el orquestador, no sobre su
  interfaz.
- **Base de datos de corridas (histórico, reanudación, comparación entre corridas)** →
  descartado por cronograma. Es la extensión natural si el proyecto siguiera, pero para 2.5
  semanas es infraestructura sin usuario.
- **Modo servidor con endpoint HTTP** → descartado: agrega ciclo de vida y superficie sin
  resolver ningún problema de la demo.

### Consecuencias
- **La observabilidad del grafo tiene que salir por el log, no por una UI.** Eso convierte el
  formato del log en una decisión de producto, no de infraestructura: es lo que se va a
  proyectar en la demo mientras el pipeline corre. Registrado como decisión pendiente en
  `ROADMAP.md`.
- Sin persistencia, una corrida interrumpida se reinicia desde cero. Aceptable para un
  pipeline de minutos; sería inaceptable si las corridas duraran horas. Si el tiempo de
  corrida crece más de lo previsto, esta decisión hay que revisitarla.
- Reanudar desde un nodo intermedio no es posible. Durante el desarrollo eso se compensa
  testeando el grafo con fakes (`AI.md`, regla de oro 3), que permite ejercitar cualquier
  nodo aislado sin correr el pipeline completo.

## ADR-006 — Servidor de lenguaje C#: Roslyn LSP en lugar de OmniSharp
**Fecha:** 2026-08-07 · **Verificado y promovido a Aceptada:** 2026-08-09 (Bloque 2)
**Estado:** Aceptada
**ADRs relacionados:** ADR-004 (por qué hay una capa LSP), ADR-005 (cómo se expone), ADR-013 (con qué se le habla).

### Contexto
La capa LSP necesita un servidor de lenguaje por stack. Para TypeScript/React la elección es
directa: `typescript-language-server`, que es el estándar de facto y el que usan casi todos
los editores no-VS Code.

Para C# hay dos candidatos y la elección no es obvia por inercia histórica:

- **OmniSharp** (`omnisharp-roslyn`) — durante años *el* language server de C# fuera de
  Visual Studio. Casi toda la documentación y los ejemplos de integración LSP con C# que se
  encuentran buscando apuntan acá.
- **Roslyn LSP** (`Microsoft.CodeAnalysis.LanguageServer`) — el servidor que Microsoft
  construyó para el C# Dev Kit de VS Code. El ecosistema se movió en esta dirección y
  OmniSharp quedó en modo mantenimiento.

Elegir OmniSharp por inercia significa construir la pieza central del proyecto sobre algo que
el propio fabricante dejó atrás — y tener que defenderlo en la entrevista.

### Decisión
**Roslyn LSP (`Microsoft.CodeAnalysis.LanguageServer`) para C#**, `typescript-language-server`
para TypeScript/React.

**El estado fue `Propuesta`, no `Aceptada`, a propósito.** La afirmación "OmniSharp está en
modo legado" es conocimiento general del ecosistema, no algo verificado en este proyecto, y
Roslyn LSP tiene un modo de distribución y arranque distinto (paquete NuGet, no un ejecutable
suelto) que podía complicar la integración desde un proceso .NET. La condición de promoción
era mostrar diagnostics reales llegando desde un `.cs` roto a propósito.

**Se cumplió el 2026-08-09 y no hizo falta el plan B.** El detalle está abajo.

### Alternativas
- **OmniSharp** → descartado salvo como plan B, por lo dicho arriba. Ventaja real que
  conserva: más documentación de integración disponible y binario standalone más simple de
  lanzar.
- **Prescindir del servidor de lenguaje y parsear la salida del compilador** → es ADR-004,
  ya descartado ahí por razones que no son de implementación sino de arquitectura.
- **Roslyn como librería embebida en el orquestador** (referenciar `Microsoft.CodeAnalysis`
  directo en vez de hablar LSP) → descartado: elimina la simetría con el lado TypeScript, que
  sí o sí tiene que ser LSP, y obligaría a mantener dos mecanismos distintos de obtener
  diagnostics. Además el desafío pide explícitamente LSP.

### Consecuencias
- Dos servidores de lenguaje con ciclos de vida distintos que el orquestador tiene que
  arrancar, mantener vivos y apagar. Ese manejo de procesos vive en `Orchestrator.Lsp`
  (`AI.md`, regla de oro 2).
- El arranque de un language server de C# sobre una solución no es instantáneo: hay un
  período de indexado durante el cual los diagnostics están incompletos. **Consultar
  demasiado pronto devuelve "todo bien" sobre código que no compila** — un falso verde en el
  gate es peor que no tener gate. Hay que esperar la señal de proyecto cargado antes de
  confiar en la primera respuesta. Es la trampa más probable del Bloque 2.
- Si el Bloque 2 se atrasa, el Bloque 3 no se bloquea: el grafo se construye contra
  `FakeLanguageServer` (`AI.md`, regla de oro 3). Los bloques se solapan a propósito.

### Verificación del Bloque 2 (2026-08-09)

El riesgo R3 del `ROADMAP.md` era exactamente esto: *"su forma de distribución y su arranque
desde otro proceso es lo que este ADR marcó como no verificado"*. Las dos mitades, resueltas:

**Distribución.** `Microsoft.CodeAnalysis.LanguageServer` **no está en nuget.org**. Vive en el
feed público de Visual Studio —`https://pkgs.dev.azure.com/azure-public/vside/_packaging/vs-impl/nuget/v3/index.json`—
en paquetes por RID: `Microsoft.CodeAnalysis.LanguageServer.win-x64`, versión fijada
`5.4.0-2.26179.14`. Trae un **ejecutable standalone** `net10.0` con `rollForward: Major`, así que
corre con el SDK ya instalado. Se obtiene con `PackageDownload` (no `PackageReference`: lo
lanzamos como proceso, no compilamos contra él) y **no se copia al output** — son ~140 MB; la
ruta resuelta se hornea en el `runtimeconfig` y se lee al arrancar. El feed está acotado con
`packageSourceMapping` a esa familia de paquetes y nada más.

**Arranque.** `--stdio` existe y funciona; `--logLevel` y `--extensionLogDirectory` son
obligatorios. Dos comportamientos propios de Roslyn, que ningún cliente LSP genérico modela:

1. **No descubre la solución desde `rootUri`.** Hay que mandarle `solution/open` (o
   `project/open`). Sin eso se queda esperando, callado. Acepta `.slnx`, verificado.
2. **Avisa el fin de la carga** con `workspace/projectInitializationComplete`. Esa notificación
   es lo que hace honesto el campo `status` de ADR-010: antes de que llegue, un pull de
   diagnostics vuelve vacío porque todavía no compiló nada. Sin la señal habría que estimar con
   un `sleep`, que es precisamente cómo un gate aprueba código roto.

**Resultado medido** contra `fixtures/broken-csharp`: `diagnostics` devuelve
`CS1061 · 'Tarea' does not contain a definition for 'Cerrar'` en `Api/TareasController.cs:27`, y
`definition` sobre una llamada sana aterriza en `Domain/Tarea.cs:19` —cruzando la frontera entre
dos proyectos— con la firma `bool Tarea.Completar(IReadOnlyList<Tarea> prerequisitos)`.
Reproducible con `dotnet run --project src/Orchestrator.LspServer.ManualVerification`.

**Un detalle no previsto: el idioma.** Roslyn distribuye recursos localizados y los elige según
el idioma de la máquina, así que en un Windows en español los diagnostics llegan en español. No
es cosmético: esos mensajes no se quedan en el log, se pegan en el prompt del agente que tiene
que arreglar el código, al lado de fuente e instrucciones en inglés. Se fija arrancando el
proceso con `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, que fuerza el fallback a los recursos
neutros. `DOTNET_CLI_UI_LANGUAGE` **no** alcanza: gobierna los mensajes del host, no los del
servidor.

`typescript-language-server` 5.3.0 quedó verificado en la misma corrida, instalado **local al
workspace** (`node_modules`) y no global, que es como va a estar en la app generada. Se lanza con
`node <ruta>/lib/cli.mjs --stdio`, evitando el shim `.cmd` de npm. **No soporta pull diagnostics**
—no anuncia `diagnosticProvider`—, así que ahí el contrato se sostiene esperando la publicación
por documento.

## ADR-005 — El LSP se expone a los agentes como servidor MCP, no como tool interna
**Fecha:** 2026-08-07
**Estado:** Aceptada
**ADRs relacionados:** ADR-004 (por qué LSP), ADR-001 (los agentes son procesos `claude -p`).

### Contexto
Definido que el LSP es la fuente de verdad (ADR-004), queda decidir **quién lo consulta**. Hay
dos formas, y la diferencia parece de plomería pero no lo es:

1. **El orquestador consulta el LSP** entre nodos del grafo, y si hay errores le pasa el texto
   de los diagnostics al agente en el prompt del siguiente turno.
2. **El agente consulta el LSP** durante su propio turno, vía una herramienta que tiene
   disponible.

En la opción 1 el agente recibe un veredicto al final: escribió a ciegas y después le dicen
qué rompió. En la opción 2 el agente puede preguntar mientras trabaja: *¿qué firma tiene esta
entidad?*, *¿quién referencia este método antes de que lo renombre?*

### Decisión
Servidor **MCP** liviano que envuelve los language servers y expone sus capacidades como
tools que los agentes de Claude Code consultan directamente. El orquestador **también**
consulta el gate entre nodos para decidir transiciones — las dos cosas, no una en lugar de
la otra.

### Alternativas
- **Solo la opción 1 (el orquestador consulta y pasa el texto)** → descartado: reduce el LSP
  a un verificador post-hoc. Es más simple de implementar y estrictamente peor: el agente
  sigue escribiendo a ciegas y solo se entera después. Ver la consecuencia central de
  ADR-004.
- **Solo la opción 2 (el agente consulta, el orquestador confía)** → descartado: el grafo
  necesita un veredicto propio para decidir la transición. Si la única fuente es el reporte
  del agente sobre sí mismo, volvemos a confiar en que el agente dice la verdad sobre si
  compiló — el problema que todo el proyecto intenta resolver.

### Consecuencias
- **El mismo servidor MCP tiene dos consumidores con necesidades distintas.** El agente quiere
  navegación y contexto (`definition`, `references`, `documentSymbol`); el orquestador quiere
  un veredicto agregado y estable para decidir la arista del grafo. El contrato de tools tiene
  que servir a ambos sin que uno degrade al otro. El diseño exacto de esas tools es una
  decisión pendiente en `ROADMAP.md`.
- MCP es el mecanismo nativo de Claude Code para dar herramientas a un agente, así que esta
  decisión encaja con ADR-001 sin adaptadores intermedios.
- El servidor MCP es un proceso más en el ciclo de vida que `Orchestrator.Lsp` administra,
  junto con los dos language servers que envuelve.

## ADR-004 — LSP como fuente de verdad del gate, no la salida de `dotnet build`
**Fecha:** 2026-08-07
**Estado:** Aceptada
**ADRs relacionados:** ADR-005 (cómo se expone), ADR-006 (qué servidores), ADR-003 (quién consume el veredicto).

### Contexto
Este es el ADR central del proyecto y la pregunta que la entrevista va a hacer: **"¿por qué
LSP y no simplemente correr `dotnet build` y leer la salida?"**. Claude Code puede ejecutar
el compilador por su cuenta; envolver un language server es mucho más trabajo. Si la
respuesta es floja, la arquitectura entera queda en duda.

El punto de partida es lo que el proyecto rechaza: **asumir que el código compiló porque el
agente dice que compiló.** Un agente que reporta "listo, implementado" sin verificación
externa es la falla característica de los pipelines de generación de código. Hace falta una
fuente de verdad independiente del agente. Hasta acá, `dotnet build` alcanza.

Lo que `dotnet build` no da es lo otro que hace falta: **contexto de entrada**. Cuando el
agente de API tiene que escribir un controller sobre la entidad de dominio que escribió otro
agente, sin herramientas solo tiene dos opciones — leer archivos enteros y esperar haber
entendido, o asumir la firma. Las dos producen el mismo error clásico: código que invoca un
método que no existe con esa signatura, descubierto recién al compilar.

Hay además un precedente propio, datado. El `CLAUDE.md` del repo de trading del autor
(`F:\DesarrolloTrading\QuantConnect\Lean`) termina con esta regla, escrita a mano hace meses:

> *Always use LSP tools for C# code navigation: goToDefinition before modifying any
> unfamiliar code, findReferences before any refactoring, LSP diagnostics to verify changes
> compile correctly.*

O sea que la práctica ya existe como disciplina manual impuesta al asistente. **Este proyecto
la automatiza y la vuelve obligatoria en vez de sugerida.**

### Decisión
La capa LSP es la fuente de verdad del pipeline, con **dos** funciones que van juntas:

1. **Gate de verificación** — diagnostics como veredicto independiente del agente. Sin
   errores, el grafo avanza; con errores, vuelve al agente de esa capa con los diagnostics
   como input (ADR-003).
2. **Contexto de navegación** — `definition`, `references`, `documentSymbol` disponibles para
   los agentes durante su turno (ADR-005), para que puedan *preguntar* qué firma tiene algo
   en vez de asumirla.

### Alternativas
- **Parsear la salida de `dotnet build` y `tsc --noEmit`** → descartado. Cubre (1) y no cubre
  (2) en absoluto. Además: obliga a un build completo por iteración cuando los diagnostics de
  LSP son incrementales, y obliga a parsear texto formateado para humanos que cambia entre
  versiones del SDK, en vez de consumir una estructura tipada con rango, severidad y código
  de error.
- **Confiar en el reporte del agente** → descartado. Es el problema, no una alternativa.
- **Tests en vez de compilación como gate** → descartado *para este gate*, no en general. Un
  test que no compila no corre, así que la compilación es previa por necesidad. Los tests
  serían un segundo gate, más fuerte, encima de este; queda fuera del alcance de 2.5 semanas.

### Consecuencias
- **Criterio de falsación, escrito a propósito:** *si al final del proyecto la capa LSP
  terminó exponiendo únicamente diagnostics, entonces esto es un `dotnet build` caro y la
  decisión no se sostiene.* Lo que justifica el trabajo extra es la función (2). Es un
  criterio verificable contra el código terminado, no una declaración de intenciones — y es
  la respuesta honesta a la pregunta de la entrevista.
- Los diagnostics son el formato de intercambio entre el LSP y el grafo, así que su forma
  exacta (qué campos, cómo se agrupan por capa, cómo se recortan para caber en un prompt sin
  perder lo que importa) es una decisión de diseño con consecuencias en varios componentes.
  Pendiente en `ROADMAP.md`.
- El gate solo verifica lo que el compilador puede ver. **Una regla de negocio puede estar
  ausente y el código compilar perfecto** — por eso el artefacto de juguete tiene una
  invariante y un endpoint que intenta violarla (ADR-009). El gate de LSP y la verificación
  de la regla de negocio son dos cosas distintas y ninguna reemplaza a la otra.

## ADR-003 — Máquina de estados propia, no LangGraph ni framework de grafos
**Fecha:** 2026-08-07
**Estado:** Aceptada
**ADRs relacionados:** ADR-002 (el lenguaje), ADR-004 (qué alimenta las transiciones).

### Contexto
El desafío pide una solución de orquestación "con LSP y grafos". Existen frameworks para la
parte de grafos — LangGraph es el más conocido, y en .NET está el Process Framework de
Semantic Kernel. La pregunta es si adoptar uno o escribir el grafo a mano.

El grafo que este proyecto necesita es chico y de forma conocida: nodos que son agentes o
pasos de verificación, aristas condicionales gobernadas por un predicado sobre los
diagnostics, y un ciclo de revisión que vuelve al agente de la capa que falló. No hay
paralelismo, no hay ramificación especulativa, no hay persistencia de estado entre corridas
(ADR-007).

### Decisión
Máquina de estados propia en .NET, en `Orchestrator.Application`. Sin framework de grafos.

### Alternativas
- **LangGraph** → descartado por dos razones independientes. La primera es de stack: es
  Python, y adoptarlo invertiría ADR-002 solo por la parte más simple del sistema. La
  segunda es de fondo: **el grafo es exactamente lo que se evalúa en este desafío.**
  Delegarlo a un framework esconde la parte interesante del trabajo detrás de la
  configuración de una librería, y en la entrevista la conversación pasaría de "cómo diseñaste
  las transiciones" a "cómo se usa LangGraph".
- **Semantic Kernel Process Framework** → descartado: es .NET y sería consistente con ADR-002,
  pero arrastra el modelo de SK entero (kernels, plugins, conectores de servicio) para usar
  una fracción, y su modelo de agentes asume la API directa — lo que choca con ADR-001.
- **Escribirlo a mano pero como grafo genérico configurable por JSON** → descartado por ahora:
  generalización sin segundo caso de uso. El grafo de este pipeline se puede leer como código
  y eso vale más, en un proyecto que se lee para evaluarlo, que la flexibilidad.

### Consecuencias
- El grafo queda como código C# leíble, testeable con fakes y sin dependencias externas. Es
  el artefacto que se abre primero en la entrevista.
- Hay que implementar a mano lo que un framework daría gratis: límite de iteraciones del loop
  de revisión, detección de no-progreso (el agente devuelve el mismo error dos veces
  seguidas), y política ante fallo terminal. **Nada de eso es opcional** — un loop de
  revisión sin límite de iteraciones contra `claude -p` consume la cuota del plan Pro en una
  corrida (ADR-001).
- Si el proyecto creciera a paralelismo real entre capas o a reanudación de corridas, esta
  decisión hay que revisitarla. No es el caso en 2.5 semanas.

## ADR-002 — Orquestador en .NET, no Python ni TypeScript
**Fecha:** 2026-08-07
**Estado:** Aceptada
**ADRs relacionados:** ADR-001 (cómo invoca los agentes), ADR-003 (por qué no hace falta el ecosistema de Python).

### Contexto
El desafío no exige lenguaje para el orquestador — solo para lo que el orquestador produce
(.NET + React). O sea que la elección es libre y hay que justificarla.

El sesgo del ecosistema apunta a Python: casi todo el tooling de agentes vive ahí. El
contrapeso: .NET es el lenguaje más fuerte del autor, corre bien en modo headless, y —dado
ADR-001— lo único que el orquestador necesita del ecosistema de agentes es poder lanzar un
subproceso y leer su salida, cosa que `System.Diagnostics.Process` hace sin ayuda.

### Decisión
Orquestador en **.NET**, como aplicación de consola (con la puerta abierta a Worker Service
si hiciera falta un ciclo de vida más largo, cosa que ADR-007 hace improbable).

### Alternativas
- **Python** → descartado. El ecosistema de agentes es la ventaja, y ADR-001 + ADR-003 la
  anulan: no se usa ninguna librería de agentes ni de grafos. Quedaría la desventaja de
  trabajar en un lenguaje menos dominado en un proyecto con plazo corto.
- **TypeScript / Node** → descartado. Argumento a favor: el SDK de Claude Code y el
  ecosistema MCP son de primera clase ahí, y el lado React del artefacto sería el mismo
  stack. Argumento en contra que pesa más: el gate de C# es la mitad difícil del problema, y
  hacerlo desde .NET da acceso natural al tooling de Roslyn si la integración LSP se complica
  (ADR-006).
- **Worker Service en vez de consola** → diferido, no descartado. Con el spec entrando por
  CLI y sin estado entre corridas (ADR-007), una consola alcanza. El día que haya corridas
  disparadas por evento, se reconsidera.

### Consecuencias
- Coherencia de stack: el orquestador está escrito en el mismo lenguaje que la mitad de lo
  que genera, así que las convenciones de `AI.md` aplican a los dos lados.
- El servidor MCP (ADR-005) puede escribirse en .NET también, o en otra cosa si el SDK de MCP
  lo hace más simple — es un proceso separado que habla un protocolo, no una dependencia de
  compilación. La decisión queda abierta hasta el Bloque 2.
- Menos ejemplos de referencia disponibles para integraciones de agentes que en Python. Se
  compensa con que la superficie de integración es chica: lanzar un proceso, hablar LSP,
  hablar MCP.

## ADR-001 — Claude Code CLI headless (`claude -p`), no la API de Anthropic
**Fecha:** 2026-08-07
**Estado:** Aceptada
**ADRs relacionados:** ADR-002 (el orquestador que la invoca), ADR-005 (cómo recibe las tools de LSP).

### Contexto
El motor que ejecuta cada nodo-agente del grafo puede ser la API de Anthropic invocada con
una key, o la CLI de Claude Code en modo headless.

Tres hechos ordenan la decisión:

- Claude Code ya se usa en producción a diario en el trabajo del autor, con sus límites y
  workarounds conocidos. No es una herramienta nueva a aprender bajo plazo.
- El cliente ya tuvo una conversación sobre esta herramienta específica; usarla refuerza esa
  continuidad.
- **La facturación.** Se opera bajo plan Pro de Anthropic. La CLI corre contra la
  suscripción; la API con key factura por token aparte. Un pipeline de agentes que regenera
  código en loop puede consumir mucho, y la diferencia entre las dos rutas no es de estilo
  sino de cuánto sale el proyecto.

### Decisión
**Siempre `claude -p` en modo headless, invocado como subproceso.** Nunca la API directa,
nunca `ANTHROPIC_API_KEY`. La invocación vive exclusivamente en `Orchestrator.Agents`
(`AI.md`, regla de oro 2).

### Alternativas
- **API de Anthropic con key** → descartado por facturación (corre por fuera de la
  suscripción) y porque perdería el andamiaje que Claude Code ya trae hecho: subagentes,
  skills, permisos por herramienta, integración MCP nativa. Reimplementar eso sobre la API
  sería el proyecto entero.
- **OpenCode u otro runner de agentes** → descartado: no está en uso diario, sus modos de
  fallo son desconocidos, y no aporta nada que Claude Code no dé.

### Consecuencias
- **El límite de 5 horas del plan Pro es una restricción de diseño, no un detalle
  operativo.** Un loop de revisión que reintenta contra diagnostics consume cuota rápido, y
  la cuota se agota justo cuando más se necesita: depurando. De ahí salen dos reglas duras:
  - **Ninguna suite de tests invoca la CLI real** (`AI.md`, regla de oro 3). El grafo se
    depura contra `FakeAgentRunner` con respuestas grabadas. Esta regla existe por este ADR.
  - El loop de revisión tiene límite de iteraciones y detección de no-progreso (ADR-003).
  - **Escalada:** si en la semana 1 se pega contra el techo de forma sostenida, evaluar
    créditos de uso o subir a Max. Registrado como riesgo en `ROADMAP.md`.
- El orquestador depende de un ejecutable externo (`claude`) presente en el `PATH`, con su
  versión y su comportamiento de CLI. Es una dependencia de entorno que hay que documentar en
  el README y verificar al arrancar — fallar rápido si `claude` no responde es mejor que
  fallar en el nodo tres.
- Los agentes de capa se definen como subagentes o skills de Claude Code, no como prompts
  sueltos. El scope exacto de cada uno (prompts, permisos, qué archivos puede tocar) es una
  decisión pendiente en `ROADMAP.md`.
