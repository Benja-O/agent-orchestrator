namespace Orchestrator.Agents;

/// <summary>
/// Everything the runner needs to invoke one agent in the generated workspace.
/// </summary>
/// <remarks>
/// <para>
/// Several of these fields exist because of what block 4 found when it first ran a real headless
/// agent, and each one closes a way for the pipeline to degrade into blind generation without
/// anything failing. They are documented one by one because none of them is obvious from its
/// name, and a future reader deleting one would not see the damage in any test.
/// </para>
/// <para>
/// <strong>There is no API key anywhere in this type and there must never be.</strong> Billing
/// runs against the subscription through the CLI (ADR-001).
/// </para>
/// </remarks>
public sealed record ClaudeCodeSettings
{
    /// <summary>The CLI. Verified to answer before the run starts, never assumed.</summary>
    public string ExecutablePath { get; init; } = "claude";

    /// <summary>The generated application's root: the working directory of every invocation.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>
    /// The MCP endpoint of the LSP server, passed on the command line rather than left to the
    /// workspace's <c>.mcp.json</c> alone.
    /// </summary>
    /// <remarks>
    /// This is the fix for risk R5, and the reason it is a fix rather than a mitigation: a server
    /// declared only in a project-scope <c>.mcp.json</c> waits for an interactive approval that
    /// nobody can give in <c>claude -p</c>, and the failure is silent — the agent runs with no
    /// LSP tools at all. Passing the server explicitly sidesteps the approval instead of hoping
    /// it was granted. The port is chosen at run time, so it could not have been written into a
    /// versioned file anyway.
    /// </remarks>
    public required string McpEndpoint { get; init; }

    /// <summary>Where the layer's <c>PreToolUse</c> hook script lives, relative to the workspace root.</summary>
    public string HookScriptRelativePath { get; init; } = ".claude/hooks/restrict-to-layer.js";

    /// <summary>
    /// How long the orchestrator lets one invocation run before killing the process.
    /// </summary>
    /// <remarks>
    /// The graph applies its own timeout around the call (<c>GraphPolicy.AgentTimeout</c>); this
    /// is what actually stops the operating system process when that fires, because cancelling a
    /// task the process does not know about would leave it running and still spending quota.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// The tools an invocation is allowed to run without asking anyone.
/// </summary>
/// <remarks>
/// <para>
/// A second thing block 4 had to discover by running it: making a tool <em>available</em> and
/// making it <em>runnable</em> are different switches. The subagent's frontmatter decides what
/// exists for that agent; this decides what it may execute without a permission prompt. In
/// headless there is nobody to prompt, so a tool that is available but not allowed is a tool the
/// agent calls once and then reports that it needs permission — which reads, in the transcript,
/// almost exactly like the tool not being there.
/// </para>
/// </remarks>
public static class AgentToolPermissions
{
    /// <summary>The five tools of the MCP contract, spelled the way Claude Code names them.</summary>
    public static readonly IReadOnlyList<string> LspTools =
    [
        "mcp__lsp__diagnostics",
        "mcp__lsp__definition",
        "mcp__lsp__references",
        "mcp__lsp__documentSymbol",
        "mcp__lsp__workspaceSymbol",
    ];

    /// <summary>What a layer agent needs: read and write its own files, plus the whole LSP layer.</summary>
    public static readonly IReadOnlyList<string> ForLayerAgent =
        ["Read", "Write", "Edit", "Glob", "Grep", .. LspTools];

    /// <summary>
    /// What the spec analyzer needs, which is deliberately less.
    /// </summary>
    /// <remarks>
    /// No write tools, by the same reasoning ADR-011 gives for its frontmatter: its output is a
    /// plan, and an analyzer that can write code dissolves the line between planning and doing
    /// on its first turn.
    /// </remarks>
    public static readonly IReadOnlyList<string> ForSpecAnalyzer = ["Read", "Glob", "Grep"];
}
