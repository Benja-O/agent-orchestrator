using System.Globalization;
using Orchestrator.Domain;

namespace Orchestrator.Cli;

/// <summary>Everything one run of the orchestrator is told from the command line.</summary>
public sealed record OrchestratorOptions
{
    /// <summary>The SDD spec to build from. The only required argument.</summary>
    public required string SpecPath { get; init; }

    /// <summary>Where the generated application goes. Deleted and rebuilt from scratch (ADR-008).</summary>
    public string OutputDirectory { get; init; } = "output";

    /// <summary>Where the run's JSONL log goes, and the language servers' own logs under it.</summary>
    public string LogDirectory { get; init; } = "logs";

    /// <summary>
    /// The per-node attempt ceiling.
    /// </summary>
    /// <remarks>
    /// Exposed on the command line because it is the cost dial, not a tuning detail: each extra
    /// attempt is another paid agent turn per layer (ADR-001, ADR-003). Two while debugging the
    /// wiring, three — the default — for a run meant to finish.
    /// </remarks>
    public int MaximumAttemptsPerNode { get; init; } = GraphPolicy.Default.MaximumAttemptsPerNode;

    /// <summary>Whether the TypeScript language server takes part in the gate.</summary>
    public bool AnalyzeTypeScript { get; init; } = true;

    /// <summary>Dumps every LSP message. The first thing to reach for when a server goes quiet.</summary>
    public bool TraceProtocol { get; init; }
}

/// <summary>
/// The command line, parsed as a pure function.
/// </summary>
/// <remarks>
/// Separate from <c>Program</c> so it can be tested without a <c>Main</c>, an environment or a
/// filesystem. Argument handling is exactly the kind of code that looks too simple to test until
/// a typo silently sends a run to the wrong output directory.
/// </remarks>
public static class CommandLineParser
{
    public const string Usage = """
        Orchestrator — builds an application from an SDD spec with a graph of Claude Code agents,
        gated by real language servers.

          dotnet run --project src/Orchestrator.Cli -- --spec <path> [options]

        Options:
          --spec <path>          The SDD spec to build from. Required.
          --output <directory>   Where the generated application goes. Default: output
                                 Deleted and regenerated from scratch on every run (ADR-008).
          --log-directory <dir>  Where the run's JSONL log goes. Default: logs
          --max-attempts <n>     Attempts per node before the run gives up. Default: 3
                                 Each attempt is a paid agent turn; use 2 while debugging.
          --no-typescript        Leave the TypeScript language server out of the gate.
          --trace-protocol       Dump every LSP message. For a language server that goes quiet.
          --help, -h             This text.

        Exit codes:
          0  The run completed: every layer passed its gate.
          1  The run terminated on a ceiling — iteration limit or non-progress.
          2  The run could not start, or failed terminally.
        """;

    public static bool IsHelpRequest(IReadOnlyList<string> arguments) =>
        arguments.Count == 0
        || arguments.Any(argument =>
            argument is "--help" or "-h" or "-?" or "/?");

    public static Result<OrchestratorOptions> Parse(IReadOnlyList<string> arguments)
    {
        string? specPath = null;
        var outputDirectory = "output";
        var logDirectory = "logs";
        var maximumAttempts = GraphPolicy.Default.MaximumAttemptsPerNode;
        var analyzeTypeScript = true;
        var traceProtocol = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            switch (argument)
            {
                case "--spec":
                    if (!TryTakeValue(arguments, ref index, out var specValue))
                    {
                        return Missing(argument);
                    }

                    specPath = specValue;
                    break;

                case "--output":
                    if (!TryTakeValue(arguments, ref index, out var outputValue))
                    {
                        return Missing(argument);
                    }

                    outputDirectory = outputValue;
                    break;

                case "--log-directory":
                    if (!TryTakeValue(arguments, ref index, out var logValue))
                    {
                        return Missing(argument);
                    }

                    logDirectory = logValue;
                    break;

                case "--max-attempts":
                    if (!TryTakeValue(arguments, ref index, out var attemptsText))
                    {
                        return Missing(argument);
                    }

                    if (!int.TryParse(attemptsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maximumAttempts)
                        || maximumAttempts < 1)
                    {
                        return Result<OrchestratorOptions>.Failure(
                            $"--max-attempts needs a positive whole number; got '{attemptsText}'.");
                    }

                    break;

                case "--no-typescript":
                    analyzeTypeScript = false;
                    break;

                case "--trace-protocol":
                    traceProtocol = true;
                    break;

                default:
                    return Result<OrchestratorOptions>.Failure($"Unknown argument '{argument}'.");
            }
        }

        if (specPath is null)
        {
            return Result<OrchestratorOptions>.Failure("--spec is required: there is nothing to build without one.");
        }

        return Result<OrchestratorOptions>.Success(new OrchestratorOptions
        {
            SpecPath = specPath,
            OutputDirectory = outputDirectory,
            LogDirectory = logDirectory,
            MaximumAttemptsPerNode = maximumAttempts,
            AnalyzeTypeScript = analyzeTypeScript,
            TraceProtocol = traceProtocol,
        });
    }

    /// <summary>
    /// Reads the value after a flag, rejecting another flag in its place.
    /// </summary>
    /// <remarks>
    /// The check on the leading dash is the point: <c>--spec --output out</c> would otherwise
    /// take <c>--output</c> as the spec path and fail much later, with a message about a missing
    /// file rather than about the typo that caused it.
    /// </remarks>
    private static bool TryTakeValue(IReadOnlyList<string> arguments, ref int index, out string value)
    {
        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith('-'))
        {
            value = string.Empty;
            return false;
        }

        index++;
        value = arguments[index];
        return true;
    }

    private static Result<OrchestratorOptions> Missing(string flag) =>
        Result<OrchestratorOptions>.Failure($"{flag} needs a value.");
}
