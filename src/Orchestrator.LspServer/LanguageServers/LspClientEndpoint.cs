using System.Text.Json;
using Orchestrator.LspServer.Protocol;
using StreamJsonRpc;

namespace Orchestrator.LspServer.LanguageServers;

/// <summary>
/// Our side of the conversation: the methods the language server calls on <em>us</em>.
/// </summary>
/// <remarks>
/// <para>
/// Every method here carries <c>UseSingleObjectParameterDeserialization</c>, and that is not
/// decoration. JSON-RPC named parameters normally map an object's properties onto a method's
/// parameters one by one; LSP instead passes <b>one</b> object as the whole parameter set. Without
/// the flag, StreamJsonRpc looks for a parameter named after each JSON property, finds none,
/// and rejects the call with "an argument was not supplied for a required parameter".
/// </para>
/// <para>
/// The consequence is worth remembering, because it is how this integration fails: rejecting
/// <c>workspace/configuration</c> does not crash anything. Roslyn logs an error into its own
/// queue and simply never finishes loading the solution — so the server stays silent, the
/// contract answers <c>indexing</c> forever, and nothing anywhere says why. The tell is only
/// visible with <c>--LspServer:TraceProtocol=true</c>.
/// </para>
/// </remarks>
public class LspClientEndpoint
{
    private readonly LanguageServerSession _session;

    public LspClientEndpoint(LanguageServerSession session) => _session = session;

    protected LanguageServerSession Session => _session;

    /// <summary>
    /// Answers the settings the server asks for. Null per item means "not configured, use your
    /// default", which is what an editor with untouched settings sends too.
    /// </summary>
    [JsonRpcMethod("workspace/configuration", UseSingleObjectParameterDeserialization = true)]
    public object?[] GetConfiguration(LspConfigurationParams parameters) =>
        new object?[parameters.Items.Count];

    [JsonRpcMethod("client/registerCapability", UseSingleObjectParameterDeserialization = true)]
    public object? RegisterCapability(JsonElement parameters) => null;

    [JsonRpcMethod("client/unregisterCapability", UseSingleObjectParameterDeserialization = true)]
    public object? UnregisterCapability(JsonElement parameters) => null;

    [JsonRpcMethod("window/workDoneProgress/create", UseSingleObjectParameterDeserialization = true)]
    public object? CreateWorkDoneProgress(JsonElement parameters) => null;

    [JsonRpcMethod("workspace/diagnostic/refresh", UseSingleObjectParameterDeserialization = true)]
    public object? RefreshDiagnostics(JsonElement parameters) => null;

    [JsonRpcMethod("workspace/semanticTokens/refresh", UseSingleObjectParameterDeserialization = true)]
    public object? RefreshSemanticTokens(JsonElement parameters) => null;

    [JsonRpcMethod("workspace/inlayHint/refresh", UseSingleObjectParameterDeserialization = true)]
    public object? RefreshInlayHints(JsonElement parameters) => null;

    [JsonRpcMethod("workspace/codeLens/refresh", UseSingleObjectParameterDeserialization = true)]
    public object? RefreshCodeLenses(JsonElement parameters) => null;

    [JsonRpcMethod("window/showMessage", UseSingleObjectParameterDeserialization = true)]
    public void ShowMessage(JsonElement parameters)
    {
    }

    [JsonRpcMethod("telemetry/event", UseSingleObjectParameterDeserialization = true)]
    public void TelemetryEvent(JsonElement parameters)
    {
    }

    [JsonRpcMethod("$/progress", UseSingleObjectParameterDeserialization = true)]
    public void Progress(JsonElement parameters)
    {
    }

    [JsonRpcMethod("$/setTrace", UseSingleObjectParameterDeserialization = true)]
    public void SetTrace(JsonElement parameters)
    {
    }

    [JsonRpcMethod("window/logMessage", UseSingleObjectParameterDeserialization = true)]
    public void LogMessage(JsonElement parameters) => _session.LogServerMessage(parameters);

    [JsonRpcMethod(LspMethodNames.PublishDiagnostics, UseSingleObjectParameterDeserialization = true)]
    public void PublishDiagnostics(JsonElement parameters) => _session.HandleDiagnosticsPublished(parameters);
}

/// <summary>The extra calls only <c>Microsoft.CodeAnalysis.LanguageServer</c> makes.</summary>
public sealed class RoslynClientEndpoint : LspClientEndpoint
{
    private readonly Action _onProjectInitializationComplete;

    public RoslynClientEndpoint(LanguageServerSession session, Action onProjectInitializationComplete)
        : base(session) =>
        _onProjectInitializationComplete = onProjectInitializationComplete;

    /// <summary>
    /// The signal the whole <c>status</c> field rests on: the solution is loaded and a
    /// diagnostics pull now means something. It carries no parameters.
    /// </summary>
    [JsonRpcMethod(LspMethodNames.RoslynProjectInitializationComplete)]
    public void ProjectInitializationComplete() => _onProjectInitializationComplete();

    [JsonRpcMethod("window/_roslyn_showToast", UseSingleObjectParameterDeserialization = true)]
    public void ShowToast(JsonElement parameters) => Session.LogServerMessage(parameters);

    [JsonRpcMethod("workspace/didChangeWatchedFiles", UseSingleObjectParameterDeserialization = true)]
    public object? DidChangeWatchedFiles(JsonElement parameters) => null;

    [JsonRpcMethod("workspace/attachDebugger", UseSingleObjectParameterDeserialization = true)]
    public object? AttachDebugger(JsonElement parameters) => null;

    [JsonRpcMethod("workspace/debugConfiguration", UseSingleObjectParameterDeserialization = true)]
    public object[] DebugConfiguration(JsonElement parameters) => [];
}
