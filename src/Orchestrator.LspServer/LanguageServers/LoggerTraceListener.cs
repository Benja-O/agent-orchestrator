using System.Diagnostics;

namespace Orchestrator.LspServer.LanguageServers;

/// <summary>
/// Pipes StreamJsonRpc's own trace of every LSP message into the application log.
/// </summary>
/// <remarks>
/// Off by default and worth keeping: when a language server goes quiet instead of failing —
/// the characteristic way this integration breaks — the only way to tell "it never sent it"
/// from "we never matched it" is to read the traffic.
/// Enable with <c>--LspServer:TraceProtocol=true</c>.
/// </remarks>
public sealed class LoggerTraceListener : TraceListener
{
    private readonly ILogger _logger;
    private readonly string _sourceName;

    public LoggerTraceListener(ILogger logger, string sourceName)
    {
        _logger = logger;
        _sourceName = sourceName;
    }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _logger.LogInformation("[{SourceName} rpc] {Message}", _sourceName, message);
        }
    }

    public override void TraceEvent(
        TraceEventCache? eventCache, string source, TraceEventType eventType, int identifier, string? format, params object?[]? arguments)
    {
        var message = format is null
            ? string.Empty
            : arguments is null ? format : string.Format(format, arguments);

        WriteLine($"{eventType} {identifier}: {message}");
    }
}
