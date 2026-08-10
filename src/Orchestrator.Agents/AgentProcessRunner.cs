using System.Diagnostics;
using System.Text;

namespace Orchestrator.Agents;

/// <summary>One subprocess invocation, described without saying how to run it.</summary>
public sealed record AgentProcessRequest
{
    public required string ExecutablePath { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public required string WorkingDirectory { get; init; }

    /// <summary>Written to the process's stdin and then closed.</summary>
    public required string StandardInput { get; init; }
}

/// <summary>What the subprocess did.</summary>
public sealed record AgentProcessResult
{
    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    /// <summary>True when the orchestrator stopped it rather than it finishing.</summary>
    public bool TimedOut { get; init; }
}

/// <summary>
/// The seam between "which command to run" and "running a command".
/// </summary>
/// <remarks>
/// It exists so that <see cref="ClaudeCodeAgentRunner"/> can be tested without launching
/// anything, which is golden rule 3 of AI.md applied one level down: the suite must be able to
/// exercise the runner's own logic — how it reads an answer, what it does with a non-zero exit —
/// without spending a single turn of the Pro plan.
/// </remarks>
public interface IAgentProcessRunner
{
    Task<AgentProcessResult> RunAsync(AgentProcessRequest request, CancellationToken cancellationToken);
}

/// <summary>The real one, over <see cref="Process"/>.</summary>
/// <remarks>
/// One of the three places in the whole solution allowed to start a process (AI.md, golden rule
/// 2), and the only one on the Claude Code side.
/// </remarks>
public sealed class SystemAgentProcessRunner : IAgentProcessRunner
{
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _timeProvider;

    public SystemAgentProcessRunner(TimeSpan timeout, TimeProvider timeProvider)
    {
        _timeout = timeout;
        _timeProvider = timeProvider;
    }

    public async Task<AgentProcessResult> RunAsync(AgentProcessRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(request.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = request.WorkingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = StartOrThrow(startInfo, request.ExecutablePath);

        using var timeout = new CancellationTokenSource(_timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        // Started before writing stdin: a process that answers before reading its whole input
        // would otherwise deadlock against a full pipe buffer, with both sides waiting politely.
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), linked.Token).ConfigureAwait(false);
            process.StandardInput.Close();

            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            KillTree(process);

            return new AgentProcessResult
            {
                ExitCode = -1,
                StandardOutput = await ReadWhateverArrivedAsync(standardOutput).ConfigureAwait(false),
                StandardError = await ReadWhateverArrivedAsync(standardError).ConfigureAwait(false),
                TimedOut = true,
            };
        }
        catch (OperationCanceledException)
        {
            // The run itself was cancelled. Killing the child is the point: AI.md is explicit
            // that a cancelled run must not leave the subprocesses it started behind.
            KillTree(process);
            throw;
        }

        return new AgentProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await standardOutput.ConfigureAwait(false),
            StandardError = await standardError.ConfigureAwait(false),
        };
    }

    private static Process StartOrThrow(ProcessStartInfo startInfo, string executablePath)
    {
        try
        {
            return Process.Start(startInfo)
                ?? throw new AgentRunnerException($"'{executablePath}' could not be started.");
        }
        catch (Exception exception) when (exception is not AgentRunnerException)
        {
            throw new AgentRunnerException(
                $"'{executablePath}' could not be started. Is Claude Code on the PATH?", exception);
        }
    }

    private static async Task<string> ReadWhateverArrivedAsync(Task<string> reader)
    {
        var completed = await Task.WhenAny(reader, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        return completed == reader ? await reader.ConfigureAwait(false) : string.Empty;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
