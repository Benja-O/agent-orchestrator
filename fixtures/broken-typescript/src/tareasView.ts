import { Tarea } from "./tarea";

/**
 * The view side of the fixture: one call that resolves across files, and one that does not
 * compile, so the verification can tell a real diagnostic from an empty answer.
 */
export function completar(tarea: Tarea, prerequisitos: readonly Tarea[]): boolean {
  return tarea.completar(prerequisitos);
}

export function cerrar(tarea: Tarea): boolean {
  // Deliberate error: Tarea has no cerrar method. Expect TS2339.
  return tarea.cerrar();
}
