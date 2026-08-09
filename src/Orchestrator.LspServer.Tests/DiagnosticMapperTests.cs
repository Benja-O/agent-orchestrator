using System.Text.Json;
using Orchestrator.LspServer.Contract;
using Orchestrator.LspServer.Mapping;
using Orchestrator.LspServer.Protocol;

namespace Orchestrator.LspServer.Tests;

public sealed class DiagnosticMapperTests
{
    [Fact]
    public void ToSourceRange_converts_the_protocol_zero_based_range_to_one_based()
    {
        var range = new LspRange
        {
            Start = new LspPosition { Line = 41, Character = 16 },
            End = new LspPosition { Line = 41, Character = 33 },
        };

        var converted = DiagnosticMapper.ToSourceRange(range);

        Assert.Equal(42, converted.StartLine);
        Assert.Equal(17, converted.StartColumn);
        Assert.Equal(42, converted.EndLine);
        Assert.Equal(34, converted.EndColumn);
    }

    [Theory]
    [InlineData(1, DiagnosticSeverityNames.Error)]
    [InlineData(2, DiagnosticSeverityNames.Warning)]
    [InlineData(3, DiagnosticSeverityNames.Information)]
    [InlineData(4, DiagnosticSeverityNames.Hint)]
    public void ToSeverityName_maps_every_protocol_severity(int severity, string expected) =>
        Assert.Equal(expected, DiagnosticMapper.ToSeverityName(severity));

    [Fact]
    public void ToCode_accepts_both_shapes_the_protocol_allows()
    {
        Assert.Equal("CS1061", DiagnosticMapper.ToCode(JsonDocument.Parse("\"CS1061\"").RootElement));
        Assert.Equal("2339", DiagnosticMapper.ToCode(JsonDocument.Parse("2339").RootElement));
        Assert.Equal(string.Empty, DiagnosticMapper.ToCode(null));
    }

    [Fact]
    public void Compose_orders_errors_first_then_by_file_then_by_line()
    {
        var diagnostics = new[]
        {
            Diagnostic("src/B.cs", 10, DiagnosticSeverityNames.Warning),
            Diagnostic("src/B.cs", 3, DiagnosticSeverityNames.Error),
            Diagnostic("src/A.cs", 99, DiagnosticSeverityNames.Error),
            Diagnostic("src/A.cs", 1, DiagnosticSeverityNames.Hint),
        };

        var response = DiagnosticMapper.Compose(diagnostics, IndexingStatusNames.Ready, maximumItems: 10);

        Assert.Collection(
            response.Items,
            first => AssertItem(first, "src/A.cs", 99, DiagnosticSeverityNames.Error),
            second => AssertItem(second, "src/B.cs", 3, DiagnosticSeverityNames.Error),
            third => AssertItem(third, "src/B.cs", 10, DiagnosticSeverityNames.Warning),
            fourth => AssertItem(fourth, "src/A.cs", 1, DiagnosticSeverityNames.Hint));
    }

    /// <summary>
    /// The cut is made at the end, so this is what guarantees the review loop is handed the
    /// errors and not an arbitrary subset of warnings.
    /// </summary>
    [Fact]
    public void Compose_truncates_by_the_tail_and_says_so()
    {
        var diagnostics = Enumerable
            .Range(1, 5)
            .Select(line => Diagnostic("src/A.cs", line, DiagnosticSeverityNames.Warning))
            .Append(Diagnostic("src/Z.cs", 500, DiagnosticSeverityNames.Error))
            .ToList();

        var response = DiagnosticMapper.Compose(diagnostics, IndexingStatusNames.Ready, maximumItems: 2);

        Assert.Equal(6, response.Total);
        Assert.True(response.Truncated);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(DiagnosticSeverityNames.Error, response.Items[0].Severity);
    }

    [Fact]
    public void Compose_reports_no_truncation_when_everything_fits()
    {
        var response = DiagnosticMapper.Compose(
            [Diagnostic("src/A.cs", 1, DiagnosticSeverityNames.Error)],
            IndexingStatusNames.Ready,
            maximumItems: 50);

        Assert.Equal(1, response.Total);
        Assert.False(response.Truncated);
    }

    private static DiagnosticItem Diagnostic(string filePath, int startLine, string severity) => new()
    {
        FilePath = filePath,
        Range = new SourceRange { StartLine = startLine, StartColumn = 1, EndLine = startLine, EndColumn = 2 },
        Severity = severity,
        Code = "CS0000",
        Message = "message",
        Source = DiagnosticSourceNames.Roslyn,
    };

    private static void AssertItem(DiagnosticItem item, string filePath, int startLine, string severity)
    {
        Assert.Equal(filePath, item.FilePath);
        Assert.Equal(startLine, item.Range.StartLine);
        Assert.Equal(severity, item.Severity);
    }
}
