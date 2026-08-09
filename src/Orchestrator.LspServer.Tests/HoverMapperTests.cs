using Orchestrator.LspServer.Mapping;
using Orchestrator.LspServer.Protocol;

namespace Orchestrator.LspServer.Tests;

public sealed class HoverMapperTests
{
    [Fact]
    public void Summarize_splits_the_roslyn_hover_into_signature_and_prose()
    {
        var hover = Hover(
            "```csharp\n" +
            "bool Tarea.Completar(IReadOnlyList<Tarea> prerequisitos)\n" +
            "```\n" +
            "Completes the task, refusing while any prerequisite is still open \\(RN\\-01\\)\\.");

        var summary = HoverMapper.Summarize(hover);

        Assert.Equal("bool Tarea.Completar(IReadOnlyList<Tarea> prerequisitos)", summary.Signature);
        Assert.Equal("Completes the task, refusing while any prerequisite is still open (RN-01).", summary.Documentation);
    }

    [Fact]
    public void Summarize_handles_the_typescript_hover_shape()
    {
        var hover = Hover(
            "```typescript\n(method) Tarea.completar(prerequisitos: readonly Tarea[]): boolean\n```");

        var summary = HoverMapper.Summarize(hover);

        Assert.Equal("(method) Tarea.completar(prerequisitos: readonly Tarea[]): boolean", summary.Signature);
        Assert.Null(summary.Documentation);
    }

    [Fact]
    public void Summarize_falls_back_to_the_whole_text_when_there_is_no_code_fence()
    {
        var summary = HoverMapper.Summarize(Hover("class Tarea"));

        Assert.Equal("class Tarea", summary.Signature);
    }

    [Fact]
    public void Summarize_of_nothing_is_nothing()
    {
        Assert.Null(HoverMapper.Summarize(null).Signature);
        Assert.Null(HoverMapper.Summarize(Hover("   ")).Signature);
    }

    private static LspHover Hover(string value) => new()
    {
        Contents = new LspMarkupContent { Value = value },
    };
}
