# Contrato del servidor MCP de LSP

> **Qué es este documento.** La interfaz entre el servidor MCP que envuelve los language servers y sus dos consumidores. Es un contrato: lo implementa `Orchestrator.Lsp` de un lado y lo consumen los agentes de capa del otro. El **porqué** de cada decisión vive en `DECISIONS.md` (ADR-004, ADR-005, ADR-006, ADR-010); acá va el **qué**.
>
> **Estado a 2026-08-10: implementado y verificado contra los dos servidores reales.** Lo implementa `src/Orchestrator.LspServer/`. Las firmas del diseño en papel se sostuvieron; se agregó el campo opcional `statusDetail` y un endpoint `/health` fuera del contrato MCP, ambos registrados en ADR-010. El Bloque 4 lo consumió por primera vez en un loop de revisión y encontró dos formas más de veredicto falso, las dos por el estado del documento; están abajo y las dos están cerradas.
>
> **Para verlo funcionar:** `dotnet run --project src/Orchestrator.LspServer.ManualVerification`. Arranca el servidor sobre los fixtures rotos a propósito de `fixtures/`, consulta las cinco tools, arregla un archivo en disco y crea otro roto a mitad de sesión para comprobar que el veredicto sigue al disco. Arranca language servers reales, así que no es parte de la suite de tests (`AI.md`, regla de oro 3).

## Los dos consumidores

| Consumidor | Qué necesita | Cuándo consulta |
|---|---|---|
| **Agentes de capa** (dominio, API, frontend) | Navegación: qué firma tiene un símbolo, quién lo referencia, qué contiene un archivo | Durante su turno, mientras escriben código |
| **Orquestador** (`GraphRunner`) | Un veredicto agregado y estable | Entre nodos del grafo, para decidir la transición |

Ambos hablan con **el mismo servidor**. Esa es la razón de ser del contrato: si el gate y el agente consultaran instancias distintas, podrían ver diagnostics distintos y el grafo tomaría decisiones sobre una realidad que el agente no comparte.

## Transporte: HTTP

El servidor corre como proceso HTTP y se declara en el `.mcp.json` del workspace generado:

```json
{
  "mcpServers": {
    "lsp": {
      "type": "http",
      "url": "http://127.0.0.1:{puerto}/mcp",
      "timeout": 120000
    }
  }
}
```

Los subagentes lo referencian **por nombre** (`mcpServers: [lsp]`), nunca con una definición inline. Tres razones, en orden de peso:

1. **Un solo servidor para los dos consumidores.** Es lo que garantiza que el veredicto del gate y lo que ve el agente sean la misma cosa.
2. **Los language servers indexan una sola vez por corrida.** Una definición inline en el frontmatter de un subagente se conecta al arrancar ese subagente y se desconecta al terminar: con stdio inline, cada spawn levantaría su propio servidor de lenguaje y pagaría el indexado desde cero — que es justo cuando devuelve falsos verdes (ver `status` más abajo). Una referencia por nombre comparte la conexión de la sesión padre.
3. **HTTP reconecta solo** con backoff exponencial ante una caída; stdio no.

### Trampa de arranque — aprobación de `.mcp.json`

Los servidores declarados en un `.mcp.json` con scope de proyecto **requieren aprobación interactiva** la primera vez. En `claude -p` headless no hay nadie para aprobar, y el resultado no es un error: el agente simplemente corre **sin las tools de LSP**, en silencio, y el pipeline degrada a generación a ciegas.

El orquestador tiene que escribir el servidor en la lista `enabledMcpjsonServers` del `settings.json` del workspace generado al prepararlo. Verificar que las tools están efectivamente disponibles es parte del arranque, no una comprobación opcional.

## Tools

### `diagnostics`

El veredicto. Único tool que consumen los dos lados.

```
diagnostics(scope: string) -> DiagnosticsResponse
```

