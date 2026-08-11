namespace Orchestrator.Agents.Tests;

/// <summary>
/// Resolving a command name to a full path, which turned out to matter for exactly one kind of
/// executable and to matter a lot.
/// </summary>
public sealed class ExecutableLocatorTests
{
    [Fact]
    public void A_path_that_is_already_absolute_comes_back_unchanged()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "somewhere", "tool.exe");

        Assert.Equal(absolute, ExecutableLocator.Resolve(absolute));
    }

    [Fact]
    public void A_command_on_the_path_resolves_to_a_file_that_exists()
    {
        var resolved = ExecutableLocator.Resolve(OperatingSystem.IsWindows() ? "node.exe" : "node");

        Assert.True(Path.IsPathRooted(resolved), $"'node' did not resolve to a full path: {resolved}");
        Assert.True(File.Exists(resolved));
    }

    /// <summary>
    /// An unresolvable command comes back as it was given.
    /// </summary>
    /// <remarks>
    /// Deliberate, not an oversight: the caller launches it anyway and the failure to start names
    /// the command a person recognises. Throwing here would replace that with a worse report of
    /// the same fact, one layer earlier.
    /// </remarks>
    [Fact]
    public void A_command_that_is_nowhere_comes_back_as_it_was_given()
    {
        Assert.Equal("no-such-tool-8f3a", ExecutableLocator.Resolve("no-such-tool-8f3a"));
    }
}
