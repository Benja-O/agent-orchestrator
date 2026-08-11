using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Orchestrator.GeneratedAppVerification;

/// <summary>One request and what came back, kept so the report can show its own evidence.</summary>
public sealed record Exchange(string Method, string Url, string RequestBody, HttpStatusCode Status, string ResponseBody)
{
    public bool Succeeded => (int)Status is >= 200 and < 300;

    public bool RejectedByTheClient => (int)Status is >= 400 and < 500;

    public override string ToString() =>
        $"{Method} {Url} → {(int)Status} {Status}"
        + (RequestBody.Length > 0 ? $"{Environment.NewLine}      sent: {Compact(RequestBody)}" : string.Empty)
        + (ResponseBody.Length > 0 ? $"{Environment.NewLine}      got:  {Compact(ResponseBody)}" : string.Empty);

    private static string Compact(string body)
    {
        var single = body.ReplaceLineEndings(" ").Trim();
        return single.Length <= 400 ? single : single[..400] + "…";
    }
}

/// <summary>
/// Talks to the generated API without having been told its field names.
/// </summary>
/// <remarks>
/// <para>
/// The routes come in as flags (see <see cref="ApiShape"/>); the property names are probed
/// instead, because there are only a handful of plausible ones and asking a person for six more
/// flags to run one check is how a verification stops being run. Every attempt is recorded in
/// <see cref="Exchanges"/>, so a probe that guesses wrong shows up as a list of exactly what was
/// tried rather than as a mystery.
/// </para>
/// <para>
/// Probing is honest here for a reason worth stating: nothing about the invariant depends on what
/// the fields are called. If the API refuses the completion and names the blocking prerequisite,
/// RN-01 holds whatever the JSON looks like.
/// </para>
/// </remarks>
public sealed class TaskManagerClient : IDisposable
{
    private static readonly string[] TitleFieldCandidates = ["titulo", "title", "nombre", "name"];

    private static readonly string[] DependencyFieldCandidates =
        ["dependenciaId", "dependeDeId", "prerequisitoId", "dependencyId", "dependsOnId", "prerequisiteId", "tareaId", "id"];

    private static readonly string[] IdentifierFieldCandidates = ["id", "identificador", "identifier", "tareaId", "taskId"];

    private static readonly string[] StatusFieldCandidates =
        ["estado", "status", "state", "completada", "completed", "isCompleted", "estaCompletada"];

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ApiShape _shape;
    private readonly List<Exchange> _exchanges = [];

    private string? _knownTitleField;

    public TaskManagerClient(ApiShape shape) => _shape = shape;

    public IReadOnlyList<Exchange> Exchanges => _exchanges;

    /// <summary>Creates a task and returns its identifier, or null with the attempts recorded.</summary>
    public async Task<string?> CreateTaskAsync(string title, CancellationToken cancellationToken)
    {
        foreach (var field in _knownTitleField is null ? TitleFieldCandidates : [_knownTitleField])
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, string> { [field] = title });
            var exchange = await SendAsync(HttpMethod.Post, _shape.ResolveTasks(), body, cancellationToken).ConfigureAwait(false);

            if (!exchange.Succeeded)
            {
                continue;
            }

            _knownTitleField = field;

            // The identifier may come back in the creation response or only from the collection;
            // both are legitimate designs and neither is worth failing over.
            return ReadIdentifier(exchange.ResponseBody)
                ?? await FindIdentifierByTitleAsync(title, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Declares that <paramref name="dependentIdentifier"/> depends on <paramref name="prerequisiteIdentifier"/>.</summary>
    public async Task<bool> DeclareDependencyAsync(
        string dependentIdentifier, string prerequisiteIdentifier, CancellationToken cancellationToken)
    {
        var url = _shape.ResolveDependencies(dependentIdentifier);

        foreach (var field in DependencyFieldCandidates)
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, string> { [field] = prerequisiteIdentifier });
            var exchange = await SendAsync(HttpMethod.Post, url, body, cancellationToken).ConfigureAwait(false);

            if (exchange.Succeeded)
            {
                return true;
            }

            // Every candidate gets tried, including after a 404. An earlier version stopped there,
            // reasoning that a 404 meant the route was wrong rather than the field — and a real
            // run disproved it in one line: the handler answers 404 when the prerequisite in the
            // body cannot be found, which is exactly what happens when the field name is wrong and
            // the id deserialises to its default. The shortcut turned "wrong guess" into "wrong
            // route" and pointed the report at the wrong thing.
        }

