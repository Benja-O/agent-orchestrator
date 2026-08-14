# CLAUDE.md — aplicación generada

> **Este archivo es un artefacto de runtime.** El orquestador lo copia al workspace de la app generada (`output/`) antes de lanzar el primer agente. No es documentación del repo del orquestador; el `CLAUDE.md` de ese repo es otro archivo, con otro propósito, y lo lee otro asistente. Ver la sección "Los dos CLAUDE.md" del `CLAUDE.md` del orquestador.

Estás trabajando dentro de una aplicación **generada por un orquestador de agentes** a partir de un spec. No la escribió una persona y no hay historia previa que respetar.

## Cómo se trabaja acá

**El spec es la fuente de verdad.** Está en `spec.md`, en la raíz de este workspace. Sus reglas de negocio están numeradas `RN-nn` y sus criterios de aceptación `CA-nn`. Cualquier cosa que hagas se justifica contra un identificador del spec; si no se puede atribuir a ninguno, no va.

**No inventes requisitos.** Si el spec no lo pide, no existe. Ante una ambigüedad que afecte lo que estás construyendo, decilo en tu respuesta en lugar de resolverla por tu cuenta — el orquestador está leyendo.

**Trabajás dentro de tu capa.** Cada agente tiene una carpeta asignada y solo escribe ahí. El resto del workspace es de solo lectura. Si necesitás un cambio fuera de tu alcance, reportalo; no lo hagas.

| Capa | Carpeta |
|---|---|
| Dominio | `src/Domain/` |
| API | `src/Api/` |
| Frontend | `src/Frontend/` |

Las carpetas las fija el orquestador, no vos: es su mapa de rutas el que decide a qué agente le vuelve un error del gate. Un archivo fuera de esas tres carpetas es un archivo que nadie puede arreglar, y una corrida que lo encuentra se detiene.

**Las reglas de negocio viven en el dominio, una sola vez.** La API traduce lo que el dominio decide y el frontend muestra lo que la API responde. Una condición del spec replicada en dos capas se desincroniza; si te encontrás escribiendo la misma regla dos veces, una de las dos está en el lugar equivocado.

## El servidor de lenguaje

Tenés un servidor MCP llamado `lsp` con tools sobre el código de este workspace. **Usalo, no adivines:**

| Tool | Para qué |
|---|---|
| `definition` | La firma exacta de algo que vas a invocar |
| `references` | Quién usa un símbolo, antes de cambiarlo |
| `documentSymbol` | Qué contiene un archivo, sin abrirlo entero |
| `workspaceSymbol` | Dónde vive un símbolo, sin recorrer directorios |
| `diagnostics` | Si lo que escribiste compila |

**Consultá `diagnostics` antes de dar por terminada cualquier tarea.** Y leé el campo `status`:

- `"ready"` con lista vacía: no hay errores.
- `"indexing"`: el servidor todavía está analizando y **no sabe**. Esperá y volvé a consultar. Una lista vacía con `status: "indexing"` no significa que compile.

No declares nada terminado sin haberlo verificado así. El orquestador consulta el mismo gate: si tu reporte no coincide con lo que el gate ve, la tarea vuelve a vos con los diagnostics como input.

## Stack

- Backend en .NET, API HTTP.
- Frontend en React con TypeScript, servido por **Vite**. El `vite.config.ts` es parte del esqueleto: no lo escribas ni lo modifiques.
- Persistencia con Entity Framework Core, proveedor **InMemory**: sin migraciones, sin base de datos externa, sin cadena de conexión.
- Sin autenticación.

**La API no elige en qué dirección escucha.** La recibe de su configuración —`app.Run()` sin argumentos, y nada de `UseUrls`, `ListenLocalhost` ni un puerto escrito en el código—, porque el orquestador la arranca en un puerto que elige él para verificarla. Una dirección fija en el código gana sobre la que le pasa el orquestador, y entonces la app queda escuchando donde nadie la consulta: **arranca perfecto y el gate reporta que nunca contestó.**

**Son dos procesos y dos orígenes.** El frontend no se sirve desde la API, así que toda llamada del navegador a la API es cross-origin y la API la tiene que aceptar explícitamente en desarrollo. Es la clase de fallo que no aparece en ningún gate de compilación: las dos capas compilan, la API contesta, y la pantalla queda vacía.

**El proveedor InMemory no enforcea integridad referencial.** No delegues ninguna regla de negocio a la base de datos: no las aplicaría, y aunque lo hiciera, dejaría la invariante fuera del código que se puede testear.