| Parámetro | Descripción |
|---|---|
| `scope` | Ruta relativa a la raíz del workspace: un archivo, un directorio, o `"."` para todo el workspace |

```jsonc
// Respuesta
{
  "status": "ready",        // "ready" | "indexing"
  "statusDetail": null,     // presente solo con "indexing": qué se está esperando
  "total": 3,               // diagnostics que existen en el scope
  "truncated": false,       // true si items < total
  "items": [
    {
      "filePath": "src/Api/Controllers/TareasController.cs",
      "range": { "startLine": 42, "startColumn": 17, "endLine": 42, "endColumn": 34 },
      "severity": "error",  // "error" | "warning" | "information" | "hint"
      "code": "CS1061",
      "message": "'Tarea' does not contain a definition for 'Completar'",
      "source": "roslyn"    // "roslyn" | "typescript"
    }
  ]
}
```

#### `status` — el campo que impide el falso verde

Un language server recién arrancado devuelve una lista vacía mientras todavía está indexando. Si el gate lee eso como "compila limpio", **aprueba código que no compila** — y un gate que aprueba de más es peor que no tener gate, porque el grafo avanza con confianza injustificada.

Por eso la respuesta separa las dos situaciones de forma que no se puedan confundir:

- `status: "ready"` con `items: []` significa **no hay errores**.
- `status: "indexing"` significa **todavía no sé**, y el contenido de `items` no es concluyente.

El orquestador trata `indexing` como "esperar y reconsultar", nunca como aprobación. Es una obligación del consumidor, y se testea con `FakeLanguageServer`.

**Si algún servidor del scope está indexando, la respuesta entera es `indexing`**, aunque el otro ya esté listo. Un veredicto parcial leído como completo es un falso verde.

##### Cómo se decide `status`, servidor por servidor

Verificado en el Bloque 2. **En ningún caso hay un temporizador**: un `sleep` estimado es exactamente cómo un gate termina aprobando código roto.

| Servidor | Señal de que se puede confiar |
|---|---|
| Roslyn | La notificación `workspace/projectInitializationComplete`, que emite al terminar de cargar la solución |
| `typescript-language-server` | No tiene fase de carga equivalente y no soporta *pull* de diagnostics: la garantía es por documento, esperando su primera publicación de `textDocument/publishDiagnostics` |

##### `statusDetail`

Opcional; presente solo cuando `status` es `"indexing"`. Dice **qué** se está esperando:

```jsonc
{ "status": "indexing", "total": 0, "truncated": false, "items": [],
  "statusDetail": "Roslyn is loading the solution 'BrokenCSharp.slnx'" }
```

No estaba en el diseño en papel. Se agregó durante el Bloque 2 por una razón concreta: un `indexing` eterno y mudo es indistinguible de un servidor colgado, y un estado que no se puede diagnosticar termina siendo un estado que alguien decide ignorar.

##### La otra vía al falso verde: las rutas

Los dos extremos no escriben igual la misma ruta de Windows. Nosotros emitimos `file:///F:/proyecto/src/tarea.ts`; `typescript-language-server` contesta sobre `file:///f%3A/proyecto/src/tarea.ts`.

Comparadas como texto son archivos distintos, y el daño es preciso: los diagnostics publicados quedan bajo una clave que nadie consulta y **el archivo parece limpio**. Es el mismo falso verde de `status`, llegando por normalización en vez de por timing. Toda conversión pasa por un único lugar (`WorkspacePaths`) y tiene test de regresión.

##### Las dos vías que encontró el Bloque 4, las dos por el estado del documento

Consultar la misma tool dos veces en una corrida —que es lo que hace un loop de revisión— destapó
dos formas más de que la respuesta sea falsa, y ninguna tiene que ver con el timing ni con las
rutas. **Las dos son responsabilidad del servidor y ya están cerradas**; se documentan acá porque
son propiedades que el consumidor da por sentadas sin decirlo.

