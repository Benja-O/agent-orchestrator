# Prompt de arranque — Bloque 4

> **Convención.** Un prompt por bloque, escrito al cerrar el bloque anterior. Es corto a propósito: el repo se autodescribe —`CLAUDE.md` se carga solo y manda leer `ROADMAP.md`, `DECISIONS.md` y `AI.md`— así que el prompt no reexplica la arquitectura. Lleva solo lo que los documentos no dicen: qué decisiones abiertas cierra el bloque, en qué orden atacar, y qué ya está verificado.

Copiar y pegar en un chat nuevo abierto en la raíz del repo:

```
Arrancamos el Bloque 4. Leé ROADMAP.md primero para el estado y el criterio de salida.

## Este bloque no cierra decisiones: cobra riesgos

Después del Bloque 3 no queda ninguna decisión abierta del briefing. Lo que
queda abierto es de otra naturaleza: dos afirmaciones sin verificar y la
primera cuota real del plan Pro.

1. R5 — el agente headless corre sin las tools de LSP, en silencio. Un
   .mcp.json con scope de proyecto pide aprobación interactiva, y en `claude -p`
   no hay quién apruebe. El fallo no levanta error: el agente trabaja sin el
   servidor MCP y el pipeline degrada a generación a ciegas, que es
   exactamente lo que el proyecto existe para evitar.

   **Es lo primero del bloque, no lo último.** Es el riesgo abierto más caro de
   diagnosticar que queda, y todo lo demás del bloque asume que está resuelto.
   La verificación es directa: un `claude -p` de un turno que solo llame a una
   tool del servidor `lsp` y devuelva lo que vio. Si no la ve, no sigas
   construyendo encima.

2. ADR-011 pasa a Aceptada o se corrige con la razón. Es el único ADR que
   sigue en Propuesta.

## Lo que hay que construir

Los dos adaptadores que faltan, contra interfaces que ya existen y ya están
ejercitadas por 124 tests:

- Orchestrator.Agents → IAgentRunner sobre `claude -p`. Además de invocar:
  preparar el workspace (copiar templates/agents/ a .claude/agents/, el
  CLAUDE.md generado, el .mcp.json, y el servidor en enabledMcpjsonServers).
- Orchestrator.Lsp → ILanguageServerGateway sobre el servidor MCP del Bloque 2.
  Lanza el proceso, consulta la tool `diagnostics`, y traduce DiagnosticItem del
  contrato a Diagnostic del dominio.

El grafo no cambia. Si aparece la tentación de tocar Orchestrator.Application
para que un adaptador encaje, es señal de que el adaptador está mal: la
frontera ya está probada del lado de adentro.

## Criterio de salida

Un error inyectado a propósito hace volver el grafo al agente de esa capa, y la
siguiente iteración lo corrige. Visible en el log.

## Regla de costo — este es el bloque donde se paga

R1 se materializa acá. Antes de la primera corrida completa:

- Bajá GraphPolicy.MaximumAttemptsPerNode a 2 mientras depurás. El default es 3
  y eso son tres turnos de agente por capa que se pagan.
- Depurá contra FakeAgentRunner todo lo que se pueda depurar contra
  FakeAgentRunner. La suite existe para eso y corre en un segundo.
- Los adaptadores tienen tests propios que NO invocan nada real: un
  ClaudeCodeAgentRunner se testea contra un ejecutable de mentira que escribe
  en stdout, no contra `claude`.

## Lo que ya está verificado y no hay que redescubrir

- El contrato MCP funciona contra los dos servidores reales
  (docs/mcp-contract.md, y `dotnet run --project
  src/Orchestrator.LspServer.ManualVerification`).
- El grafo consume ese contrato en la forma exacta que tiene, incluido que
  `indexing` no es aprobación y que las reconsultas tienen techo.
- El layout de la app generada está fijo y es de LayerMap: src/Domain/,
  src/Api/, src/Frontend/. Está en las tres plantillas de agente y en
  templates/generated-app-CLAUDE.md.
- El formato de salida del Spec Analyzer está en
  templates/agents/spec-analyzer.md y el parser lo espera. Si la respuesta real
  del agente no lo respeta, es un hallazgo: reemplazá el fixture escrito a mano
  por la respuesta real y ajustá lo que corresponda (ADR-014).

## Deuda con fecha que vence en este bloque

D5 — el alcance de archivos por agente es una convención del prompt, no una
barrera. El hook PreToolUse de ADR-011 se implementa acá.

## Al cerrar el bloque

- ADR-011 a Aceptada, o corregido.
- R5 cerrado en ROADMAP.md, con la evidencia.
- ROADMAP.md: bloque en ✅ con fecha y su entrada en el historial.
- docs/prompts/bloque-5.md.

## Entorno ya verificado, no lo redescubras

.NET SDK 10.0.300 · Node v24.18.0 / npm 11.16.0 · Claude Code 2.1.224.

dotnet build src/Orchestrator.slnx
dotnet test  src/Orchestrator.slnx     # 124 tests, ninguna tanda pasa de un segundo
```
