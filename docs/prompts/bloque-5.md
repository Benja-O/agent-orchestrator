# Prompt de arranque — Bloque 5

> **Convención.** Un prompt por bloque, escrito al cerrar el bloque anterior. Es corto a propósito: el repo se autodescribe —`CLAUDE.md` se carga solo y manda leer `ROADMAP.md`, `DECISIONS.md` y `AI.md`— así que el prompt no reexplica la arquitectura. Lleva solo lo que los documentos no dicen: qué decisiones abiertas cierra el bloque, en qué orden atacar, y qué ya está verificado.

Copiar y pegar en un chat nuevo abierto en la raíz del repo:

```
Arrancamos el Bloque 5. Leé ROADMAP.md primero para el estado y el criterio de salida.

## Lo que este bloque tiene que decidir

El Bloque 4 dejó una sola cosa sin decidir, y es la que bloquea todo lo demás:
**cuál es el layout del proyecto generado**. No se adelantó a propósito —decidirlo
de paso, para que un arnés corriera, habría sido decidirlo de casualidad.

Son las deudas D12 y D13, y las dos son la misma pregunta vista de dos lados:

1. D12 — Roslyn carga una solución, no una carpeta de archivos sueltos. Sin
   `.slnx` y `.csproj`, el gate no analiza nada. Y "no analiza nada" se ve
   exactamente igual que "está limpio": es el falso verde otra vez, llegando por
   una tercera puerta que ni ADR-006 ni el Bloque 2 habían previsto.

2. D13 — `typescript-language-server` tiene que estar en el `node_modules` del
   workspace analizado, que en una app recién generada no existe. Ojo con la
   interacción, que es la parte fácil de pasar por alto: **el contrato contesta
   `indexing` para todo el scope mientras algún servidor indexa**, así que un
   servidor que nunca puede quedar listo impide que el gate dé veredicto sobre el
   C# que sí lo está.

Hoy el esqueleto de C# lo escribe `Orchestrator.PipelineVerification` y el
servidor de TypeScript está apagado ahí. Las dos cosas son andamios del arnés, no
del producto: al terminar este bloque tienen que vivir en el orquestador o estar
justificadas por escrito.

**La pregunta de diseño real:** ¿el esqueleto lo escribe el orquestador al
preparar el workspace, o es la primera tarea del plan y la escribe un agente? La
primera opción es determinista y saca superficie de fallo; la segunda deja que el
pipeline demuestre más. Es un ADR, no una decisión de implementación.

## Lo que hay que construir

Orchestrator.Cli — el único `Main` del lado del orquestador. Parseo de
argumentos, wiring, código de salida:

    dotnet run --project src/Orchestrator.Cli -- --spec specs/gestor-tareas.md --output output/

El wiring ya existe entero y funciona: miralo en
src/Orchestrator.PipelineVerification/Program.cs, que arma workspace, servidor
MCP, chequeos de arranque, observadores y grafo en ese orden. Buena parte de este
bloque es mover eso a un host de verdad, no inventarlo.

## Criterio de salida

`output/` se genera de cero, la app compila, y el endpoint que intenta violar la
invariante de ADR-009 la rechaza.

Notar que son tres cosas y la tercera es la que importa: R4 del ROADMAP dice que
el gate verifica compilación, no corrección. Una corrida que compila y no sostiene
RN-01 cumple dos tercios del criterio y falla en lo único que el proyecto quería
demostrar.

## Regla de costo

Sigue vigente todo lo del Bloque 4, y ahora con datos:

- Una corrida completa del pipeline sobre el spec real son ~7 turnos de agente
  como piso. El spec mínimo de verificación gastó bastante menos y alcanzó para
  ver el loop: si estás depurando el wiring, usá ese.
- GraphPolicy.MaximumAttemptsPerNode a 2 mientras depurás. El default es 3.
- El agente de dominio corre en sonnet y tarda ~2 min por turno. Los de api y
  frontend corren en haiku. Eso ya está afinado en ADR-011, no lo toques sin
  razón.
- Todo lo que se pueda depurar contra los fakes, se depura contra los fakes. Son
  218 tests y corren en segundos.

## Lo que ya está verificado y no hay que redescubrir

- Los dos adaptadores funcionan y tienen tests propios que no invocan nada real.
- R5 está cerrado y era tres riesgos, no uno. Si un agente parece no ver el
  servidor MCP, la respuesta está en ADR-011 y en la tabla de R5 del ROADMAP —
  no vuelvas a diagnosticarlo desde cero. El diagnóstico determinista es el
  mensaje `init` de `--output-format stream-json`, que lista servidores y tools
  antes de cualquier inferencia.
- El hook de alcance de archivos bloquea de verdad, y el orquestador lo comprueba
  al arrancar.
- El formato del plan del spec analyzer sobrevivió su primer contacto con un
  agente real: parseó al primer intento.
- La sincronización de documentos con los language servers está arreglada y
  tiene su paso 5 en el arnés del Bloque 2. Si el gate vuelve a reportar un
  error que el agente ya arregló, mirá `DocumentSynchronizer` antes que nada
  — y acordate de que **Roslyn no falla, se calla**: ante cualquier cuelgue
  suyo, `--LspServer:TraceProtocol=true` es lo primero, no lo último.
- El grafo no necesitó cambiar para que los adaptadores encajaran. Si en este
  bloque aparece la tentación de tocar Orchestrator.Application, sospechá del
  wiring antes que del grafo.

## Al cerrar el bloque

- ADR nuevo con la decisión de layout del proyecto generado (D12/D13).
- ROADMAP.md: bloque en ✅ con fecha y su entrada en el historial.
- docs/prompts/bloque-6.md.

## Entorno ya verificado, no lo redescubras

.NET SDK 10.0.300 · Node v24.18.0 / npm 11.16.0 · Claude Code 2.1.224.
`pwsh` NO está instalado en esta máquina: usá `node` o `powershell.exe`.

dotnet build src/Orchestrator.slnx
dotnet test  src/Orchestrator.slnx     # 218 tests
dotnet run --project src/Orchestrator.PipelineVerification   # gasta cuota
```
