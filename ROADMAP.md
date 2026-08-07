# ROADMAP — Orquestador de agentes con LSP y grafos

> **Propósito:** mantener visibilidad del plan completo entre sesiones de trabajo. Cualquier
> sesión con Claude Code debe leer este archivo primero para entender en qué punto está el
> proyecto y qué corresponde hacer.
>
> **Reglas:**
> - Bloque completado: ✅ con fecha. En curso: 🔄. Pendiente: ⬜. Abortado: ❌ con la razón.
> - **Cada bloque tiene un criterio de salida verificable.** Un bloque no se marca ✅ porque
>   "ya está hecho", sino porque su criterio se cumplió y se puede mostrar.
> - `DECISIONS.md` es lo cerrado; este archivo es lo abierto. Cuando una decisión pendiente
>   se resuelve, se escribe su ADR y se saca de la sección "Decisiones pendientes".
> - Actualizar al retomar el proyecto y al cerrar cada bloque, en el mismo commit que el
>   trabajo.

---

## Estado (2026-08-07) — Bloque 0 cerrado; próximo: Bloque 1

Repo abierto y con el andamiaje documental completo. Las nueve decisiones fundacionales están
cerradas en `DECISIONS.md`; el briefing original queda como registro de origen en
[orquestador-agentes-briefing.md](orquestador-agentes-briefing.md). Todavía no hay una línea
de código.

**Plazo: vie 2026-08-07 → lun 2026-08-24 (17 días).** Es el condicionante que ordena todas
las prioridades de abajo: hay tiempo para un pipeline que funcione de punta a punta sobre un
artefacto chico, y no lo hay para nada más.

**Lo próximo — Bloque 1.** Escribir `specs/gestor-tareas.md` y cerrar el contrato de tools del
servidor MCP. Son las dos decisiones pendientes que bloquean todo el resto: sin el spec no hay
qué analizar, y sin el contrato no hay gate que construir.

---

## Plan general

| Bloque | Fechas | Contenido | Criterio de salida | Estado |
|---|---|---|---|---|
| **0** | vie 07/08 | Andamiaje documental (`CLAUDE.md`, `AI.md`, `DECISIONS.md`, `ROADMAP.md`, README stub) + `git init` | Los cinco documentos existen, las referencias cruzadas resuelven, commit inicial hecho | ✅ 07/08 |
| **1** | sáb 08 – lun 10/08 | `specs/gestor-tareas.md` + contrato de tools del servidor MCP | El spec está escrito en formato SDD y el contrato de tools tiene firmas y formato de `Diagnostic` cerrados, con su ADR | ⬜ |
| **2** | mar 11 – vie 14/08 | Servidor MCP de LSP sobre Roslyn LSP + `typescript-language-server` | Una consulta manual al servidor devuelve diagnostics reales de un `.cs` roto a propósito, **y** una consulta de `definition` devuelve la ubicación correcta. ADR-006 pasa a `Aceptada` o se revierte a OmniSharp | ⬜ |
| **3** | mié 12 – dom 16/08 | Esqueleto del grafo (estado, nodos, transiciones) + Spec Analyzer | El grafo corre end-to-end contra `FakeAgentRunner` y `FakeLanguageServer`, incluido el ciclo de revisión y las tres vías de terminación. La suite corre sin `claude` en el `PATH` | ⬜ |
| **4** | lun 17 – vie 21/08 | Agentes de capa (dominio, API .NET, React) + loop de revisión contra diagnostics reales | Un error inyectado a propósito hace volver el grafo al agente de esa capa, y la siguiente iteración lo corrige. Visible en el log | ⬜ |
| **5** | vie 21 – sáb 22/08 | Primera corrida completa: spec → app compilable | `output/` se genera de cero, la app compila, y el endpoint que intenta violar la invariante de ADR-009 la rechaza | ⬜ |
| **6** | dom 23 – lun 24/08 | Pulido, README real, `DECISIONS.md` al día, demo ensayada | El README explica la arquitectura del grafo, la integración LSP y la integración con Claude Code. La demo corre de principio a fin sin intervención | ⬜ |

**Los bloques 2 y 3 se solapan a propósito.** El grafo se construye contra
`FakeLanguageServer` mientras el servidor MCP real se termina — es la regla de oro 3 de
`AI.md` pagando dividendos de cronograma, no solo de costo. Si el Bloque 2 se atrasa, el 3
no se bloquea.

