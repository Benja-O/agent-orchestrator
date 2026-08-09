using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Orchestrator.Domain;

namespace Orchestrator.Observability;

/// <summary>
/// Turns a <see cref="RunEvent"/> into the JSON object that goes on one line of the log.
/// </summary>
/// <remarks>
/// The three keys every line starts with — <c>timestamp</c>, <c>run</c>, <c>event</c> — are
/// fixed and first, so a line is identifiable with a <c>grep</c> and the file stays readable
/// even without a parser. <c>summary</c> is dropped: it is the console rendering of the same
/// data, and duplicating it into the machine-readable copy would double the file for nothing.
/// </remarks>
public static class RunEventSerialization
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static JsonObject ToJsonObject(RunEvent runEvent)
    {
        var serialized = JsonSerializer.SerializeToNode(runEvent, runEvent.GetType(), Options)!.AsObject();

        var line = new JsonObject
        {
            ["timestamp"] = serialized["timestamp"]?.DeepClone(),
            ["run"] = serialized["runId"]?.DeepClone(),
            ["event"] = serialized["event"]?.DeepClone(),
        };

        foreach (var property in serialized)
        {
            if (property.Key is "timestamp" or "runId" or "event" or "summary")
            {
                continue;
            }

            // Named for its unit, because a bare number called "duration" is the kind of
            // ambiguity that gets aggregated wrong six months later.
            var key = property.Key == "duration" ? "durationMs" : property.Key;
            line[key] = property.Value?.DeepClone();
        }

        return line;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new RunIdConverter());
        options.Converters.Add(new NodeIdConverter());
        options.Converters.Add(new MillisecondsConverter());

        return options;
    }

    private sealed class RunIdConverter : JsonConverter<RunId>
    {
        public override RunId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetString() ?? string.Empty);

        public override void Write(Utf8JsonWriter writer, RunId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    private sealed class NodeIdConverter : JsonConverter<NodeId>
    {
        public override NodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetString() ?? string.Empty);

        public override void Write(Utf8JsonWriter writer, NodeId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    /// <summary>Durations as a number of milliseconds, so the log can be aggregated without parsing a duration format.</summary>
    private sealed class MillisecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TimeSpan.FromMilliseconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(Math.Round(value.TotalMilliseconds, 3));
    }
}
