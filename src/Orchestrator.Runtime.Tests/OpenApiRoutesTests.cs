using Orchestrator.Runtime;

namespace Orchestrator.Runtime.Tests;

/// <summary>
/// Which endpoints the runtime gate decides to call.
/// </summary>
/// <remarks>
/// Pure string work, so it is tested without starting anything — which is the point of it living
/// in its own class. The real verifier runs <c>dotnet run</c> and waits minutes; the rules about
/// what it chooses to exercise should not have to.
/// </remarks>
public sealed class OpenApiRoutesTests
{
    private const string TaskManagerDocument = """
        {
          "openapi": "3.0.1",
          "paths": {
            "/api/tareas": {
              "get": { "responses": { "200": {} } },
              "post": { "responses": { "201": {} } }
            },
            "/api/tareas/{id}": {
              "get": { "responses": { "200": {} } },
              "delete": { "responses": { "204": {} } }
            },
            "/api/tareas/{id}/completar": {
              "post": { "responses": { "200": {} } }
            }
          }
        }
        """;

    [Fact]
    public void The_collection_endpoint_is_discovered()
    {
        var routes = OpenApiRoutes.ParameterlessGets(TaskManagerDocument);

        Assert.Equal(["/api/tareas"], routes);
    }

    /// <summary>
    /// Routes with path parameters are skipped, because calling them needs data to be invented.
    /// </summary>
    [Fact]
    public void A_route_with_a_path_parameter_is_not_called()
    {
        Assert.DoesNotContain("/api/tareas/{id}", OpenApiRoutes.ParameterlessGets(TaskManagerDocument));
    }

    /// <summary>
    /// Only <c>GET</c>. A verification that changed the state of the thing it is verifying would
    /// be measuring something it had just altered.
    /// </summary>
    [Fact]
    public void A_path_that_only_answers_post_is_not_called()
    {
        Assert.DoesNotContain("/api/tareas/{id}/completar", OpenApiRoutes.ParameterlessGets(TaskManagerDocument));
    }

    [Fact]
    public void A_get_with_a_required_query_parameter_is_skipped()
    {
        const string Document = """
            {
              "paths": {
                "/api/buscar": {
                  "get": { "parameters": [{ "name": "q", "in": "query", "required": true }] }
                }
              }
            }
            """;

        Assert.Empty(OpenApiRoutes.ParameterlessGets(Document));
    }

    [Fact]
    public void An_optional_query_parameter_does_not_disqualify_a_route()
    {
        const string Document = """
            {
              "paths": {
                "/api/tareas": {
                  "get": { "parameters": [{ "name": "orden", "in": "query", "required": false }] }
                }
              }
            }
            """;

        Assert.Equal(["/api/tareas"], OpenApiRoutes.ParameterlessGets(Document));
    }

    /// <summary>
    /// A document that cannot be read yields nothing — and the caller turns nothing into a
    /// failure, never into an approval.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("<html>404</html>")]
    [InlineData("{}")]
    [InlineData("""{ "paths": null }""")]
    public void An_unusable_document_discovers_nothing(string document)
    {
        Assert.Empty(OpenApiRoutes.ParameterlessGets(document));
    }

    [Fact]
    public void Routes_come_back_in_a_stable_order()
    {
        const string Document = """
            {
              "paths": {
                "/zeta": { "get": {} },
                "/alfa": { "get": {} }
              }
            }
            """;

        Assert.Equal(["/alfa", "/zeta"], OpenApiRoutes.ParameterlessGets(Document));
    }
}