| Situación | Qué contestaba | Por qué |
|---|---|---|
| El agente **arregla** un archivo y se vuelve a consultar | El mismo error, para siempre | Un language server contesta sobre el texto que le pasaron, no sobre el archivo. Sin notificarle el cambio, el veredicto queda congelado. **Falso rojo:** no aprueba código roto, pero manda a un agente a rehacer trabajo ya hecho y lo da por agotado |
| El agente **crea** un archivo nuevo y se consulta | Ningún diagnostic de ese archivo | Un archivo que aparece después de cargar la solución no está en el sistema de proyectos, y abrirlo no lo mete ahí. Nadie lo analiza, y eso llega como "sin errores". **Falso verde**, y el caso importa porque los agentes crean archivos todo el tiempo |

La garantía que el contrato ofrece, entonces, dicha explícitamente: **cada respuesta de
`diagnostics` refleja el estado del disco en el momento de la consulta**, incluidos los archivos
creados o modificados después de que el servidor arrancó.

#### Truncado y orden

Un solo archivo roto puede producir cientos de diagnostics, y el prompt del agente tiene presupuesto. La respuesta acota `items` y declara el recorte con `total` y `truncated`, en un orden fijo:

1. Por severidad: `error` antes que `warning`, `warning` antes que el resto.
2. Después por `filePath` alfabético.
3. Después por `startLine`.

El orden importa porque el recorte se hace por el final: con esta prioridad, lo que sobrevive al truncado es siempre lo que bloquea la compilación. Sin orden estable, el loop de revisión le entrega al agente un subconjunto arbitrario y lo manda a corregir warnings mientras los errores siguen ahí.

#### `range` es 1-based

El protocolo LSP numera líneas y columnas desde cero. Los mensajes de compilador, los editores y las personas cuentan desde uno. **La conversión se hace en el servidor** y el contrato expone 1-based, para que ni el agente ni el orquestador tengan que acordarse de sumar uno. Es la clase de off-by-one que produce un bug silencioso donde el agente edita la línea equivocada.

#### Sin campo `layer`

El contrato **no** dice a qué capa pertenece un diagnostic. Mapear ruta → capa es concern del orquestador: es su modelo de dominio el que sabe que `src/Domain/**` es del agente de dominio. Mantenerlo fuera del servidor lo deja agnóstico del proyecto que está analizando, y deja la decisión de a qué agente volver en `Orchestrator.Application`, que se testea con fakes.

### `definition`

Dónde está definido el símbolo que hay en una posición, y con qué firma.

```
definition(filePath: string, line: int, column: int) -> DefinitionResponse
```

```jsonc
{
  "status": "ready",
  "found": true,
  "filePath": "src/Domain/Tarea.cs",
  "range": { "startLine": 18, "startColumn": 5, "endLine": 18, "endColumn": 60 },
  "signature": "public Result Completar(IReadOnlyList<Tarea> dependencias)",
  "documentation": "Marca la tarea como completada si RN-01 se satisface."
}
```

`signature` es lo que hace útil el tool: le permite al agente de API **preguntar** qué firma tiene el método del dominio en vez de asumirla. Sin ese campo, el agente tendría que abrir el archivo entero y deducirla.

### `references`

Quién usa este símbolo, antes de renombrarlo o cambiarle la firma.

```
references(filePath: string, line: int, column: int) -> ReferencesResponse
```

```jsonc
{
  "status": "ready",
  "total": 4,
  "truncated": false,
  "items": [
    { "filePath": "src/Api/Controllers/TareasController.cs",
      "range": { "startLine": 42, "startColumn": 17, "endLine": 42, "endColumn": 25 },
      "preview": "var resultado = tarea.Completar(dependencias);" }
  ]
}
```

`preview` es la línea de texto donde ocurre la referencia: evita que el agente tenga que abrir cada archivo para saber si le importa.

### `documentSymbol`

El outline de un archivo, sin leerlo entero.

```
documentSymbol(filePath: string) -> SymbolsResponse
```