### Detalle por bloque

**Bloque 1 — spec y contrato.** Los dos ítems que desbloquean todo lo demás. El spec no se
puede analizar si no existe; el gate no se puede construir si no está definido qué devuelve.
Producen ADR-010 (contrato de tools y formato de diagnostics) y el ADR del formato de spec.

**Bloque 2 — la capa LSP.** El riesgo técnico concentrado del proyecto. Dos servidores de
lenguaje con ciclos de vida distintos, envueltos por un servidor MCP. La trampa a evitar está
escrita en `AI.md`: consultar antes de que termine el indexado devuelve un falso verde.

**Bloque 3 — el grafo.** Lo que más se evalúa. Máquina de estados propia (ADR-003) con las
tres vías de terminación obligatorias. Al cerrarlo, revisar `AI.md`: deja de ser arquitectura
objetivo y pasa a describir código real.

**Bloque 4 — los agentes.** Acá se define el scope de cada subagente y se paga la primera
cuota real del plan Pro. Es el bloque donde el riesgo de límite de uso se materializa.

**Bloque 5 — la corrida completa.** El criterio de salida no es "la app compila" sino "la app
compila **y** sostiene la invariante". Compilar es lo que verifica el gate de LSP; la
invariante es lo que verifica que el pipeline transmitió una regla de negocio a través de tres
capas (ADR-004, consecuencia final).

**Bloque 6 — la entrega.** Dos repos (ADR-008). El README del orquestador explica la
arquitectura; el de la app generada aclara que es output, no trabajo manual.

---

## Decisiones pendientes

Los cinco puntos que el briefing dejó abiertos. No están en `DECISIONS.md` porque no están
decididos. Cada uno se resuelve en un bloque y produce un ADR.

| # | Decisión | Se resuelve en | Produce |
|---|---|---|---|
| 1 | **Protocolo orquestador ↔ servidor MCP de LSP:** qué tools expone exactamente, con qué firmas, y cuál es el formato de los diagnostics | Bloque 1 | ADR-010 |
| 2 | **Scope de cada subagente de Claude Code:** prompts, permisos, qué archivos puede tocar cada agente de capa | Bloque 1 (diseño) / Bloque 4 (validación) | ADR-011 |
| 3 | **Formato del `spec.md` bajo filosofía SDD:** estructura del documento de entrada que el Spec Analyzer parsea | Bloque 1 | ADR-012 |
| 4 | **Estrategia de testing del propio orquestador** (no de la app generada): cómo se valida que el grafo y el loop de revisión funcionan | Bloque 3 | ADR-013 |
| 5 | **Nivel de logging y observabilidad del grafo** para que la demo muestre el proceso con claridad | Bloque 3 (diseño) / Bloque 6 (pulido) | ADR-014 |

Notas sobre el estado de cada una:

- **#1 y #3 son las que bloquean.** Sin ellas el Bloque 2 y el Spec Analyzer no arrancan.
- **#2** tiene una restricción ya fijada: los agentes de capa se definen como subagentes o
  skills de Claude Code, no como prompts sueltos (ADR-001, consecuencias).
- **#4 está parcialmente resuelta de antemano** por la regla de oro 3 de `AI.md` (fakes, sin
  invocar la CLI real). Lo que falta es el diseño concreto: qué escenarios se testean, cómo
  se graban las respuestas del `FakeAgentRunner`, y cómo se verifica que la suite no está
  invocando nada real.
- **#5** dejó de ser una decisión de infraestructura al cerrarse ADR-007: sin UI, el log es la
  única ventana al grafo y es lo que se proyecta en la demo.

---

## Riesgos

**R1 — Límite de 5 h del plan Pro.** *El riesgo real de cronograma.* Un loop de revisión que
regenera código contra diagnostics consume cuota rápido, y se agota justo cuando más se
necesita: depurando. Se materializa en el Bloque 4.

- *Mitigación en firme:* regla de oro 3 de `AI.md` (la suite completa corre con fakes) más
  los límites de iteración de ADR-003. Todo lo que se pueda depurar sin agente real, se
  depura sin agente real.
- *Disparador de escalada:* si durante la semana 1 se pega contra el techo de forma sostenida
  —no una vez aislada— evaluar créditos de uso o subir a Max. La decisión se toma con el dato,
  no por precaución.

