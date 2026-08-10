using Orchestrator.Domain;

namespace Orchestrator.Lsp.Tests;

/// <summary>
/// The seam between the MCP contract's wire shape and the type the graph decides on.
/// </summary>
/// <remarks>
/// Worth testing this closely because every way of getting it wrong produces the same symptom
/// and it is the worst one available: a gate that says the code is fine when it is not.
/// </remarks>
public sealed class DiagnosticTranslationTests
{
    /// <summary>The real answer of the real server about the fixture that is broken on purpose.</summary>
    private static string RecordedReadyResponse() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "diagnostics-ready-roslyn.json"));

    [Fact]
    public void A_recorded_ready_response_becomes_a_ready_verdict()
    {
        var verdict = DiagnosticTranslation.FromJson(RecordedReadyResponse());

        Assert.Equal(GateStatus.Ready, verdict.Status);
        Assert.Null(verdict.StatusDetail);
        Assert.Equal(5, verdict.Diagnostics.Total);
        Assert.False(verdict.Diagnostics.Truncated);
        Assert.Equal(5, verdict.Diagnostics.Items.Count);
    }

    /// <summary>
    /// The one that matters for the conditional edge: of the five diagnostics the real server
    /// returned, exactly one blocks compilation and the other four are suggestions.
    /// </summary>
    [Fact]
    public void Only_the_compiler_error_of_the_recorded_response_blocks()
    {
        var verdict = DiagnosticTranslation.FromJson(RecordedReadyResponse());

        var blocking = Assert.Single(verdict.Diagnostics.BlockingItems);
        Assert.Equal("CS1061", blocking.Code);
        Assert.Equal("Api/TareasController.cs", blocking.FilePath);
        Assert.Equal("roslyn", blocking.Source);
        Assert.Contains("does not contain a definition for 'Cerrar'", blocking.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 1-based, as the contract promises. An off-by-one here does not fail loudly: it sends the
    /// agent to edit the line above the broken one.
    /// </summary>
    [Fact]
    public void The_range_arrives_one_based_exactly_as_the_contract_exposes_it()
    {
        var verdict = DiagnosticTranslation.FromJson(RecordedReadyResponse());
        var blocking = verdict.Diagnostics.BlockingItems.Single();

        Assert.Equal(new SourceRange(27, 22, 27, 28), blocking.Range);
    }

    [Fact]
    public void Indexing_is_never_an_approval_and_carries_what_is_being_waited_on()
    {
        const string Json = """
            { "status": "indexing", "total": 0, "truncated": false, "items": [],
              "statusDetail": "Roslyn is loading the solution 'App.slnx'" }
            """;

        var verdict = DiagnosticTranslation.FromJson(Json);

        Assert.Equal(GateStatus.Indexing, verdict.Status);
        Assert.Equal("Roslyn is loading the solution 'App.slnx'", verdict.StatusDetail);
    }

    /// <summary>
    /// An indexing answer with items is still not an answer, and the items travel anyway so the
    /// log can show what the server had found so far.
    /// </summary>
    [Fact]
    public void A_partial_list_during_indexing_is_carried_but_not_approved()
    {
        const string Json = """
            { "status": "indexing", "total": 1, "truncated": false, "statusDetail": "still going",
              "items": [ { "filePath": "src/Domain/Tarea.cs",
                           "range": { "startLine": 4, "startColumn": 1, "endLine": 4, "endColumn": 9 },
                           "severity": "error", "code": "CS0103", "message": "boom", "source": "roslyn" } ] }
            """;

        var verdict = DiagnosticTranslation.FromJson(Json);

        Assert.Equal(GateStatus.Indexing, verdict.Status);
        Assert.Single(verdict.Diagnostics.Items);
    }

    [Fact]
    public void An_indexing_answer_without_detail_still_says_something_diagnosable()
    {
        const string Json = """{ "status": "indexing", "total": 0, "truncated": false, "items": [] }""";

        var verdict = DiagnosticTranslation.FromJson(Json);

        Assert.Equal(GateStatus.Indexing, verdict.Status);
        Assert.False(string.IsNullOrWhiteSpace(verdict.StatusDetail));
    }

    [Fact]
    public void Truncation_is_carried_through_because_the_fingerprint_depends_on_it()
    {
        const string Json = """
            { "status": "ready", "total": 240, "truncated": true,
              "items": [ { "filePath": "src/Api/Controller.cs",
                           "range": { "startLine": 1, "startColumn": 1, "endLine": 1, "endColumn": 2 },
                           "severity": "warning", "code": "CS0168", "message": "unused", "source": "roslyn" } ] }
            """;

        var verdict = DiagnosticTranslation.FromJson(Json);

        Assert.True(verdict.Diagnostics.Truncated);
        Assert.Equal(240, verdict.Diagnostics.Total);
        Assert.Single(verdict.Diagnostics.Items);
    }

    [Theory]
    [InlineData("error", DiagnosticSeverity.Error)]
    [InlineData("warning", DiagnosticSeverity.Warning)]
    [InlineData("information", DiagnosticSeverity.Information)]
    [InlineData("hint", DiagnosticSeverity.Hint)]
    public void The_four_severities_of_the_contract_map_across(string wireSeverity, DiagnosticSeverity expected)
    {
        var verdict = DiagnosticTranslation.FromJson(ResponseWithSeverity(wireSeverity));

        Assert.Equal(expected, verdict.Diagnostics.Items.Single().Severity);
    }

    /// <summary>
    /// Contract drift is an exception, not a default. Every convenient fallback available here —
    /// assume ready, drop the item, call it a hint — ends in the gate approving broken code.
    /// </summary>
    [Theory]
    [InlineData("""{ "status": "almost", "total": 0, "truncated": false, "items": [] }""")]
    [InlineData("""{ "total": 0, "truncated": false, "items": [] }""")]
    [InlineData("not json at all")]
    public void An_answer_that_cannot_be_read_fails_instead_of_degrading(string json)
    {
        Assert.Throws<LspGatewayException>(() => DiagnosticTranslation.FromJson(json));
    }

    [Fact]
    public void An_unknown_severity_fails_rather_than_being_softened()
    {
        Assert.Throws<LspGatewayException>(() => DiagnosticTranslation.FromJson(ResponseWithSeverity("catastrophe")));
    }

    /// <summary>
    /// A diagnostic with no path could not be attributed to a layer, and a diagnostic no layer
    /// owns stops the run rather than being discarded (ADR-010).
    /// </summary>
    [Fact]
    public void A_diagnostic_without_a_file_path_fails()
    {
        const string Json = """
            { "status": "ready", "total": 1, "truncated": false,
              "items": [ { "range": { "startLine": 1, "startColumn": 1, "endLine": 1, "endColumn": 2 },
                           "severity": "error", "code": "CS0103", "message": "boom", "source": "roslyn" } ] }
            """;

        Assert.Throws<LspGatewayException>(() => DiagnosticTranslation.FromJson(Json));
    }

    [Fact]
    public void A_ready_response_with_no_items_is_the_clean_verdict()
    {
        const string Json = """{ "status": "ready", "total": 0, "truncated": false, "items": [] }""";

        var verdict = DiagnosticTranslation.FromJson(Json);

        Assert.Equal(GateStatus.Ready, verdict.Status);
        Assert.False(verdict.Diagnostics.HasBlockingItems);
    }

    private static string ResponseWithSeverity(string severity) => $$"""
        { "status": "ready", "total": 1, "truncated": false,
          "items": [ { "filePath": "src/Domain/Tarea.cs",
                       "range": { "startLine": 4, "startColumn": 1, "endLine": 4, "endColumn": 9 },
                       "severity": "{{severity}}", "code": "X", "message": "m", "source": "roslyn" } ] }
        """;
}
