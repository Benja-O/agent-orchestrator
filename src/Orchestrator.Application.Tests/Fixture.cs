using Orchestrator.Application.Spec;
using Orchestrator.Domain;

namespace Orchestrator.Application.Tests;

/// <summary>
/// Where recorded agent answers come from.
/// </summary>
/// <remarks>
/// One file per answer, replayed verbatim by <c>FakeAgentRunner</c> — that is what "recording
/// the responses" means in this project (ADR-014). The spec itself is not a copy: the real
/// <c>specs/gestor-tareas.md</c> is linked into the output, so a change to it that breaks its
/// own invariants breaks the suite instead of drifting away unnoticed.
/// </remarks>
internal static class Fixture
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string SpecAnalyzerAnswer(string fileName) =>
        File.ReadAllText(Path.Combine(Root, "spec-analyzer", fileName));

    public static string RealSpecText => File.ReadAllText(Path.Combine(Root, "specs", "gestor-tareas.md"));

    public static SpecDocument RealSpec
    {
        get
        {
            var parsed = SpecParser.Parse("specs/gestor-tareas.md", RealSpecText);

            Assert.True(parsed.IsSuccess, $"The repository's own spec does not parse: {parsed.FailureReason}");
            return parsed.Value;
        }
    }
}
