using System.Diagnostics;
using System.Text.Json;
using Orchestrator.Domain;

namespace Orchestrator.Agents.Tests;

/// <summary>
/// The barrier of ADR-011, exercised as the thing it actually is: a script Claude Code runs.
/// </summary>
/// <remarks>
/// <para>
/// This suite launches <c>node</c>, which is not what golden rule 3 forbids — that rule is about
/// <c>claude -p</c> and about real language servers, both of which cost quota or seconds. Node
/// costs neither, and the alternative is worse than the cost: the only interesting property of
/// this hook is its exit code, so testing anything other than the real script running would be
/// testing a paraphrase of it.
/// </para>
/// <para>
/// Debt D5 is paid here. Until this existed, "each agent only writes in its own layer" was a
/// sentence in a prompt.
/// </para>
/// </remarks>
public sealed class FileScopeHookTests
{
    private static string HookScript => Path.Combine(AppContext.BaseDirectory, "Templates", "hooks", "restrict-to-layer.js");

    private const string WorkspaceRoot = "F:/run/output";

    /// <summary>0 lets the write through; 2 is what Claude Code reads as "block this call".</summary>
    private static int ExitCodeFor(string? filePath, string allowedFolder = "src/Domain")
    {
        var payload = JsonSerializer.Serialize(new
        {
            cwd = WorkspaceRoot,
            tool_name = "Write",
            tool_input = filePath is null ? new { } : (object)new { file_path = filePath },
        });

        return RunHook(payload, allowedFolder);
    }

    private static int RunHook(string standardInput, string allowedFolder)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add(HookScript);
        startInfo.ArgumentList.Add(allowedFolder);

        using var process = Process.Start(startInfo)!;
        process.StandardInput.Write(standardInput);
        process.StandardInput.Close();
        process.WaitForExit(TimeSpan.FromSeconds(30));

        return process.ExitCode;
    }

    [Theory]
    [InlineData("src/Domain/Tarea.cs")]
    [InlineData("src/Domain/ValueObjects/Estado.cs")]
    public void A_write_inside_the_agents_own_layer_goes_through(string filePath)
    {
        Assert.Equal(0, ExitCodeFor(filePath));
    }

    [Theory]
    [InlineData("src/Api/TareasController.cs")]
    [InlineData("src/Frontend/App.tsx")]
    [InlineData("Program.cs")]
    public void A_write_in_another_layer_is_rejected(string filePath)
    {
        Assert.Equal(2, ExitCodeFor(filePath));
    }

    /// <summary>
    /// The ways out of the folder that do not look like leaving it. The agent really did emit an
    /// absolute path during the block 4 probe, so the first of these is not hypothetical.
    /// </summary>
    [Theory]
    [InlineData("/src/Api/Nota.cs")]
    [InlineData("C:/Windows/System32/evil.cs")]
    [InlineData("src/Domain/../Api/Nota.cs")]
    [InlineData("src/Domain")]
    public void An_escape_out_of_the_layer_is_rejected_however_it_is_spelled(string filePath)
    {
        Assert.Equal(2, ExitCodeFor(filePath));
    }

    /// <summary>A call that names no file is not this hook's business.</summary>
    [Fact]
    public void A_tool_call_without_a_file_path_is_left_alone()
    {
        Assert.Equal(0, ExitCodeFor(filePath: null));
    }

    /// <summary>
    /// Unreadable input is a rejection, not a pass. Everything about this script has to fail on
    /// the safe side, because the failure it is guarding against is invisible.
    /// </summary>
    [Fact]
    public void Input_it_cannot_read_is_treated_as_a_violation()
    {
        Assert.Equal(2, RunHook("this is not json", "src/Domain"));
    }

    [Fact]
    public void Without_a_folder_to_enforce_it_refuses_rather_than_allowing_everything()
    {
        var startInfo = new ProcessStartInfo("node") { UseShellExecute = false, RedirectStandardInput = true };
        startInfo.ArgumentList.Add(HookScript);

        using var process = Process.Start(startInfo)!;
        process.StandardInput.Close();
        process.WaitForExit(TimeSpan.FromSeconds(30));

        Assert.Equal(2, process.ExitCode);
    }

    /// <summary>
    /// Each layer's folder comes from the same map the gate uses to attribute a diagnostic, so
    /// the barrier and the blame cannot drift apart.
    /// </summary>
    [Theory]
    [InlineData(Layer.Domain, "src/Api/Other.cs")]
    [InlineData(Layer.Api, "src/Domain/Other.cs")]
    [InlineData(Layer.Frontend, "src/Api/Other.cs")]
    public void The_enforced_folder_is_the_one_the_layer_map_owns(Layer layer, string foreignFile)
    {
        var ownFolder = LayerMap.Default.ScopeOf(layer);

        Assert.Equal(0, ExitCodeFor($"{ownFolder}/Mine.cs", ownFolder));
        Assert.Equal(2, ExitCodeFor(foreignFile, ownFolder));
    }
}
