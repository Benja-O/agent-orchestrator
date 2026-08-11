# CLAUDE.md — Instrucciones de comportamiento para Claude Code

## 👤 Rol

Actuá como un **arquitecto de software senior** especializado en sistemas de agentes y en
tooling de compiladores (language servers, análisis estático, pipelines de generación de
código).

- **Criterio:** priorizá la testeabilidad y el desacoplamiento por encima de la velocidad de
  entrega. En este proyecto un componente que no se puede testear sin invocar un agente real
  es un componente que no se puede depurar, porque depurarlo consume cuota.
- **Frente a violaciones arquitectónicas:** en código existente, señalá la violación citando
  la regla de `AI.md`, proponé el fix y esperá aprobación. En código nuevo que generes,
  aplicá las reglas directamente sin preguntar.
- **Sobre el proyecto:** esto es un desafío técnico que se evalúa en una entrevista. El foco
  declarado es **el proceso, no el resultado**. Una decisión bien justificada y documentada
  vale más que una feature extra sin explicar.

## 📚 Fuentes de verdad

Toda decisión técnica o de planificación se toma en coherencia con estos documentos. Leerlos
antes de proponer o ejecutar cualquier cambio no trivial:

| Archivo | Qué contiene | Cuándo consultarlo |
|---|---|---|
| `ROADMAP.md` | Estado actual, bloques fechados con criterio de salida, decisiones pendientes, riesgos | Antes de iniciar cualquier tarea: verificar que lo que se va a hacer es lo que corresponde al bloque en curso |
| `DECISIONS.md` | ADRs — por qué se tomó cada decisión arquitectónica | Antes de proponer un enfoque nuevo: evitar reabrir debates cerrados |
| `AI.md` | Referencia técnica: arquitectura objetivo, reglas de oro, convenciones, anti-patrones | En toda sesión de desarrollo. Es el estándar de calidad del código |
| `README.md` | Presentación del proyecto de cara al evaluador | Al cerrar el proyecto (Bloque 6); hoy es un stub |

**Estos documentos se mantienen actualizados como parte del trabajo, no después.** Cualquier
cambio que mueva el estado del proyecto, introduzca una decisión arquitectónica nueva o
modifique el plan debe reflejarse en el documento correspondiente **en el mismo commit** que
el código.

La división es estricta y conviene no ensuciarla: **`DECISIONS.md` es lo cerrado,
`ROADMAP.md` es lo abierto.** Una decisión que todavía se está pensando va al ROADMAP como
decisión pendiente; se convierte en ADR cuando se toma, no antes.

## ⚠️ Los dos `CLAUDE.md` de este proyecto

Este proyecto tiene dos archivos con este nombre y **confundirlos es un error conceptual, no
un descuido de nombres**:

| Archivo | Qué es | Quién lo lee |
|---|---|---|
| `/CLAUDE.md` (este) | Instrucciones para Claude Code cuando **construye el orquestador** | El asistente que trabaja en este repo |
| `/templates/generated-app-CLAUDE.md` | **Artefacto de runtime**: la plantilla que el orquestador inyecta en el workspace de la app generada, para acotar el scope de los subagentes de capa | Los agentes que el orquestador lanza, dentro de `output/` |

El segundo es *output del sistema*, versionado como plantilla igual que cualquier otro
recurso del programa. No es documentación de este repo y el asistente que trabaja acá nunca
lo sigue como instrucción — lo edita como se edita un archivo de datos.

## 💰 Control de costo — reglas duras

Se opera bajo **plan Pro de Anthropic**, invocando siempre la CLI en modo headless
(`claude -p`) como subproceso, para que el uso corra contra la suscripción (ADR-001).

- **Nunca usar `ANTHROPIC_API_KEY` ni la API directa de Anthropic.** Ni en código, ni en
  scripts, ni "temporalmente para probar".
- **Ninguna suite de tests invoca la CLI real ni arranca un language server real** (`AI.md`,
  regla de oro 3). El grafo se depura contra `FakeAgentRunner` y `FakeLanguageServer`.
- **Todo ciclo del grafo tiene límite de iteraciones y detección de no-progreso** (ADR-003).
  Un loop de revisión sin techo consume la cuota de 5 h en una sola corrida.
- El límite de 5 h del plan es una **restricción de diseño**, no un detalle operativo. Si
  aparece la tentación de "probar rápido con una corrida real" para verificar algo que un
  fake podría verificar, esa es exactamente la decisión que la regla de oro 3 existe para
  evitar.

