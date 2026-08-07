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
**Fecha:** 2026-08-07
**Estado:** **Propuesta** — pendiente de verificación empírica en el Bloque 2 del `ROADMAP.md`
**ADRs relacionados:** ADR-004 (por qué hay una capa LSP), ADR-005 (cómo se expone).

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

**El estado es `Propuesta`, no `Aceptada`, a propósito.** La afirmación "OmniSharp está en
modo legado" es conocimiento general del ecosistema, no algo verificado en este proyecto, y
Roslyn LSP tiene un modo de distribución y arranque distinto (paquete NuGet, no un ejecutable
suelto) que puede complicar la integración desde un proceso .NET. La decisión se promueve a
`Aceptada` recién cuando el Bloque 2 muestre diagnostics reales llegando desde un archivo
`.cs` roto a propósito. Si la integración resulta impracticable en el plazo, se revierte a
OmniSharp y este ADR se actualiza con la razón — no se reescribe la historia.

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