        // Some designs take the bare identifier as the whole body.
        var raw = await SendAsync(
            HttpMethod.Post, url, JsonSerializer.Serialize(prerequisiteIdentifier), cancellationToken).ConfigureAwait(false);

        return raw.Succeeded;
    }

    public Task<Exchange> CompleteAsync(string identifier, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, _shape.ResolveComplete(identifier), string.Empty, cancellationToken);

    public Task<Exchange> ListAsync(CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, _shape.ResolveTasks(), string.Empty, cancellationToken);

    /// <summary>
    /// Whether the listed task reads as completed.
    /// </summary>
    /// <remarks>
    /// Deliberately generous about the encoding — a string <c>"Completada"</c>, a boolean, an
    /// enum number — and deliberately strict about the default: an entity whose status cannot be
    /// read at all counts as not completed, so an unreadable answer fails the check that says the
    /// task stayed pending rather than passing it by accident.
    /// </remarks>
    public async Task<bool> IsCompletedAsync(string identifier, CancellationToken cancellationToken)
    {
        var listing = await ListAsync(cancellationToken).ConfigureAwait(false);

        if (!listing.Succeeded || FindEntity(listing.ResponseBody, identifier) is not { } entity)
        {
            return false;
        }

        foreach (var field in StatusFieldCandidates)
        {
            if (!entity.TryGetProperty(field, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => value.GetInt32() != 0,
                JsonValueKind.String => value.GetString() is { } text
                    && (text.Contains("complet", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("done", StringComparison.OrdinalIgnoreCase)),
                _ => false,
            };
        }

        return false;
    }

    /// <summary>The identifier of the task in the collection whose title matches.</summary>
    private async Task<string?> FindIdentifierByTitleAsync(string title, CancellationToken cancellationToken)
    {
        var listing = await ListAsync(cancellationToken).ConfigureAwait(false);

        if (!listing.Succeeded)
        {
            return null;
        }

        foreach (var entity in EnumerateEntities(listing.ResponseBody))
        {
            var carriesTheTitle = entity.EnumerateObject().Any(property =>
                property.Value.ValueKind == JsonValueKind.String
                && string.Equals(property.Value.GetString(), title, StringComparison.Ordinal));

            if (carriesTheTitle && ReadIdentifier(entity) is { } identifier)
            {
                return identifier;
            }
        }

        return null;
    }

    private static JsonElement? FindEntity(string json, string identifier)
    {
        foreach (var entity in EnumerateEntities(json))
        {
            if (ReadIdentifier(entity) == identifier)
            {
                return entity;
            }
        }

        return null;
    }

    /// <summary>Objects in a body, whether it is an array, a single object, or an envelope around one.</summary>
    private static IEnumerable<JsonElement> EnumerateEntities(string json)
    {
        JsonElement root;

        try
        {
            root = JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            yield break;
        }

        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in root.EnumerateArray())
                {
                    yield return item;
                }

                break;

            case JsonValueKind.Object:
                yield return root;

                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var item in property.Value.EnumerateArray())
                    {
                        yield return item;
                    }
                }

                break;
        }
    }

    private static string? ReadIdentifier(string json) =>
        EnumerateEntities(json).Select(ReadIdentifier).FirstOrDefault(identifier => identifier is not null);

    private static string? ReadIdentifier(JsonElement entity)
    {
        if (entity.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var field in IdentifierFieldCandidates)
        {
            if (!entity.TryGetProperty(field, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
        }

        return null;
    }

    private async Task<Exchange> SendAsync(
        HttpMethod method, string url, string body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body.Length > 0)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        Exchange exchange;

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            exchange = new Exchange(method.Method, url, body, response.StatusCode, responseBody);
        }
        catch (HttpRequestException failure)
        {
            exchange = new Exchange(method.Method, url, body, HttpStatusCode.ServiceUnavailable, failure.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            exchange = new Exchange(
                method.Method,
                url,
                body,
                HttpStatusCode.RequestTimeout,
                string.Create(CultureInfo.InvariantCulture, $"No answer within {_httpClient.Timeout.TotalSeconds:F0} s."));
        }

        _exchanges.Add(exchange);
        return exchange;
    }

    public void Dispose() => _httpClient.Dispose();
}
