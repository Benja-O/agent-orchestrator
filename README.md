# Orquestador de agentes con LSP y grafos

Orquestador que toma un spec bajo filosofía **SDD** (Spec-Driven Development) y coordina un
grafo de agentes de Claude Code para producir una aplicación web en .NET + React.

Lo que lo distingue de un pipeline de generación de código convencional: **no asume que el
código compiló porque el agente dice que compiló.** Una capa de Language Server Protocol
actúa como fuente de verdad independiente — si devuelve errores, el grafo vuelve al agente de
esa capa con los diagnostics como input; si compila limpio, avanza.

> ⚠️ **Estado: en construcción (2026-08-07).** Este README es un stub. El contenido real se
> escribe en el Bloque 6 del [ROADMAP.md](ROADMAP.md), cuando exista arquitectura terminada
> que describir. Lo de arriba es la tesis del proyecto, no una descripción de algo que ya
> corre.

## Documentación

| Documento | Contenido |
|---|---|
| [DECISIONS.md](DECISIONS.md) | ADRs — por qué se tomó cada decisión arquitectónica |
| [ROADMAP.md](ROADMAP.md) | Estado actual, bloques con criterio de salida, decisiones pendientes, riesgos |
| [AI.md](AI.md) | Referencia técnica: arquitectura, reglas de oro, convenciones |
| [CLAUDE.md](CLAUDE.md) | Instrucciones de comportamiento para Claude Code en este repo |
| [orquestador-agentes-briefing.md](orquestador-agentes-briefing.md) | El briefing original del desafío |

## Secciones pendientes (Bloque 6)

- **Arquitectura del grafo** — nodos, aristas condicionales, el ciclo de revisión y sus tres
  vías de terminación.
- **Integración LSP** — el servidor MCP que envuelve Roslyn LSP y `typescript-language-server`,
  y por qué el gate es LSP y no `dotnet build` (ver [ADR-004](DECISIONS.md)).
- **Integración con Claude Code** — invocación headless, scope de los subagentes de capa.
- **Cómo correrlo** — requisitos de entorno, comandos, qué esperar de una corrida.
- **El output** — enlace al repo de la app generada, presentada explícitamente como salida
  del orquestador y no como trabajo manual (ver [ADR-008](DECISIONS.md)).