```jsonc
{
  "status": "ready",
  "items": [
    { "name": "Tarea", "kind": "class", "range": {...},
      "children": [
        { "name": "Completar", "kind": "method",
          "signature": "public Result Completar(IReadOnlyList<Tarea> dependencias)",
          "range": {...} }
      ] }
  ]
}
```

### `workspaceSymbol`

Encontrar un símbolo por nombre sin saber en qué archivo vive.

```
workspaceSymbol(query: string) -> SymbolsResponse
```

Misma forma de respuesta que `documentSymbol`, con `filePath` en cada item y sin anidamiento. Es el tool que resuelve *"¿dónde está la entidad Tarea?"* sin obligar al agente a recorrer el árbol de directorios.

> **Limitación conocida.** Un language server puede reportar el workspace como cargado mientras su índice de símbolos todavía se arma, y una consulta temprana vuelve vacía — indistinguible, en el contrato, de *"ese símbolo no existe"*. Es la misma clase de problema que `status` resuelve para `diagnostics`, sin una señal equivalente disponible. **El gate no depende de esta tool**, así que no bloquea el pipeline; el consumidor real es un agente de capa, que puede reintentar. Registrado como deuda D7 en `ROADMAP.md`.

## Por qué cuatro tools de navegación y no solo `diagnostics`

Porque `diagnostics` solo es la mitad del valor de la capa LSP, y la mitad que un `dotnet build` también daría. ADR-004 dejó escrito el criterio de falsación: **si al final del proyecto la capa LSP terminó exponiendo únicamente diagnostics, esto es un `dotnet build` caro y la decisión no se sostiene.** `definition`, `references`, `documentSymbol` y `workspaceSymbol` son lo que hace que el agente pueda preguntar en vez de asumir, y son la parte del contrato que hay que defender en la entrevista.

## Errores

Un fallo del servidor no es un diagnostic. Los tools devuelven error de MCP —no una respuesta vacía— cuando:

| Situación | Motivo |
|---|---|
| El `filePath` no existe o queda fuera del workspace | Entrada inválida |
| El language server del lenguaje pedido no está corriendo | Fallo de infraestructura |
| El language server no responde dentro del timeout | Fallo de infraestructura |

La distinción es la misma que hace `AI.md` para el orquestador: un archivo que no compila es un **estado** que el grafo tiene que poder razonar; un language server caído es una **excepción**. Devolver `items: []` ante un servidor caído reintroduce el falso verde por la puerta de atrás.

## Ciclo de vida

Los procesos se anidan: **el orquestador lanza el servidor MCP, y el servidor MCP es dueño de los dos language servers** (ADR-013 — el que sostiene las conexiones LSP tiene que ser el que contesta las tool calls). Arrancan al preparar el workspace y se apagan de forma determinista al terminar la corrida, exitosa o no. **Un language server huérfano tras una corrida fallida es un bug**, no un detalle: mantiene abiertos handles sobre `output/`, que ADR-008 exige poder borrar y regenerar de cero. Red de seguridad manual: `powershell.exe -ExecutionPolicy Bypass -File tools/kill-language-servers.ps1` — ni `pwsh`, que no está instalado en la máquina de desarrollo, ni sin el flag, que la política de ejecución rechaza.

Los language servers arrancan **en segundo plano**, no durante el arranque del host HTTP: cargar una solución tarda segundos, y la respuesta honesta durante esos segundos es `status: "indexing"` — que requiere que el servidor ya esté contestando para poder decirla.

## `/health` — fuera del contrato MCP

```jsonc
// GET /health
{ "workspaceRoot": "F:/.../output",
  "languageServers": [ { "source": "roslyn", "status": "ready", "detail": "ready" } ] }
```

No es una tool y los agentes no lo usan. Existe para que el orquestador pueda **verificar** al arrancar que la capa LSP está viva —en vez de asumirlo (`AI.md`, fallar rápido)— sin abrir una sesión MCP para preguntarlo.
