using System.Text.Json;
using Orchestrator.Domain;

namespace Orchestrator.Observability;

/// <summary>
/// Writes one JSON object per line, the same pattern as the author's trading repository.
/// </summary>
/// <remarks>
/// This file is the run. With no UI and no persistence (ADR-007) there is nowhere else the
/// history of a run exists, so it is written as it happens and flushed on every line: a run
/// that crashes has to leave behind the part that already happened, which is precisely the
/// part worth reading.
/// </remarks>
public sealed class JsonlRunObserver : IRunObserver, IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private readonly Lock _gate = new();

    public JsonlRunObserver(TextWriter writer, bool ownsWriter = false)
    {
        _writer = writer;
        _ownsWriter = ownsWriter;
    }

    /// <summary>Opens (or creates) a log file and appends to it.</summary>
    public static JsonlRunObserver AppendingTo(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new JsonlRunObserver(new StreamWriter(filePath, append: true) { AutoFlush = true }, ownsWriter: true);
    }

    public void Observe(RunEvent runEvent)
    {
        var line = JsonSerializer.Serialize(RunEventSerialization.ToJsonObject(runEvent), RunEventSerialization.Options);

        lock (_gate)
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        if (_ownsWriter)
        {
            _writer.Dispose();
        }
    }
}