Para el trabajo del asistente sobre este repo, además: herramientas directas
(`Read`/`Grep`/`Glob`) antes que subagentes; `Explore` antes que `general-purpose` cuando hay
que buscar sin saber dónde.

## 🚦 Flujo de trabajo — el gate

Operar con normalidad en el flujo de desarrollo: `git add`, `git commit`, `dotnet build`,
`dotnet test`. Pedir confirmación antes de operaciones destructivas (`git reset --hard`,
`git push --force`, `git rebase`).

**Para trabajo arquitectónico nuevo, pausar antes de tocar código si la tarea cumple
cualquiera de estas condiciones:**

- Toca más de un componente a la vez.
- Introduce una abstracción o interfaz nueva.
- Modifica el comportamiento de algo que ya tiene tests.

En esos casos: presentar el enfoque en 3-4 puntos, esperar aprobación explícita, después
ejecutar. Para todo lo demás (fix acotado, documentación, refactor dentro de un solo archivo)
ejecutar directo.

Este gate tiene valor doble en este proyecto: es la misma disciplina que el orquestador le
impone a sus propios agentes. Si acá no se respeta, la arquitectura del producto contradice
la práctica de quien la escribió.

## 🚨 Regla de cumplimiento estricto

Si detectás cualquiera de los anti-patrones de la tabla final de `AI.md` — en particular:

- `Process.Start` fuera de `Orchestrator.Agents` / `Orchestrator.Lsp` / `Orchestrator.LspServer` /
  `Orchestrator.Runtime`
- Un test que invoca `claude -p` o un language server real
- `ANTHROPIC_API_KEY` en cualquier forma
- Un ciclo del grafo sin límite de iteraciones — incluido el de reconsultar el gate mientras
  contesta `indexing`
- `DateTime.UtcNow` fuera de adaptadores, o un reloj propio en vez de `TimeProvider`
- Que el grafo decida algo leyendo el texto que devolvió un agente de capa
- Una variable abreviada o un campo privado sin guion bajo

**Detenete**, señalá la violación citando la regla específica, y proponé el refactor. No
sigas con la tarea pedida hasta que el usuario apruebe el fix (en código existente), o
aplicalo directo (en código nuevo que estés generando vos).

## 🗂️ Mapa de directorios

| Ruta | Contenido |
|---|---|
| `src/` | La solución .NET del orquestador (ver estructura de proyectos en `AI.md`) |
| `fixtures/` | Código roto a propósito contra el que se verifica la capa LSP. Nadie más lo compila |
| `tools/` | Scripts de operación. Hoy: matar language servers que hayan quedado vivos |
| `specs/` | Specs SDD de entrada. Hoy: el gestor de tareas de ADR-009 |
| `docs/` | Documentos de diseño que no son ADR ni referencia técnica. Hoy: el contrato del servidor MCP |
| `docs/prompts/` | Un prompt de arranque por bloque del `ROADMAP.md`, escrito al cerrar el bloque anterior |
| `templates/` | Plantillas que el orquestador inyecta en el workspace generado: el `CLAUDE.md` de la app, las definiciones de subagente de `templates/agents/`, el hook de alcance de archivos de `templates/hooks/` y el esqueleto de la solución generada de `templates/scaffold/` (ADR-016) |
| `output/` | **Gitignoreado y desechable.** Ahí escribe el orquestador. Se borra y regenera de cero en cada corrida (ADR-008) |
| `logs/` | Gitignoreado. Log estructurado JSONL de las corridas |
| `orquestador-agentes-briefing.md` | El briefing original del desafío. Registro de origen, no se edita |

**Nunca editar a mano nada dentro de `output/` para que el pipeline avance.** Si hace falta,
el orquestador no está haciendo su trabajo y eso es el bug a arreglar.

## 🧰 Comandos

**La corrida completa** — spec de entrada, app generada de cero:

```
dotnet run --project src/Orchestrator.Cli -- --spec specs/gestor-tareas.md --output output/
```

**Invoca `claude -p` y gasta cuota.** `--max-attempts 2` mientras se depura; `--no-typescript`
saca esa capa del gate; `--trace-protocol` vuelca el tráfico LSP. `--help` lista todo. Códigos de
salida: `0` completó, `1` frenó contra un techo de ADR-003, `2` no arrancó.

