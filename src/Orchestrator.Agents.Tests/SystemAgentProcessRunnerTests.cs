namespace Orchestrator.Agents.Tests;

/// <summary>
/// The plumbing: standard input in, standard output back, exit code carried, timeout enforced.
/// </summary>
/// <remarks>
/// <para>
/// Exercised against <c>node</c> standing in for <c>claude</c>. That substitution is the point,
/// not a shortcut — golden rule 3 of AI.md is about not spending quota and not depending on a
/// model's mood, and what is worth checking here has nothing to do with either: whether a large
/// prompt survives the pipe, whether a process that never finishes gets killed.
/// </para>
/// <para>
/// If these were tested against the real CLI they would be slow, flaky and billed, and they
/// would still be testing the same four things.
/// </para>
/// </remarks>
public sealed class SystemAgentProcessRunnerTests : IDisposable
{
    private readonly string _scriptDirectory =
        Path.Combine(Path.GetTempPath(), "orchestrator-process-tests", Guid.NewGuid().ToString("n"));

    public SystemAgentProcessRunnerTests() => Directory.CreateDirectory(_scriptDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_scriptDirectory))
        {
            Directory.Delete(_scriptDirectory, recursive: true);
        }
    }

    private string WriteScript(string fileName, string body)
    {
        var path = Path.Combine(_scriptDirectory, fileName);
        File.WriteAllText(path, body);
        return path;
    }

    private AgentProcessRequest RequestRunning(string scriptPath, string standardInput = "") => new()
    {
        ExecutablePath = "node",
        Arguments = [scriptPath],
        WorkingDirectory = _scriptDirectory,
        StandardInput = standardInput,
    };

    /// <summary>
    /// The prompt is the reason stdin is used at all: a layer prompt with a spec and a list of
    /// diagnostics does not fit on a Windows command line, and would be truncated rather than
    /// rejected.
    /// </summary>
    [Fact]
    public async Task A_prompt_far_larger_than_a_command_line_survives_the_pipe()
    {
        var script = WriteScript("echo.js", """
            let input = '';
            process.stdin.on('data', chunk => { input += chunk; });
            process.stdin.on('end', () => { process.stdout.write(String(input.length)); });
            """);

        var prompt = new string('x', 200_000);
        var runner = new SystemAgentProcessRunner(TimeSpan.FromSeconds(30), TimeProvider.System);

        var result = await runner.RunAsync(RequestRunning(script, prompt), CancellationToken.None);

        Assert.Equal("200000", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task The_exit_code_and_both_streams_come_back()
    {
        var script = WriteScript("fail.js", """
            process.stdout.write('out');
            process.stderr.write('err');
            process.exit(7);
            """);

        var runner = new SystemAgentProcessRunner(TimeSpan.FromSeconds(30), TimeProvider.System);

        var result = await runner.RunAsync(RequestRunning(script), CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("out", result.StandardOutput);
        Assert.Equal("err", result.StandardError);
        Assert.False(result.TimedOut);
    }

    /// <summary>
    /// An agent hung inside its own turn never returns control, so the graph's iteration ceiling
    /// would never get its chance. This is the ceiling that does.
    /// </summary>
    [Fact]
    public async Task A_process_that_never_finishes_is_killed_and_reported_as_a_timeout()
    {
        var script = WriteScript("hang.js", "setTimeout(() => {}, 60_000);");

        var runner = new SystemAgentProcessRunner(TimeSpan.FromMilliseconds(300), TimeProvider.System);

        var result = await runner.RunAsync(RequestRunning(script), CancellationToken.None);

        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task A_missing_executable_says_so_instead_of_failing_obscurely()
    {
        var runner = new SystemAgentProcessRunner(TimeSpan.FromSeconds(5), TimeProvider.System);

        var request = new AgentProcessRequest
        {
            ExecutablePath = "claude-that-is-not-installed",
            Arguments = [],
            WorkingDirectory = _scriptDirectory,
            StandardInput = string.Empty,
        };

        await Assert.ThrowsAsync<AgentRunnerException>(() => runner.RunAsync(request, CancellationToken.None));
    }
}
