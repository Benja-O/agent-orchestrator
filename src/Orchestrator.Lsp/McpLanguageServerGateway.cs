using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orchestrator.Domain;

namespace Orchestrator.Lsp;

/// <summary>
/// Calls one tool of the MCP contract and hands back its raw text payload.
/// </summary>
/// <remarks>
/// The seam that keeps <see cref="McpLanguageServerGateway"/> testable without a socket. It is
/// deliberately narrow: the graph only ever wants a verdict (see
/// <see cref="ILanguageServerGateway"/>), and the four navigation tools of the contract belong
/// to the layer agents during their turn, not here.
/// </remarks>
internal interface IMcpToolInvoker
{
    Task<string> InvokeAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken);
}

/// <summary>
/// The graph's gate, over the MCP server of block 2.
/// </summary>
/// <remarks>
/// <para>
/// Note what this class does <em>not</em> do: it never waits, never retries and never sleeps.
/// Handling <c>indexing</c> is the graph's business and it already has a bounded loop for it in
/// <c>GateEvaluator</c> (<c>GraphPolicy.MaximumIndexWaitAttempts</c>). A second, unbounded wait
/// hidden down here would be exactly the uncapped cycle ADR-003 forbids, and the harder kind to
/// find because it would not look like a cycle from the graph.
/// </para>
/// </remarks>
public sealed class McpLanguageServerGateway : ILanguageServerGateway
{
    private const string DiagnosticsToolName = "diagnostics";

    private readonly IMcpToolInvoker _invoker;

    internal McpLanguageServerGateway(IMcpToolInvoker invoker) => _invoker = invoker;

    public static McpLanguageServerGateway Over(McpClient client) =>
        new(new McpClientToolInvoker(client));

    public async Task<GateVerdict> GetDiagnosticsAsync(string scope, CancellationToken cancellationToken)
    {
        var payload = await _invoker
            .InvokeAsync(DiagnosticsToolName, new Dictionary<string, object?> { ["scope"] = scope }, cancellationToken)
            .ConfigureAwait(false);

        return DiagnosticTranslation.FromJson(payload);
    }
}

/// <summary>The real invoker, over the official MCP client.</summary>
internal sealed class McpClientToolInvoker : IMcpToolInvoker
{
    private readonly McpClient _client;

    public McpClientToolInvoker(McpClient client) => _client = client;

    public async Task<string> InvokeAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        CallToolResult result;

        try
        {
            result = await _client
                .CallToolAsync(toolName, arguments, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LspGatewayException($"The call to the MCP tool '{toolName}' failed.", exception);
        }

        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

        // A tool error is infrastructure, not a verdict. The contract is explicit that a failing
        // server answers with an MCP error rather than an empty list, precisely so that this
        // case cannot be mistaken for "the workspace is clean" (ADR-010).
        if (result.IsError == true)
        {
            throw new LspGatewayException($"The MCP tool '{toolName}' answered with an error: {text}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new LspGatewayException($"The MCP tool '{toolName}' answered with no content.");
        }

        return text;
    }
}