**R2 — Deriva de alcance de la app generada.** El artefacto de juguete es chico a propósito
(ADR-009). Agregarle features es la forma más fácil de perder el Bloque 5, porque cada
invariante extra es superficie nueva donde el agente puede fallar por razones que no son
culpa del orquestador. *Mitigación:* el alcance de la app está congelado en ADR-009; ampliarlo
requiere un ADR que lo justifique contra el cronograma.

**R3 — La integración de Roslyn LSP resulta más difícil de lo previsto.** ADR-006 está en
`Propuesta`, no verificado. Roslyn LSP se distribuye distinto que OmniSharp y su arranque
desde un proceso .NET puede complicarse. *Mitigación:* el Bloque 3 no depende del Bloque 2
(fakes), así que un atraso acá no bloquea el grafo; y el plan B —volver a OmniSharp— está
escrito en el propio ADR. *Disparador:* si al mié 13/08 no hay diagnostics reales llegando,
cambiar a OmniSharp y actualizar ADR-006 con la razón.

**R4 — El gate verifica compilación, no corrección.** Una regla de negocio puede estar
ausente y el código compilar perfecto (ADR-004, consecuencia final). El proyecto ya lo tiene
contemplado —el endpoint que intenta violar la invariante de ADR-009 es exactamente esa
verificación— pero conviene que no se pierda de vista al llegar apurado al Bloque 5. Es la
diferencia entre "el pipeline produce código que compila" y "el pipeline produce la app
pedida".

---

## Deudas

Cosas conscientemente postergadas. Ninguna bloquea la entrega; todas son lo primero a
retomar si el proyecto continuara.

| # | Deuda | Origen | Trigger para cobrarla |
|---|---|---|---|
| D1 | Sin persistencia de corridas: una corrida interrumpida se reinicia desde cero | ADR-007 | Si el tiempo de una corrida completa crece a punto de volver caro reiniciar |
| D2 | Sin paralelismo entre agentes de capa: el grafo es estrictamente secuencial | ADR-003 | Si el tiempo de corrida se vuelve el cuello de botella de la iteración |
| D3 | El grafo es código, no configuración: cambiar el pipeline requiere recompilar | ADR-003 (alternativa descartada) | Cuando exista un segundo pipeline real que justifique la generalización |
| D4 | Sin gate de tests sobre la app generada, solo gate de compilación | ADR-004 (alternativa descartada) | Fuera del alcance de 2.5 semanas; es la extensión natural del gate |

---

## Historial completado

### ✅ Bloque 0 — Andamiaje documental (2026-08-07)

Cinco documentos y el repo git, antes de escribir código. La razón de invertir el primer día
acá: en este proyecto el registro de decisiones **es parte del entregable evaluado**, no
documentación de apoyo.

- **`DECISIONS.md`** — ADR-001..009, las decisiones que ya venían tomadas del briefing. Se
  declaró en el encabezado la excepción a la regla de casa de "no hay ADR sin código
  asociado", con sus tres razones. ADR-006 quedó en `Propuesta`, no `Aceptada`: la
  superioridad de Roslyn LSP sobre OmniSharp es conocimiento del ecosistema, no algo
  verificado en este proyecto.
- **`AI.md`** — arquitectura objetivo y cuatro reglas de oro, cada una con su forma de
  verificarse. Explícitamente marcado como contrato a cumplir, no descripción de algo
  existente; se revisa al cierre del Bloque 3.
- **`CLAUDE.md`** — comportamiento, control de costo, y la distinción entre este archivo y la
  plantilla `templates/generated-app-CLAUDE.md` que el orquestador inyecta en el workspace
  generado. Son dos cosas distintas con el mismo nombre y confundirlas es un error
  conceptual.
- **`ROADMAP.md`** — siete bloques fechados con criterio de salida verificable, las cinco
  decisiones pendientes del briefing, cuatro riesgos y cuatro deudas.
- **`README.md`** — stub. Se completa en el Bloque 6, cuando haya arquitectura real.

Verificación hecha: los ADR citados desde otros documentos resuelven contra `DECISIONS.md`
(ADR-010..014 aparecen solo como referencias hacia adelante en la tabla de decisiones
pendientes); los cinco puntos abiertos del briefing están en este archivo y en ninguno de los
otros; `git status` limpio.
