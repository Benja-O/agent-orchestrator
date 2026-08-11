namespace Orchestrator.Domain;

/// <summary>
/// Runs the generated application and reports what went wrong as diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This exists because block 5 produced an application that compiled and did not
/// work.</strong> Every gate was clean, <c>dotnet build</c> reported zero errors, <c>tsc</c>
/// reported zero errors — and the first request returned 500, because the API agent had written
/// <c>modelBuilder.Property("_dependencias")</c> over a <c>HashSet&lt;TareaId&gt;</c> with a
/// comment asserting that EF Core's InMemory provider handles value collections directly. It does
/// not. The code was valid; the belief about the library was wrong, and no language server can
/// see the difference.
/// </para>
/// <para>
/// That is R4 of the ROADMAP arriving in person: <em>the gate verifies compilation, not
/// correctness</em>. This interface is the narrowest thing that closes the gap — not a test suite
/// over the generated app (that is still debt D4), just the question a compiler cannot answer:
/// <em>does it run?</em>
/// </para>
/// <para>
/// It returns <see cref="DiagnosticSet"/> rather than a verdict of its own, and that is the whole
/// design. A runtime failure enters the graph as the same kind of fact a compile error is, so
/// <see cref="LayerMap"/> attributes it, <see cref="ReviewPolicy"/> applies the attempt ceiling
/// and the non-progress fingerprint to it, and the agent prompt carries it — with no new edge, no
/// new termination path, and nothing in the state machine that exists only for this.
/// </para>
/// </remarks>
public interface IApplicationVerifier
{
    /// <summary>
    /// Starts the generated application, exercises it, and shuts it down.
    /// </summary>
    /// <remarks>
    /// Failures of the generated application are diagnostics. Failures of the orchestrator's own
    /// machinery — a workspace that is not there, a project directory that does not exist — are
    /// exceptions, per AI.md's split between expected states and exceptional ones.
    /// </remarks>
    Task<ApplicationVerification> VerifyAsync(CancellationToken cancellationToken);
}

/// <summary>What running the generated application showed.</summary>
/// <remarks>
/// <para>
/// <see cref="RoutesExercised"/> travels with the diagnostics rather than beside them because it
/// is the part a reader has to see to trust the rest. <em>No failures</em> means one thing when
/// eleven endpoints answered and something else entirely when none were found, and a verdict that
/// could not tell those apart would be the false green arriving through a door this very check
/// opened.
/// </para>
/// </remarks>
public sealed record ApplicationVerification
{
    public required DiagnosticSet Diagnostics { get; init; }

    /// <summary>How many endpoints were actually called.</summary>
    public required int RoutesExercised { get; init; }

    public static ApplicationVerification Working(int routesExercised) => new()
    {
        Diagnostics = DiagnosticSet.Empty,
        RoutesExercised = routesExercised,
    };

    public static ApplicationVerification Broken(int routesExercised, params Diagnostic[] failures) => new()
    {
        Diagnostics = new DiagnosticSet { Items = failures, Total = failures.Length, Truncated = false },
        RoutesExercised = routesExercised,
    };
}

/// <summary>
/// Builds the diagnostics a runtime failure produces.
/// </summary>
/// <remarks>
/// In the domain, next to <see cref="LayerMap"/>, because the one thing these have to get right is
/// domain knowledge: the file path has to resolve to a layer. A diagnostic that resolves to none
/// stops the run (see <see cref="LayerMap.Attribute"/>), so a runtime failure attributed to
/// nowhere would turn "your app does not start" into "the pipeline cannot continue", which is
/// true but useless — nobody would be asked to fix it.
/// </remarks>
public static class RuntimeDiagnostics
{
    /// <summary>The code every runtime failure carries, so a person reading the log can tell them apart at a glance.</summary>
    public const string Code = "RUNTIME";

    /// <summary>
    /// The file a runtime failure is reported against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The API layer's entry point, always, and the reason is not that the API is always at fault.
    /// It is that the API agent is the only one who can act: the layer templates make the domain
    /// read-only for it and forbid persistence configuration from living there, so an EF mapping
    /// failure over a domain type is fixed in the <c>DbContext</c> — which is the API's file.
    /// </para>
    /// <para>
    /// Attributing by parsing the stack trace was considered and rejected: it would send work to
    /// the domain agent that the domain agent is not allowed to do.
    /// </para>
    /// </remarks>
    public static string FilePathFor(LayerMap layerMap) => $"{layerMap.ScopeOf(Layer.Api)}/Program.cs";

    public static Diagnostic Failure(LayerMap layerMap, string message) => new()
    {
        FilePath = FilePathFor(layerMap),
        Range = new SourceRange(1, 1, 1, 1),
        Severity = DiagnosticSeverity.Error,
        Code = Code,
        Message = message,
        Source = "runtime",
    };
}
