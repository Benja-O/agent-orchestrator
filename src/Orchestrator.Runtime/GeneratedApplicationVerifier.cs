using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Orchestrator.Domain;

namespace Orchestrator.Runtime;

/// <summary>Where the generated application lives and how long it gets.</summary>
public sealed record ApplicationVerifierSettings
{
    public required string WorkspaceRoot { get; init; }

    /// <summary>The API project, relative to the workspace root.</summary>
    public string ApiProjectRelativePath { get; init; } = "src/Api";

    /// <summary>Where .NET's OpenAPI middleware publishes the document by default.</summary>
    public string OpenApiDocumentPath { get; init; } = "/openapi/v1.json";

    /// <summary>Long enough for a cold <c>dotnet run</c>, which builds before it serves.</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(3);
}

/// <summary>
/// Starts the generated application, calls what it says it exposes, and reports what broke.
/// </summary>
/// <remarks>
/// <para>
/// The answer to the one question the LSP gate cannot ask. Block 5's first full run passed every
/// compile gate — clean diagnostics on three layers, zero errors from <c>dotnet build</c>, zero
/// from <c>tsc</c> — and served a 500 on its first request, because the API agent had asserted in
/// a comment that EF Core's InMemory provider maps a <c>HashSet</c> of value objects. The code was
/// valid. The belief was not. That is the gap this class closes, and it is why the check runs the
/// application instead of inspecting it.
/// </para>
/// <para>
/// <strong>Not finding anything to test is a failure.</strong> An application with no discoverable
/// endpoints comes back as a diagnostic, never as a clean verdict — the same rule the contract
/// applies to <c>indexing</c> and for the same reason: a verifier that approves because it had
/// nothing to try is indistinguishable from one that approved something that works, and the
/// difference is the entire point of the project.
/// </para>
/// </remarks>
public sealed class GeneratedApplicationVerifier : IApplicationVerifier
{
    /// <summary>How much of a failure body reaches the agent's prompt.</summary>
    /// <remarks>
    /// A developer exception page is enormous and its first lines are the message and the top of
    /// the stack, which is what a fix needs. The rest is budget spent on framework frames.
    /// </remarks>
    private const int MaximumFailureCharacters = 1200;

    private readonly ApplicationVerifierSettings _settings;
    private readonly LayerMap _layerMap;
    private readonly TimeProvider _timeProvider;

    public GeneratedApplicationVerifier(
        ApplicationVerifierSettings settings,
        TimeProvider timeProvider,
        LayerMap? layerMap = null)
    {
        _settings = settings;
        _timeProvider = timeProvider;
        _layerMap = layerMap ?? LayerMap.Default;
    }

    public async Task<ApplicationVerification> VerifyAsync(CancellationToken cancellationToken)
    {
        var projectDirectory = Path.GetFullPath(
            Path.Combine(_settings.WorkspaceRoot, _settings.ApiProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!Directory.Exists(projectDirectory))
        {
            throw new RuntimeVerificationException(
                $"There is no API project at '{projectDirectory}', so there is nothing to run.");
        }

        var baseUrl = $"http://127.0.0.1:{FindFreePort()}";

        await using var application = GeneratedApplicationProcess.Start(projectDirectory, baseUrl);

        var startup = await application
            .WaitUntilListeningAsync(baseUrl + _settings.OpenApiDocumentPath, _settings.StartupTimeout, _timeProvider, cancellationToken)
            .ConfigureAwait(false);

        if (!startup.IsListening)
        {
            return ApplicationVerification.Broken(0, Failure(DescribeStartupFailure(startup)));
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        var routes = await DiscoverRoutesAsync(httpClient, baseUrl, cancellationToken).ConfigureAwait(false);

        if (routes.Count == 0)
        {
            return ApplicationVerification.Broken(0, Failure(
                $"La aplicación arranca pero no publica un documento OpenAPI utilizable en "
                + $"'{_settings.OpenApiDocumentPath}', así que nada puede comprobar que funciona. "
                + $"Agregá `builder.Services.AddOpenApi()` y `app.MapOpenApi()` en `Program.cs`. "
                + $"Arrancar no es evidencia de nada: esta verificación existe porque una app que "
                + $"compila puede fallar en la primera request."));
        }

        var failures = new List<Diagnostic>();
        var exercised = 0;

        foreach (var route in routes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var failure = await ExerciseAsync(httpClient, baseUrl, route, cancellationToken).ConfigureAwait(false);
            exercised++;

            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        return failures.Count == 0
            ? ApplicationVerification.Working(exercised)
            : ApplicationVerification.Broken(exercised, [.. failures]);
    }

    /// <summary>
    /// Calls one endpoint and decides whether the answer counts as working.
    /// </summary>
    /// <remarks>
    /// A 4xx is a pass. The application is alive and answering, and a client error is a
    /// legitimate response to a request this verifier made up — treating it as a failure would
    /// send the agent chasing a rejection that was correct.
    /// </remarks>
    private async Task<Diagnostic?> ExerciseAsync(
        HttpClient httpClient, string baseUrl, string route, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(baseUrl + route, cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode < 500)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return Failure(string.Create(
                CultureInfo.InvariantCulture,
                $"`GET {route}` devolvió {(int)response.StatusCode} {response.StatusCode}. La app compila pero falla al "
                + $"atender la request: {Shorten(body)}"));
        }
        catch (HttpRequestException exception)
        {
            return Failure($"`GET {route}` no obtuvo respuesta: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure($"`GET {route}` no contestó dentro del tiempo permitido.");
        }
    }

    private async Task<IReadOnlyList<string>> DiscoverRoutesAsync(
        HttpClient httpClient, string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient
                .GetAsync(baseUrl + _settings.OpenApiDocumentPath, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var document = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // The OpenAPI route itself is excluded: it answered already, and calling it again
            // would let an application with no endpoints of its own look verified.
            return OpenApiRoutes.ParameterlessGets(document)
                .Where(route => !route.Equals(_settings.OpenApiDocumentPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    private static string DescribeStartupFailure(StartupOutcome startup) => startup.ExitCode is { } exitCode
        ? $"La aplicación no arranca: el proceso terminó con código {exitCode} antes de atender ninguna request. "
          + $"Salida: {Shorten(startup.Output)}"
        : $"La aplicación no llegó a atender ninguna request dentro del tiempo permitido. "
          + $"Salida: {Shorten(startup.Output)}";

    private Diagnostic Failure(string message) => RuntimeDiagnostics.Failure(_layerMap, message);

    /// <summary>
    /// Keeps the head of a message, which is where the reason is.
    /// </summary>
    /// <remarks>
    /// The opposite end from <c>GeneratedWorkspaceRestorer</c>, deliberately: a restore log ends
    /// with its error, and an exception page starts with it.
    /// </remarks>
    private static string Shorten(string text)
    {
        var trimmed = text.ReplaceLineEndings(" ").Trim();

        return trimmed.Length <= MaximumFailureCharacters ? trimmed : trimmed[..MaximumFailureCharacters] + "…";
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
