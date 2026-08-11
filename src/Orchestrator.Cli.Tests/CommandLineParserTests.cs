using Orchestrator.Cli;
using Orchestrator.Domain;

namespace Orchestrator.Cli.Tests;

/// <summary>
/// The command line, which is the whole reason the parser is a pure function.
/// </summary>
/// <remarks>
/// Argument handling looks too simple to be worth testing right up until a run silently writes to
/// the wrong directory, or spends three agent turns per node because a flag was misread. Both
/// mistakes cost quota, and neither shows up as an error.
/// </remarks>
public sealed class CommandLineParserTests
{
    [Fact]
    public void The_spec_is_the_only_thing_a_run_cannot_do_without()
    {
        var result = CommandLineParser.Parse(["--spec", "specs/gestor-tareas.md"]);

        Assert.True(result.IsSuccess);
        Assert.Equal("specs/gestor-tareas.md", result.Value.SpecPath);
        Assert.Equal("output", result.Value.OutputDirectory);
        Assert.Equal("logs", result.Value.LogDirectory);
        Assert.Equal(GraphPolicy.Default.MaximumAttemptsPerNode, result.Value.MaximumAttemptsPerNode);
        Assert.True(result.Value.AnalyzeTypeScript);
        Assert.False(result.Value.TraceProtocol);
    }

    [Fact]
    public void Without_a_spec_there_is_nothing_to_build()
    {
        var result = CommandLineParser.Parse(["--output", "somewhere"]);

        Assert.True(result.IsFailure);
        Assert.Contains("--spec", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_flag_reaches_the_options()
    {
        var result = CommandLineParser.Parse(
        [
            "--spec", "specs/gestor-tareas.md",
            "--output", "build/app",
            "--log-directory", "build/logs",
            "--max-attempts", "2",
            "--no-typescript",
            "--trace-protocol",
        ]);

        Assert.True(result.IsSuccess);

        var options = result.Value;
        Assert.Equal("build/app", options.OutputDirectory);
        Assert.Equal("build/logs", options.LogDirectory);
        Assert.Equal(2, options.MaximumAttemptsPerNode);
        Assert.False(options.AnalyzeTypeScript);
        Assert.True(options.TraceProtocol);
    }

    /// <summary>
    /// The ceiling of ADR-003 is a cost control, so a value that would remove it is refused at the
    /// command line rather than at <c>GraphPolicy.Validate</c>, where the message would be about
    /// an invariant instead of about the flag the person typed.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public void An_attempt_ceiling_that_is_not_a_positive_number_is_refused(string value)
    {
        var result = CommandLineParser.Parse(["--spec", "s.md", "--max-attempts", value]);

        Assert.True(result.IsFailure);
        Assert.Contains("--max-attempts", result.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A flag swallowing the next flag as its value is the mistake this exists to catch: it fails
    /// much later, as a missing file, and the message points nowhere near the typo.
    /// </summary>
    [Fact]
    public void A_flag_missing_its_value_does_not_eat_the_next_flag()
    {
        var result = CommandLineParser.Parse(["--spec", "--output", "out"]);

        Assert.True(result.IsFailure);
        Assert.Contains("--spec", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_argument_stops_the_run_instead_of_being_ignored()
    {
        var result = CommandLineParser.Parse(["--spec", "s.md", "--fast"]);

        Assert.True(result.IsFailure);
        Assert.Contains("--fast", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Asking_for_help_and_asking_for_nothing_are_both_help()
    {
        Assert.True(CommandLineParser.IsHelpRequest([]));
        Assert.True(CommandLineParser.IsHelpRequest(["--help"]));
        Assert.True(CommandLineParser.IsHelpRequest(["-h"]));
        Assert.False(CommandLineParser.IsHelpRequest(["--spec", "s.md"]));
    }

    /// <summary>
    /// The usage text is the only documentation someone reading <c>--help</c> gets, so a flag that
    /// exists and is not mentioned there is a flag nobody will use.
    /// </summary>
    [Theory]
    [InlineData("--spec")]
    [InlineData("--output")]
    [InlineData("--log-directory")]
    [InlineData("--max-attempts")]
    [InlineData("--no-typescript")]
    [InlineData("--trace-protocol")]
    public void Every_flag_the_parser_accepts_is_documented_in_the_usage(string flag)
    {
        Assert.Contains(flag, CommandLineParser.Usage, StringComparison.Ordinal);
    }
}
