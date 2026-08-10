using System.Text.Json;
using System.Text.Json.Serialization;
using Orchestrator.Domain;

namespace Orchestrator.Agents;

/// <summary>The result object of <c>claude -p --output-format json</c>.</summary>
internal sealed record ClaudeCodeResult
{
    [JsonPropertyName("subtype")]
    public string? Subtype { get; init; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; init; }

    [JsonPropertyName("num_turns")]
    public int NumberOfTurns { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("total_cost_usd")]
    public double? TotalCostUsd { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }
}

/// <summary>
/// Turns what the CLI printed into the <see cref="AgentOutcome"/> the graph reasons about.
/// </summary>
/// <remarks>
/// <para>
/// Note how little is read: how the turn ended, and the text for the log. Not whether the code
/// is right — the graph never branches on what an agent said about its own work, because that is
/// the claim this whole project exists to stop trusting (ADR-004, ADR-014). The transcript is
/// carried so a person reading a failed run can see what happened, and for no other purpose.
/// </para>
/// </remarks>
internal static class ClaudeCodeOutcomeTranslation
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static AgentOutcome FromProcessResult(AgentProcessResult processResult, string agentName)
    {
        if (processResult.TimedOut)
        {
            return AgentOutcome.Failed(
                AgentCompletion.TimedOut,
                $"The '{agentName}' agent was still running when the orchestrator's timeout expired and was stopped.",
                processResult.StandardOutput);
        }

        ClaudeCodeResult? result = null;

        try
        {
            result = JsonSerializer.Deserialize<ClaudeCodeResult>(processResult.StandardOutput, SerializerOptions);
        }
        catch (JsonException)
        {
            // Left null on purpose: an unreadable answer and a non-zero exit are the same
            // situation from here, and the branch below reports both with what was printed.
        }

        if (result is null)
        {
            return AgentOutcome.Failed(
                AgentCompletion.Errored,
                $"The '{agentName}' agent exited with code {processResult.ExitCode} and printed no readable result. "
                + $"stderr: {Excerpt(processResult.StandardError)}",
                processResult.StandardOutput);
        }

        var transcript = result.Result ?? string.Empty;

        // Claude Code's own ceiling, applied inside the turn (ADR-011). It is a distinct outcome
        // from a failure because the work so far is on disk and the gate is about to judge it:
        // the graph may well have something to review.
        if (IsTurnLimit(result.Subtype))
        {
            return AgentOutcome.Failed(
                AgentCompletion.TurnLimitReached,
                $"The '{agentName}' agent hit its maxTurns after {result.NumberOfTurns} turn(s).",
                transcript);
        }

        if (result.IsError || processResult.ExitCode != 0)
        {
            return AgentOutcome.Failed(
                AgentCompletion.Errored,
                $"The '{agentName}' agent reported '{result.Subtype ?? "an error"}' (exit code {processResult.ExitCode}).",
                transcript);
        }

        return AgentOutcome.Completed(transcript);
    }

    private static bool IsTurnLimit(string? subtype) =>
        subtype is not null && subtype.Contains("max_turns", StringComparison.OrdinalIgnoreCase);

    private static string Excerpt(string text) =>
        text.Length <= 400 ? text : string.Concat(text.AsSpan(0, 400), "…");
}
