using System.Diagnostics;
using System.Text;

namespace Orchestrator.Runtime;

/// <summary>How the generated application ended up, from the outside.</summary>
public sealed record StartupOutcome
{
    public required bool IsListening { get; init; }

    /// <summary>Set when the process exited instead of serving.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Everything the process printed, which is where a startup exception lives.</summary>
    public required string Output { get; init; }
}

/// <summary>
/// Runs the generated application for the length of one verification.
/// </summary>
/// <remarks>
/// <para>
/// The third place in this repository allowed to start a process, after the Claude Code runner
/// and the language servers (AI.md, golden rule 2). It exists for the same reason the other two
/// do: something outside the graph has to talk to the outside world, and the graph must not know
/// that it did.
/// </para>
/// <para>
/// <strong>Shutdown kills the tree, and here that matters twice over.</strong> <c>dotnet run</c>
/// launches the application as a child, so killing only the parent leaves something holding the
/// port and holding files under the workspace — which ADR-008 requires the next run to be able to
/// delete. And an orphan that keeps the port would make the <em>next</em> verification pass by
/// talking to the previous run's application, which is a false green with a very long fuse.
/// </para>
/// </remarks>
internal sealed class GeneratedApplicationProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();

    private GeneratedApplicationProcess(Process process) => _process = process;

    public static GeneratedApplicationProcess Start(string projectDirectory, string baseUrl)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Development, on purpose: it is what makes an unhandled exception come back in the
        // response body instead of as a bare 500. The whole value of this check is being able to
        // hand the agent the actual reason, and "Internal Server Error" is not one.
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectDirectory);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(baseUrl);

        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new RuntimeVerificationException("The generated application could not be started.");
        }
        catch (Exception exception) when (exception is not RuntimeVerificationException)
        {
            throw new RuntimeVerificationException("'dotnet run' could not be started.", exception);
        }

        var application = new GeneratedApplicationProcess(process);
        application.CaptureOutput();

        return application;
    }

    /// <summary>Waits until something answers, the process dies, or the budget runs out.</summary>
    public async Task<StartupOutcome> WaitUntilListeningAsync(
        string probeUrl, TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var startedAt = timeProvider.GetTimestamp();

        while (timeProvider.GetElapsedTime(startedAt) < timeout)
        {
            // Checked before the request, not after: an application that died on startup would
            // otherwise keep us polling a dead port until the timeout, and then report a timeout
            // instead of the exception it printed on the way out.
            if (_process.HasExited)
            {
                return new StartupOutcome { IsListening = false, ExitCode = _process.ExitCode, Output = ReadOutput() };
            }

            try
            {
                using var response = await httpClient.GetAsync(probeUrl, cancellationToken).ConfigureAwait(false);
                return new StartupOutcome { IsListening = true, Output = ReadOutput() };
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Slow first request while the host warms up.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return new StartupOutcome { IsListening = false, Output = ReadOutput() };
    }

    public string ReadOutput()
    {
        lock (_output)
        {
            return _output.ToString();
        }
    }

    private void CaptureOutput()
    {
        _process.OutputDataReceived += Append;
        _process.ErrorDataReceived += Append;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        void Append(object _, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (_output)
            {
                _output.AppendLine(eventArgs.Data);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(TimeSpan.FromSeconds(15));
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
