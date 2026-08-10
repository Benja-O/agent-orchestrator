using Orchestrator.Domain;

namespace Orchestrator.Lsp.Tests;

/// <summary>
/// The gateway over a scripted invoker: no process, no socket, no language server.
/// </summary>
/// <remarks>
/// Golden rule 3 of AI.md holds here too. What is worth asserting about this class is small on
/// purpose — it forwards a scope and translates an answer — and the interesting behaviour is the
/// behaviour it deliberately lacks: it does not wait, retry or interpret.
/// </remarks>
public sealed class McpLanguageServerGatewayTests
{
    [Fact]
    public async Task The_scope_reaches_the_diagnostics_tool_unchanged()
    {
        var invoker = new RecordingInvoker("""{ "status": "ready", "total": 0, "truncated": false, "items": [] }""");
        var gateway = new McpLanguageServerGateway(invoker);

        await gateway.GetDiagnosticsAsync(".", CancellationToken.None);

        Assert.Equal("diagnostics", invoker.ToolName);
        Assert.Equal(".", invoker.Arguments["scope"]);
    }

    [Fact]
    public async Task An_answer_becomes_a_verdict_the_graph_can_read()
    {
        const string Json = """
            { "status": "ready", "total": 1, "truncated": false,
              "items": [ { "filePath": "src/Domain/Tarea.cs",
                           "range": { "startLine": 12, "startColumn": 5, "endLine": 12, "endColumn": 20 },
                           "severity": "error", "code": "CS0103",
                           "message": "The name 'prerequisitos' does not exist", "source": "roslyn" } ] }
            """;

        var gateway = new McpLanguageServerGateway(new RecordingInvoker(Json));

        var verdict = await gateway.GetDiagnosticsAsync(".", CancellationToken.None);

        Assert.Equal(GateStatus.Ready, verdict.Status);
        Assert.True(verdict.Diagnostics.HasBlockingItems);
    }

    /// <summary>
    /// A server that is down has to be distinguishable from a workspace that is clean, which is
    /// the whole reason the contract answers with an MCP error instead of an empty list.
    /// </summary>
    [Fact]
    public async Task A_failing_tool_call_surfaces_instead_of_looking_clean()
    {
        var gateway = new McpLanguageServerGateway(new FailingInvoker());

        await Assert.ThrowsAsync<LspGatewayException>(
            () => gateway.GetDiagnosticsAsync(".", CancellationToken.None));
    }

    /// <summary>The gate asks once per call. Waiting on <c>indexing</c> belongs to the graph, which bounds it.</summary>
    [Fact]
    public async Task An_indexing_answer_returns_immediately_rather_than_being_retried_here()
    {
        var invoker = new RecordingInvoker(
            """{ "status": "indexing", "total": 0, "truncated": false, "items": [], "statusDetail": "loading" }""");

        var gateway = new McpLanguageServerGateway(invoker);

        var verdict = await gateway.GetDiagnosticsAsync(".", CancellationToken.None);

        Assert.Equal(GateStatus.Indexing, verdict.Status);
        Assert.Equal(1, invoker.CallCount);
    }

    private sealed class RecordingInvoker : IMcpToolInvoker
    {
        private readonly string _answer;

        public RecordingInvoker(string answer) => _answer = answer;

        public string? ToolName { get; private set; }

        public IReadOnlyDictionary<string, object?> Arguments { get; private set; } =
            new Dictionary<string, object?>();

        public int CallCount { get; private set; }

        public Task<string> InvokeAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            ToolName = toolName;
            Arguments = arguments;
            CallCount++;
            return Task.FromResult(_answer);
        }
    }

    private sealed class FailingInvoker : IMcpToolInvoker
    {
        public Task<string> InvokeAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken) =>
            throw new LspGatewayException("the language server is not running");
    }
}