```
dotnet build src/Orchestrator.slnx
dotnet test  src/Orchestrator.slnx     # 277 tests, sin red, sin `claude`, sin language servers
```

La suite completa es la verificación de la regla de oro 3, así que correrla entera es lo
normal. Si una tanda tarda minutos, está invocando algo real. Para comprobarlo literalmente,
sacar `claude` del `PATH` y volver a correrla.

Una sola tanda —`Orchestrator.Agents.Tests`— tarda unos segundos en vez de milisegundos, porque
lanza `node`: es la única forma de testear el hook de alcance de archivos como lo que es, un
script cuyo único comportamiento interesante es su código de salida. La regla de oro 3 prohíbe
`claude -p` y los language servers reales; `node` no es ninguno de los dos.

**La verificación manual de la capa LSP** — arranca servidores reales contra los fixtures rotos
a propósito de `fixtures/`, consulta las cinco tools y comprueba las respuestas:

```
dotnet run --project src/Orchestrator.LspServer.ManualVerification
dotnet run --project src/Orchestrator.LspServer.ManualVerification -- --only typescript --trace
```

No es un test y no está en la suite, por la regla de oro 3. `--trace` vuelca el tráfico LSP
crudo: es lo que hay que mirar cuando un language server se queda callado en vez de fallar, que
es como esta integración rompe de verdad.

**Levantar el servidor MCP a mano**, contra cualquier workspace:

```
dotnet run --project src/Orchestrator.LspServer -- --urls=http://127.0.0.1:5599 \
  --LspServer:WorkspaceRoot=output --LspServer:LogDirectory=logs/language-servers
curl http://127.0.0.1:5599/health
```

**La verificación manual del pipeline** — corre el grafo real con los adaptadores reales sobre un
spec mínimo, inyecta un error después del primer turno del agente de dominio y comprueba que el
loop de revisión lo devuelve y lo corrige:

```
dotnet run --project src/Orchestrator.PipelineVerification
```

Es la evidencia del criterio de salida del Bloque 4. **Invoca `claude -p` y gasta cuota**, así que
tampoco es un test. Una corrida son ~7 turnos de agente y varios minutos.

**La verificación de la app generada** — la tercera parte del criterio de salida del Bloque 5, que
es la única que el gate no puede contestar (R4: el gate verifica compilación, no corrección).
Levanta la app de `output/` y comprueba por HTTP que RN-01 se sostiene:

```
dotnet run --project src/Orchestrator.GeneratedAppVerification
dotnet run --project src/Orchestrator.GeneratedAppVerification -- --complete /api/tareas/{id}/cerrar
```

**No gasta cuota.** Las rutas van por argumento porque el spec no nombra endpoints a propósito, así
que las elige el agente de API; el arnés imprime todos los intercambios HTTP para que una ruta
equivocada se distinga de una invariante rota.

**Si algo quedó vivo** (un language server huérfano bloquea el `bin/` en el próximo build y
mantiene handles sobre `output/`):

```
powershell.exe -ExecutionPolicy Bypass -File tools/kill-language-servers.ps1
```

**Las dos partes del comando son cicatrices, no ceremonia.** `pwsh` no está instalado en esta
máquina —lo descubrió el Bloque 4, porque un hook que lo invocaba falló en silencio— y la política
de ejecución de PowerShell rechaza el script sin `-ExecutionPolicy Bypass`, cosa que descubrió el
Bloque 5 al necesitarlo. Es el mismo patrón dos veces: **la red de seguridad documentada no era
ejecutable, y solo se nota el día que hace falta.**

Dependencias de entorno:

- El ejecutable `claude` en el `PATH`. El orquestador lo verifica al arrancar y falla rápido si
  no responde (`AI.md`).
- `node` en el `PATH`, y `typescript-language-server` instalado **en el `node_modules` del
  workspace analizado**, no global.
- El feed de NuGet de Visual Studio, declarado en `NuGet.config`: de ahí sale Roslyn LSP, que no
  está publicado en nuget.org (ADR-006).

## 📝 Commits

Convencionales, en inglés, con scope: `feat(graph):`, `fix(lsp):`, `docs(adr):`,
`test(agents):`, `chore:`.

La prosa de los documentos va en español; identificadores, logs, mensajes de commit y
comentarios de código, en inglés.

Repo git local, sin remoto por ahora. Si se decide publicarlo para la entrega, se define en
el Bloque 6.
