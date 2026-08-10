// PreToolUse hook: rejects a Write or Edit outside the folder the invoked agent owns.
//
// This is the barrier that ADR-011 designed and left as a dated debt (D5): until it existed,
// "each agent only touches its layer" was an instruction in a prompt and nothing enforced it.
//
// Invoked as `node .claude/hooks/restrict-to-layer.js <allowedFolder>`, reading the hook
// payload from stdin. Exit 0 lets the call through; exit 2 blocks it and hands the message on
// stderr back to the agent, which is what lets it correct course instead of just failing.
//
// Two deliberate choices, both learned the hard way in block 4:
//
//   * Node, not PowerShell. `pwsh` is not installed on every Windows machine, and a hook whose
//     interpreter cannot be launched does not fail — Claude Code logs it and lets the write
//     through. A barrier that fails open silently is worse than no barrier, because it is
//     believed. `node` is already a hard dependency of this project.
//   * Unreadable input is a rejection, not a pass. Same reason: the failure modes of this file
//     have to land on the safe side.
//
// It still cannot fail closed if node itself is missing, so the orchestrator verifies at
// startup that the hook actually blocks, rather than assuming it (AI.md, fail fast).

const path = require('node:path');

const allowedFolder = process.argv[2];

if (!allowedFolder) {
  process.stderr.write('restrict-to-layer.js needs the allowed folder as its argument.\n');
  process.exit(2);
}

let payloadText = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => { payloadText += chunk; });
process.stdin.on('end', () => {
  let payload;

  try {
    payload = JSON.parse(payloadText);
  } catch {
    process.stderr.write('El hook de alcance no pudo leer la invocacion. Escritura rechazada.\n');
    process.exit(2);
  }

  const filePath = payload && payload.tool_input ? payload.tool_input.file_path : undefined;

  // A call that names no file has nothing for this hook to say about it.
  if (typeof filePath !== 'string' || filePath.trim() === '') {
    process.exit(0);
  }

  const workspaceRoot = path.resolve(payload.cwd || process.cwd());
  const allowedRoot = path.resolve(workspaceRoot, allowedFolder);
  const target = path.resolve(workspaceRoot, filePath);
  const relative = path.relative(allowedRoot, target);

  // Empty means the target is the folder itself rather than a file in it; a leading '..' means
  // it climbed out; an absolute result means another drive altogether. All three are outside.
  const isInside = relative !== '' && !relative.startsWith('..') && !path.isAbsolute(relative);

  if (isInside) {
    process.exit(0);
  }

  process.stderr.write(
    `Fuera de alcance: solo podes escribir dentro de '${allowedFolder}/'. Rechazado: ${filePath}\n`);
  process.exit(2);
});
