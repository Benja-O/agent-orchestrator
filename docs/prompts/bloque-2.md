# Prompt de arranque — Bloque 2

> **Convención.** Un prompt por bloque, escrito al cerrar el bloque anterior. Es corto a propósito: el repo se autodescribe —`CLAUDE.md` se carga solo y manda leer `ROADMAP.md`, `DECISIONS.md` y `AI.md`— así que el prompt no reexplica la arquitectura. Lleva solo lo que los documentos no dicen: qué decisiones abiertas cierra el bloque, en qué orden atacar, y qué ya está verificado.

Copiar y pegar en un chat nuevo abierto en la raíz del repo:

```
Arrancamos el Bloque 2. Leé ROADMAP.md primero para el estado y el criterio de salida.

## Paso cero, antes de tocar nada

Actualizá Claude Code. El entorno está en 2.1.138 y ADR-010 y ADR-011 se diseñaron
contra documentación que describe comportamientos de subagentes y .mcp.json
introducidos en versiones posteriores. Alinear ahora es barato; descubrir una
incompatibilidad de frontmatter en el Bloque 4 no lo es.

## Las dos decisiones abiertas que este bloque tiene que cerrar

1. En qué se escribe el servidor MCP. ADR-002 la dejó explícitamente abierta
   "hasta el Bloque 2": .NET, o lo que el SDK de MCP haga más simple. Es un
   proceso separado que habla un protocolo, no una dependencia de compilación,
   así que no está obligado a ser .NET.

2. Cómo se obtiene y se arranca Microsoft.CodeAnalysis.LanguageServer. No está
   resuelto en ningún documento del repo. Es el riesgo R3 en concreto: su forma
   de distribución y su arranque desde otro proceso es justamente lo que ADR-006
   marcó como no verificado.

Ambas producen ADR al cerrarse. La primera puede ir como ampliación de ADR-010
si resulta menor; la segunda se registra promoviendo ADR-006.

## Criterio de salida

Una consulta manual al servidor devuelve diagnostics reales de un .cs roto a
propósito, Y una consulta de definition devuelve la ubicación correcta.

Las dos partes importan: si al final solo funcionan los diagnostics, se cumplió
la mitad que un dotnet build también daría — que es el criterio de falsación
que ADR-004 se puso a sí mismo.

El contrato que hay que implementar está en docs/mcp-contract.md.

## Dos trampas conocidas, para no encontrarlas

- status: "indexing" no es aprobación. Un language server recién arrancado
  devuelve lista vacía mientras indexa; leer eso como "compila limpio" hace que
  el gate apruebe código roto. Es la falla más cara del proyecto y el contrato
  está construido alrededor de hacerla imposible. Ver ADR-006 y ADR-010.

- Los servidores de .mcp.json con scope de proyecto piden aprobación
  interactiva. En headless no hay quién apruebe y no da error: el agente corre
  sin las tools de LSP, en silencio. Ver riesgo R5 en ROADMAP.md.

## Regla de costo

Este es el bloque donde más tienta "probarlo con una corrida real". No lo
hagas: el servidor se verifica consultándolo a mano, no lanzando claude -p.
La regla de oro 3 de AI.md aplica igual acá que en los tests.

## Disparador de reversión

Si al miércoles 13/08 no hay diagnostics reales llegando, cambiá a OmniSharp y
actualizá ADR-006 con la razón. No sigas peleando contra Roslyn LSP: el plan B
está escrito en el propio ADR precisamente para poder tomarlo sin discutirlo.

## Al cerrar el bloque

- ADR-006 y ADR-010 pasan a Aceptada, o se corrigen con la razón escrita. No se
  reescribe la historia: si la decisión cambió, se dice por qué.
- ROADMAP.md: bloque en ✅ con fecha y su entrada en el historial completado.
- El ADR de la decisión 1.

## Entorno ya verificado, no lo redescubras

.NET SDK 10.0.300 · Node v24.18.0 / npm 11.16.0 · claude en el PATH.
```
