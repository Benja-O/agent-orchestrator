# Briefing: Orquestador de Agentes con LSP y Grafos

## Contexto del desafío

Un cliente/entrevistador propuso un desafío técnico de ~2.5 semanas: construir una solución de orquestación de agentes de IA que use LSP y grafos, usando Claude Code, para luego hacer una entrevista técnica sobre lo construido.

**Input:** un spec de requerimiento bajo filosofía SDD (Spec-Driven Development).
**Output esperado:** los agentes deben construir una solución web en .NET + React a partir de ese spec.

No se exigió lenguaje para el orquestador en sí — solo para lo que el orquestador produce como resultado.

## Decisiones ya tomadas

### Herramienta
- **Claude Code** (no OpenCode). Justificación: ya se usa en producción a diario, se conocen sus workarounds, y refuerza la conversación ya tenida con el cliente sobre el tema.
- Se opera bajo **plan Pro de Anthropic**, invocando siempre la CLI (`claude -p` en modo headless) — nunca la API directa con key — para que el uso corra contra la suscripción y no se facture por token. Si en la semana 1 se pega contra el límite de 5hs seguido, evaluar créditos de uso o subir a Max.

### Lenguaje del orquestador
- **.NET (consola / Worker Service)**. Es el lenguaje más fuerte del desarrollador, corre fácil en modo headless, e invoca `claude -p` como subproceso vía `System.Diagnostics.Process`.

### Arquitectura general
Grafo de agentes con feedback real de LSP (no asunción ciega de que el código compiló):

1. **Spec Analyzer** — parsea el spec SDD y lo descompone en plan de tareas por capa (dominio, aplicación, API, frontend).
2. **Agentes de capa** (Domain, API .NET, React) — cada uno como subagente/skill de Claude Code, scope acotado por capa.
3. **Capa LSP como fuente de verdad** — servidor MCP liviano que envuelve OmniSharp (C#) y typescript-language-server (React/TS), expone diagnostics (errores de compilación, símbolos, referencias) como tool que los agentes consultan después de cada cambio.
4. **El grafo** — nodos = agentes/pasos, aristas condicionales: si LSP devuelve error, vuelve al agente de esa capa con el diagnóstico como input (loop de revisión); si compila limpio, avanza al siguiente nodo. Se implementa como código propio (máquina de estados en .NET), sin necesidad de un framework pesado tipo LangGraph.

Filosofía de fondo: reusar el mismo enfoque de rigor que ya se aplica en el repo personal de trading algorítmico (gate obligatorio antes de implementar, ADRs documentando decisiones) — coherencia narrativa además de buena práctica.

### Cómo consume el spec
Archivo de texto/markdown (`spec.md`) pasado como argumento por línea de comandos al arrancar el orquestador. Sin UI ni base de datos para esto — input de arranque, no algo que cambia en runtime.

### Artefacto de juguete (la app que el orquestador debe producir)
**Gestor de tareas con dependencias simples**, elegido porque tiene al menos una regla de negocio real (no CRUD vacío):
- Entidades: Tarea (título, estado, fecha límite) y relación "depende de" entre tareas.
- Regla no trivial: no se puede marcar una tarea como completada si tiene una tarea dependiente sin completar.
- CRUD básico + endpoint que intente violar la regla (para verificar que el agente la implementó de verdad).
- Frontend: lista con checkbox y bloqueo visual/error al intentar violar la regla.

Razón de este alcance: chico para armar el spec en una tarde y correr el pipeline completo en la semana 1, pero con estado e invariantes suficientes para validar si el orquestador sostiene una regla de dominio real antes de escalar.

### Presentación al cliente — dos entregables separados
1. **Repo del orquestador** (el proyecto que se evalúa de verdad): README con la arquitectura del grafo, la integración LSP y con Claude Code; DECISIONS.md con las decisiones arquitectónicas (mismo estilo que el repo de trading).
2. **Repo de la app generada**: presentado explícitamente como output, con una aclaración en el README de que fue generada por el orquestador, no escrita a mano.

En la demo: mostrar primero el orquestador corriendo en vivo contra el spec (loops de corrección incluidos), y al final el resultado generado. El foco es el proceso, no el resultado.

### Plan tentativo de 2.5 semanas
- **Semana 1:** servidor MCP de LSP + esqueleto del grafo (estado, nodos, transiciones) + Spec Analyzer funcionando end-to-end sobre el spec de juguete.
- **Semana 2:** agentes de capa con Claude Code, loop de revisión contra diagnostics de LSP, primera corrida completa spec → solución compilable.
- **Días finales:** pulir, documentar en DECISIONS.md, preparar la demo.

## Puntos abiertos para seguir afinando con Opus

- Diseño detallado del protocolo entre el orquestador .NET y el servidor MCP de LSP (qué tools exactas expone, formato de los diagnostics).
- Cómo se define el scope exacto de cada subagente de Claude Code (prompts, permisos, qué archivos puede tocar cada uno).
- Formato exacto del spec.md de juguete (para poder empezar a escribirlo ya).
- Estrategia de testing del propio orquestador (no de la app generada) — cómo se valida que el grafo y el loop de revisión funcionan como se espera.
- Nivel de logging/observabilidad del grafo para poder mostrar el proceso en la demo de forma clara.
