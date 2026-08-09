/** A task that may depend on other tasks being completed first. */
export class Tarea {
  public estaCompletada = false;

  constructor(
    public readonly identifier: number,
    public readonly titulo: string,
  ) {}

  /** Completes the task, refusing while any prerequisite is still open (RN-01). */
  public completar(prerequisitos: readonly Tarea[]): boolean {
    if (prerequisitos.some((prerequisito) => !prerequisito.estaCompletada)) {
      return false;
    }

    this.estaCompletada = true;
    return true;
  }
}
