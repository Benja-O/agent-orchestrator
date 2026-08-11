# Prompt de arranque — Bloque 6

> **Convención.** Un prompt por bloque, escrito al cerrar el bloque anterior. Es corto a propósito: el repo se autodescribe —`CLAUDE.md` se carga solo y manda leer `ROADMAP.md`, `DECISIONS.md` y `AI.md`— así que el prompt no reexplica la arquitectura. Lleva solo lo que los documentos no dicen: qué decisiones abiertas cierra el bloque, en qué orden atacar, y qué ya está verificado.

Copiar y pegar en un chat nuevo abierto en la raíz del repo:

```
Arrancamos el Bloque 6, el último. Leé ROADMAP.md primero para el estado y el
criterio de salida.

## Qué es distinto en este bloque

Los cinco anteriores construían. Este entrega, y el briefing dice que **el foco
es el proceso, no el resultado**. O sea: el entregable principal de este bloque
es que alguien que abre el repo por primera vez entienda **por qué** cada
decisión es como es, en veinte minutos y sin que se lo cuenten.

Casi todo el material ya está escrito —DECISIONS.md tiene 17 ADRs, ROADMAP.md
tiene el historial de cada bloque con sus hallazgos—. El trabajo es de
selección y de puesta en escena, no de redacción desde cero. **Resistí la
tentación de agregar features.** R2 del ROADMAP es exactamente eso y este es el
bloque donde más fácil se cae.

## Lo que hay que construir

**README.md**, que hoy es un stub. Tiene que explicar tres cosas —la
arquitectura del grafo, la integración LSP, y la integración con Claude Code— y
tiene una cuarta que sale gratis y vale más que las tres: **el proyecto tiene
una colección poco común de modos de fallo silencioso documentados con
evidencia**. El falso verde por indexado, el falso verde por normalización de
rutas, el falso rojo del documento no sincronizado, el archivo que nace después
de cargar la solución, los tres mecanismos de Claude Code que fallan abiertos, y
el más caro de todos: una app con cuatro verificaciones en verde que devuelve
500 en la primera request. Esa lista es el proyecto.

**La demo.** El criterio de salida pide que corra de principio a fin sin
intervención. Hoy son tres comandos y conviene ensayarlos en ese orden:

    dotnet run --project src/Orchestrator.Cli -- --spec specs/gestor-tareas.md --output output/
    dotnet build output/App.slnx
    dotnet run --project src/Orchestrator.GeneratedAppVerification

La corrida completa son ~18 minutos y gasta cuota. **Ensayá con el log de una
corrida vieja antes de gastar una nueva**: están todos en logs/, en JSONL, y
ADR-015 los diseñó para proyectarlos.

## Lo que este bloque tiene que decidir

Solo una cosa, y es de entrega, no técnica: **si el repo se publica**. `CLAUDE.md`
dice que hoy es git local sin remoto y que se define acá. ADR-008 ya fijó que
son dos repos —el orquestador y la app generada—, así que lo que falta es el
dónde, no el cómo.

Ojo con una consecuencia práctica: `output/` está gitignoreado. Si la entrega
incluye mostrar la app generada, hay que decidir cómo viaja, y "corré el
pipeline y vas a ver" no es una respuesta para quien evalúa sin cuota de Pro.

## Regla de costo

Sigue todo lo anterior, y con datos frescos:

- Una corrida completa sobre el spec real: ~18 min, 4 turnos de agente si todo
  sale a la primera (spec-analyzer, dominio, api, frontend).
- El gate de runtime **no gasta cuota** pero suma ~1 min por corrida, porque
  `dotnet run` compila antes de servir.
- `GeneratedAppVerification` no gasta cuota y tarda segundos. Usalo todo lo que
  quieras.
- Todo lo que se pueda verificar contra los fakes, contra los fakes: 277 tests
  en segundos.

## Lo que ya está verificado y no hay que redescubrir

- **Las tres partes del criterio del Bloque 5 están cumplidas y son
  reproducibles.** No hace falta volver a correr el pipeline para probar que
  funciona; hace falta para ensayar la demo.
- Los defectos de entorno están todos documentados en ROADMAP y AI.md. Si algo
  de `npm`, `pwsh`, la política de ejecución de PowerShell o el `node_modules`
  del frontend se comporta raro, la respuesta ya está escrita — no lo
  diagnostiques de cero.
- El grafo no cambió una línea para que entraran los tres adaptadores. Si
  aparece la tentación de tocar `Orchestrator.Application`, sospechá del wiring.
- **Roslyn no falla, se calla.** Cuarta vez. Ante cualquier cuelgue suyo,
  `--LspServer:TraceProtocol=true` es lo primero, no lo último.

## Al cerrar el bloque

- README.md real.
- ROADMAP.md: Bloque 6 en ✅ con fecha, su entrada en el historial, y el
  proyecto cerrado.
- Si se publica, el ADR de entrega.
- No hace falta un prompt de arranque para el Bloque 7 — no hay Bloque 7.

## Entorno ya verificado, no lo redescubras

.NET SDK 10.0.300 · Node v24.18.0 / npm 11.16.0 · Claude Code 2.1.224.
`pwsh` NO está instalado, y la política de ejecución rechaza los .ps1 sin
`-ExecutionPolicy Bypass`.

dotnet build src/Orchestrator.slnx
dotnet test  src/Orchestrator.slnx     # 277 tests
```
