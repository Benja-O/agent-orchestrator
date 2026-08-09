namespace BrokenCSharp.Domain;

/// <summary>A task that may depend on other tasks being completed first.</summary>
public sealed class Tarea
{
    public Tarea(int identifier, string titulo)
    {
        Identifier = identifier;
        Titulo = titulo;
    }

    public int Identifier { get; }

    public string Titulo { get; }

    public bool EstaCompletada { get; private set; }

    /// <summary>Completes the task, refusing while any prerequisite is still open (RN-01).</summary>
    public bool Completar(IReadOnlyList<Tarea> prerequisitos)
    {
        if (prerequisitos.Any(prerequisito => !prerequisito.EstaCompletada))
        {
            return false;
        }

        EstaCompletada = true;
        return true;
    }
}
