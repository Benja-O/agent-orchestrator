namespace Orchestrator.GeneratedAppVerification;

/// <summary>
/// Where the generated application put the three operations CA-06 needs.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These are arguments, not assumptions, and that is a deliberate cost of the spec's
/// design.</strong> <c>specs/gestor-tareas.md</c> says what the system must do and refuses to
/// name endpoints — decomposing it is the pipeline's job, so the routes are the API agent's
/// choice, run to run. The defaults are the Spanish shape the spec's own vocabulary suggests;
/// when a run picks something else, the flags are how this harness is pointed at it.
/// </para>
/// <para>
/// What is <em>not</em> negotiable is the assertion. Whatever the routes turn out to be, the
/// question this harness answers is always the same one: does completing a task with an open
/// prerequisite get rejected, does the rejection say which prerequisite, and is the task still
/// pending afterwards.
/// </para>
/// </remarks>
public sealed record ApiShape
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:5099";

    /// <summary>Collection route: <c>POST</c> creates a task, <c>GET</c> lists them (CA-01, CA-02).</summary>
    public string TasksPath { get; init; } = "/api/tareas";

    /// <summary>Route that declares a dependency (CA-04). <c>{id}</c> is the dependent task.</summary>
    public string DependenciesPath { get; init; } = "/api/tareas/{id}/dependencias";

    /// <summary>Route that completes a task (CA-05, CA-06, CA-07). <c>{id}</c> is the task.</summary>
    public string CompletePath { get; init; } = "/api/tareas/{id}/completar";

    /// <summary>Where the API project lives, so the harness can start it.</summary>
    public string ApiProjectPath { get; init; } = Path.Combine("output", "src", "Api");

    /// <summary>Skips starting the application, for when it is already running.</summary>
    public bool UseRunningApplication { get; init; }

    public string ResolveTasks() => BaseUrl.TrimEnd('/') + TasksPath;

    public string ResolveDependencies(string identifier) =>
        BaseUrl.TrimEnd('/') + DependenciesPath.Replace("{id}", identifier, StringComparison.Ordinal);

    public string ResolveComplete(string identifier) =>
        BaseUrl.TrimEnd('/') + CompletePath.Replace("{id}", identifier, StringComparison.Ordinal);
}

/// <summary>The flags, parsed. Kept apart from the checks so the checks read as prose.</summary>
public static class ApiShapeParser
{
    public const string Usage = """
        Verifies, from outside, that the generated application holds RN-01: a task with an open
        prerequisite cannot be completed. This is the part of block 5's exit criterion the LSP
        gate cannot answer — the gate verifies compilation, not correctness (ROADMAP, R4).

          dotnet run --project src/Orchestrator.GeneratedAppVerification -- [options]

        Options:
          --base-url <url>        Default: http://127.0.0.1:5099
          --tasks <path>          Default: /api/tareas
          --dependencies <path>   Default: /api/tareas/{id}/dependencias
          --complete <path>       Default: /api/tareas/{id}/completar
          --api-project <path>    Default: output/src/Api
          --running               Do not start the application; one is already listening.
          --help, -h              This text.

        The routes are flags because the spec deliberately does not name endpoints, so the API
        agent chooses them. Every request and response is echoed: when a default is wrong, the
        output says exactly what was tried.
        """;

    public static bool IsHelpRequest(IReadOnlyList<string> arguments) =>
        arguments.Any(argument => argument is "--help" or "-h" or "-?" or "/?");

    public static ApiShape Parse(IReadOnlyList<string> arguments)
    {
        var shape = new ApiShape();

        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--base-url":
                    shape = shape with { BaseUrl = Value(arguments, ref index) };
                    break;

                case "--tasks":
                    shape = shape with { TasksPath = Value(arguments, ref index) };
                    break;

                case "--dependencies":
                    shape = shape with { DependenciesPath = Value(arguments, ref index) };
                    break;

                case "--complete":
                    shape = shape with { CompletePath = Value(arguments, ref index) };
                    break;

                case "--api-project":
                    shape = shape with { ApiProjectPath = Value(arguments, ref index) };
                    break;

                case "--running":
                    shape = shape with { UseRunningApplication = true };
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{arguments[index]}'.");
            }
        }

        return shape;
    }

    private static string Value(IReadOnlyList<string> arguments, ref int index)
    {
        if (index + 1 >= arguments.Count)
        {
            throw new ArgumentException($"{arguments[index]} needs a value.");
        }

        index++;
        return arguments[index];
    }
}
