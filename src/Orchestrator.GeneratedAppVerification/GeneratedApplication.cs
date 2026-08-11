using System.Diagnostics;

namespace Orchestrator.GeneratedAppVerification;

/// <summary>
/// Runs the generated application for the length of one verification, and takes it down after.
/// </summary>
/// <remarks>
/// It kills the whole process tree, for the same reason <c>Orchestrator.Lsp.LspServerHost</c>
/// does: <c>dotnet run</c> launches the application as a child, so killing only the parent leaves
/// something listening on the port and holding files under <c>output/</c> — which ADR-008
/// requires the next run to be able to delete.
/// </remarks>
public sealed class GeneratedApplication : IAsyncDisposable
{
    private readonly Process? _process;

    private GeneratedApplication(Process? process) => _process = process;

    /// <summary>Nothing to start: the caller says something is already listening.</summary>
    public static GeneratedApplication AlreadyRunning() => new(null);

    public static async Task<GeneratedApplication> StartAsync(
        ApiShape shape, TimeSpan startupTimeout, CancellationToken cancellationToken)
    {
        var projectPath = Path.GetFullPath(shape.ApiProjectPath);

        if (!Directory.Exists(projectPath))
        {
            throw new InvalidOperationException(
                $"There is no API project at '{projectPath}'. Run the orchestrator first, or pass --api-project.");
        }

        var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = projectPath };

        // Same reason the MCP server sets it: messages that end up in front of a person should
        // not depend on the machine's language.
        startInfo.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(shape.BaseUrl);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The generated application could not be started.");

        var application = new GeneratedApplication(process);

        try
        {
            await application.WaitUntilListeningAsync(shape, startupTimeout, cancellationToken).ConfigureAwait(false);
            return application;
        }
        catch
        {
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Polls the collection route until it answers anything at all.
    /// </summary>
    /// <remarks>
    /// Any HTTP status counts, including a 404. The question here is whether the application is
    /// listening; whether the routes are the ones this harness was told about is a different
    /// question, and answering it here would report a wrong <c>--tasks</c> flag as a startup
    /// timeout.
    /// </remarks>
    private async Task WaitUntilListeningAsync(ApiShape shape, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var startedAt = Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(startedAt) < timeout)
        {
            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"The generated application exited with code {_process.ExitCode} before it started listening. "
                    + "It compiles — the gate said so — but it does not run.");
            }

            try
            {
                using var response = await httpClient.GetAsync(shape.ResolveTasks(), cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Slow first request while the host warms up.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"The generated application never answered {shape.ResolveTasks()} within {timeout.TotalSeconds:F0} s.");
    }

    public ValueTask DisposeAsync()
    {
        if (_process is null)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(TimeSpan.FromSeconds(10));
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}
