using System.Text.Json;

namespace Orchestrator.Runtime;

/// <summary>
/// Reads the endpoints out of an OpenAPI document.
/// </summary>
/// <remarks>
/// <para>
/// Discovery rather than configuration, and the reason is the spec: <c>specs/gestor-tareas.md</c>
/// deliberately names no endpoints, so the routes are the API agent's to choose. Reading them
/// back from the application's own description is how the orchestrator exercises them without
/// ever having named one.
/// </para>
/// <para>
/// Pure string work, in its own class, so the selection rules can be tested without starting an
/// application (AI.md, golden rule 3).
/// </para>
/// </remarks>
public static class OpenApiRoutes
{
    /// <summary>
    /// The <c>GET</c> paths that take no parameters.
    /// </summary>
    /// <remarks>
    /// Parameterless because those are the ones that can be called without inventing data, and
    /// <c>GET</c> because a verification must not change anything: this runs against the same
    /// application the pipeline just built, and a check with side effects would be a check that
    /// alters what it is measuring.
    /// </remarks>
    public static IReadOnlyList<string> ParameterlessGets(string openApiDocument)
    {
        JsonElement root;

        try
        {
            root = JsonDocument.Parse(openApiDocument).RootElement.Clone();
        }
        catch (JsonException)
        {
            return [];
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("paths", out var paths)
            || paths.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var routes = new List<string>();

        foreach (var path in paths.EnumerateObject())
        {
            if (path.Name.Contains('{', StringComparison.Ordinal))
            {
                continue;
            }

            if (path.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var hasParameterlessGet = path.Value.EnumerateObject().Any(operation =>
                operation.Name.Equals("get", StringComparison.OrdinalIgnoreCase)
                && !RequiresQueryParameters(operation.Value));

            if (hasParameterlessGet)
            {
                routes.Add(path.Name);
            }
        }

        routes.Sort(StringComparer.Ordinal);
        return routes;
    }

    private static bool RequiresQueryParameters(JsonElement operation) =>
        operation.ValueKind == JsonValueKind.Object
        && operation.TryGetProperty("parameters", out var parameters)
        && parameters.ValueKind == JsonValueKind.Array
        && parameters.EnumerateArray().Any(parameter =>
            parameter.ValueKind == JsonValueKind.Object
            && parameter.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.True);
}
