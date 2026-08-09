using BrokenCSharp.Domain;

namespace BrokenCSharp.Api;

/// <summary>
/// The API side of the fixture. It exists to give the verification two things at once:
/// a call that resolves across projects, and a call that does not compile.
/// </summary>
public sealed class TareasController
{
    private readonly Dictionary<int, Tarea> _tareas = new();

    /// <summary>The healthy call. 'definition' on Completar here must land in Domain/Tarea.cs.</summary>
    public bool Completar(int identifier, IReadOnlyList<Tarea> prerequisitos)
    {
        var tarea = _tareas[identifier];
        return tarea.Completar(prerequisitos);
    }

    /// <summary>
    /// The broken call, on purpose: Tarea has no Cerrar method, so this is a CS1061 that a
    /// language server reports and that no amount of agent self-reporting would catch.
    /// </summary>
    public bool Cerrar(int identifier)
    {
        var tarea = _tareas[identifier];
        return tarea.Cerrar();
    }
}
