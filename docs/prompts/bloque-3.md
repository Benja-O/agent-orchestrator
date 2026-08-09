# Prompt de arranque — Bloque 3

> **Convención.** Un prompt por bloque, escrito al cerrar el bloque anterior. Es corto a propósito: el repo se autodescribe —`CLAUDE.md` se carga solo y manda leer `ROADMAP.md`, `DECISIONS.md` y `AI.md`— así que el prompt no reexplica la arquitectura. Lleva solo lo que los documentos no dicen: qué decisiones abiertas cierra el bloque, en qué orden atacar, y qué ya está verificado.

Copiar y pegar en un chat nuevo abierto en la raíz del repo:

```
Arrancamos el Bloque 3. Leé ROADMAP.md primero para el estado y el criterio de salida.

## Las dos decisiones abiertas que este bloque tiene que cerrar

Son las dos últimas del briefing. Después de este bloque no queda ninguna.

1. Estrategia de testing del propio orquestador (ADR-014). La regla de oro 3
   de AI.md ya fija el qué —fakes, sin invocar la CLI real—; falta el diseño:
   qué escenarios, cómo se graban las respuestas del FakeAgentRunner, y cómo
   se verifica que la suite no está invocando nada real.

   El Bloque 2 ya dejó probado el patrón de un lado: FakeLanguageServerSession
   sirve respuestas en forma de protocolo y toda la superficie de tools se
   ejercita contra él, 33 tests en 2 segundos. Copialo. El lado difícil es el
   otro: una respuesta de agente es texto libre, no una estructura, así que
   FakeAgentRunner tiene un problema de diseño que el de LSP no tenía.

2. Nivel de logging y observabilidad del grafo (ADR-015). Sin UI (ADR-007) el
   log es la única ventana al grafo y es lo que se proyecta en la demo, así que
   es decisión de producto. Materia prima disponible: los identificadores
   RN-nn / CA-nn del spec (ADR-012) permiten mostrar qué regla se está
   implementando en qué capa, no solo qué nodo corre.

## Criterio de salida

El grafo corre end-to-end contra FakeAgentRunner y FakeLanguageServer,
incluido el ciclo de revisión y las tres vías de terminación. La suite corre
sin `claude` en el PATH.

Las tres vías de terminación no son opcionales y no las da ningún framework
(ADR-003): límite de iteraciones por nodo, detección de no-progreso —el agente
devuelve el mismo conjunto de diagnostics dos veces seguidas—, y fallo terminal
explícito con traza.

## Lo que ya existe y no hay que inventar

La capa LSP funciona contra servidores reales. El grafo no la necesita para
este bloque (se construye contra FakeLanguageServer), pero sí tiene que
consumir su forma exacta, que ya está fija y verificada en
docs/mcp-contract.md:

- `status: "ready" | "indexing"`. El gate trata `indexing` como "esperar y
  reconsultar", NUNCA como aprobación. Es el test más importante que este
  bloque tiene que escribir.
- Los diagnostics vienen 1-based, ordenados por severidad y con `total` /
  `truncated` explícitos.
- Sin campo `layer`: mapear ruta → capa es concern de Orchestrator.Application
  y se decide en este bloque.

## Orden sugerido

Domain primero (GraphState inmutable, Diagnostic, NodeId, las interfaces
IAgentRunner e ILanguageServerGateway), después TestSupport con los fakes,
después Application con el GraphRunner. El Spec Analyzer al final: es el nodo
que menos incertidumbre tiene.

Si en algún momento el orden se siente al revés, es señal de que una
dependencia apunta hacia afuera y hay que mirar la regla de oro 1.

## Regla de costo

Este bloque no debería invocar `claude -p` ni una sola vez. Si aparece la
tentación, es señal de que falta un fake, no de que falte una corrida.

## Al cerrar el bloque

- ADR-014 y ADR-015.
- Revisar AI.md completo: deja de ser arquitectura objetivo y pasa a describir
  código real. Es la revisión que el propio documento se agendó.
- ROADMAP.md: bloque en ✅ con fecha y su entrada en el historial completado.
- docs/prompts/bloque-4.md.

## Entorno ya verificado, no lo redescubras

.NET SDK 10.0.300 · Node v24.18.0 / npm 11.16.0 · Claude Code 2.1.224.

La solución está en src/Orchestrator.slnx. Convenciones del build ya fijadas en
src/Directory.Build.props: net10.0, nullable, TreatWarningsAsErrors.
```
