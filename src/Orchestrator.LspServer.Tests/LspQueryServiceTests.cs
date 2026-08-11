using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestrator.LspServer.Configuration;
using Orchestrator.LspServer.Contract;
using Orchestrator.LspServer.LanguageServers;
using Orchestrator.LspServer.Protocol;
using Orchestrator.LspServer.Tools;
using Orchestrator.LspServer.Workspace;

namespace Orchestrator.LspServer.Tests;

public sealed class LspQueryServiceTests : IDisposable
{
    private readonly TemporaryWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    /// <summary>
    /// The single most important test in this project.
    /// </summary>
    /// <remarks>
    /// The fake is loaded with diagnostics <em>and</em> reported as still indexing, which is the
    /// state a real language server passes through on every cold start. The contract must answer
    /// "I do not know yet" and must not produce a verdict of any kind. Getting this wrong is how
    /// a gate approves code that does not compile (ADR-006, ADR-010).
    /// </remarks>
    [Fact]
    public async Task Diagnostics_answers_indexing_and_no_verdict_while_the_session_is_loading()
    {
        _workspace.WriteFile("src/Tarea.cs", "class Tarea { }");

        var session = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs")
        {
            IndexingState = new IndexingState(false, "Roslyn is loading the solution 'App.slnx'"),
        };
        session.Diagnostics["*"] = [Error("CS1061", 41, 16)];

        var response = await CreateService(session).GetDiagnosticsAsync(".", CancellationToken.None);

        Assert.Equal(IndexingStatusNames.Indexing, response.Status);
        Assert.Empty(response.Items);
        Assert.Equal(0, response.Total);
        Assert.Equal("Roslyn is loading the solution 'App.slnx'", response.StatusDetail);
    }

    [Fact]
    public async Task Diagnostics_maps_position_severity_and_source_once_ready()
    {
        _workspace.WriteFile("src/Tarea.cs", "class Tarea { }");

        var session = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");
        session.Diagnostics["*"] = [Error("CS1061", 41, 16)];

        var response = await CreateService(session).GetDiagnosticsAsync(".", CancellationToken.None);

        Assert.Equal(IndexingStatusNames.Ready, response.Status);
        var item = Assert.Single(response.Items);
        Assert.Equal("src/Tarea.cs", item.FilePath);
        Assert.Equal(42, item.Range.StartLine);
        Assert.Equal(17, item.Range.StartColumn);
        Assert.Equal(DiagnosticSeverityNames.Error, item.Severity);
        Assert.Equal("CS1061", item.Code);
        Assert.Equal(DiagnosticSourceNames.Roslyn, item.Source);
    }

    /// <summary>An empty list only ever means "no errors" when it arrives with <c>ready</c>.</summary>
    [Fact]
    public async Task Diagnostics_of_a_clean_workspace_is_ready_and_empty()
    {
        _workspace.WriteFile("src/Tarea.cs", "class Tarea { }");

        var session = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");

        var response = await CreateService(session).GetDiagnosticsAsync(".", CancellationToken.None);

        Assert.Equal(IndexingStatusNames.Ready, response.Status);
        Assert.Empty(response.Items);
    }

    /// <summary>
    /// One session still loading makes the whole answer inconclusive, even when the other is done.
    /// The safe direction is the only acceptable one: a partial verdict read as a full one is a
    /// false green.
    /// </summary>
    [Fact]
    public async Task Diagnostics_is_indexing_when_any_language_server_in_scope_is_still_loading()
    {
        _workspace.WriteFile("src/Tarea.cs", "class Tarea { }");
        _workspace.WriteFile("src/tarea.ts", "export class Tarea {}");

        var readySession = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");
        var loadingSession = new FakeLanguageServerSession(DiagnosticSourceNames.TypeScript, ".ts")
        {
            IndexingState = new IndexingState(false, "the TypeScript language server is still starting"),
        };

        var response = await CreateService(readySession, loadingSession)
            .GetDiagnosticsAsync(".", CancellationToken.None);

        Assert.Equal(IndexingStatusNames.Indexing, response.Status);
    }

    [Fact]
    public async Task Diagnostics_refuses_a_scope_outside_the_workspace()
    {
        var session = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");
        var service = CreateService(session);

        await Assert.ThrowsAsync<ToolInputException>(
            () => service.GetDiagnosticsAsync("../../../Windows", CancellationToken.None));
    }

    /// <summary>
    /// A dead language server has to travel as an exception. Answering <c>items: []</c> would
    /// report a workspace nobody analysed as clean — the false green through the side door.
    /// </summary>
    [Fact]
    public async Task Diagnostics_propagates_a_dead_language_server_instead_of_answering_empty()
    {
        _workspace.WriteFile("src/Tarea.cs", "class Tarea { }");

        var session = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");
        var registry = new FakeLanguageServerRegistry(session);
        registry.FailStartup(DiagnosticSourceNames.Roslyn, new InvalidOperationException("executable not found"));

        var service = CreateService(registry);

        await Assert.ThrowsAsync<LanguageServerException>(
            () => service.GetDiagnosticsAsync(".", CancellationToken.None));
    }

    [Fact]
    public async Task Definition_converts_the_one_based_request_into_a_zero_based_protocol_position()
    {
        var documentFullPath = _workspace.WriteFile("src/Tarea.cs", "class Tarea { }");

        var session = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");
        session.Definitions.Add(new LspLocation
        {
            Uri = new Uri(documentFullPath).AbsoluteUri,
            Range = Range(17, 4),
        });

        var response = await CreateService(session)
            .GetDefinitionAsync("src/Tarea.cs", line: 42, column: 17, CancellationToken.None);

        var requested = Assert.Single(session.RequestedPositions);
        Assert.Equal(41, requested.Line);
        Assert.Equal(16, requested.Character);

        Assert.True(response.Found);
        Assert.Equal("src/Tarea.cs", response.FilePath);
        Assert.Equal(18, response.Range!.StartLine);
    }

    [Fact]
    public async Task Definition_takes_the_signature_from_the_hover()
    {
        var documentFullPath = _workspace.WriteFile("src/Tarea.cs", "class Tarea { }");

        var session = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");
        session.Definitions.Add(new LspLocation { Uri = new Uri(documentFullPath).AbsoluteUri, Range = Range(17, 4) });
        session.Hover = new LspHover
        {
            Contents = new LspMarkupContent
            {
                Value = "```csharp\nbool Tarea.Completar(IReadOnlyList<Tarea> prerequisitos)\n```\nCompletes it \\(RN\\-01\\)\\.",
            },
        };

        var response = await CreateService(session)
            .GetDefinitionAsync("src/Tarea.cs", 1, 1, CancellationToken.None);

        Assert.Equal("bool Tarea.Completar(IReadOnlyList<Tarea> prerequisitos)", response.Signature);
        Assert.Equal("Completes it (RN-01).", response.Documentation);
    }

    [Fact]
    public async Task Definition_reports_not_found_as_ready_not_as_indexing()
    {
        _workspace.WriteFile("src/Tarea.cs", "class Tarea { }");

        var session = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");

        var response = await CreateService(session)
            .GetDefinitionAsync("src/Tarea.cs", 1, 1, CancellationToken.None);

        Assert.Equal(IndexingStatusNames.Ready, response.Status);
        Assert.False(response.Found);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task Definition_rejects_a_position_below_one(int line, int column)
    {
        _workspace.WriteFile("src/Tarea.cs", "class Tarea { }");

        var service = CreateService(new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs"));

        await Assert.ThrowsAsync<ToolInputException>(
            () => service.GetDefinitionAsync("src/Tarea.cs", line, column, CancellationToken.None));
    }

    [Fact]
    public async Task References_carry_the_source_line_so_the_agent_need_not_open_the_file()
    {
        var documentFullPath = _workspace.WriteFile(
            "src/Tarea.cs",
            "class Tarea\n{\n    public bool Completar() => true;\n}\n");

        var session = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");
        session.References.Add(new LspLocation { Uri = new Uri(documentFullPath).AbsoluteUri, Range = Range(2, 16) });

        var response = await CreateService(session)
            .GetReferencesAsync("src/Tarea.cs", 1, 1, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("public bool Completar() => true;", item.Preview);
        Assert.Equal(3, item.Range.StartLine);
    }

    [Fact]
    public async Task WorkspaceSymbol_refuses_an_empty_query() =>
        await Assert.ThrowsAsync<ToolInputException>(
            () => CreateService(new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs"))
                .GetWorkspaceSymbolsAsync("   ", CancellationToken.None));

    /// <summary>
    /// A server that owns nothing in this workspace is not asked, and cannot spoil the answer.
    /// </summary>
    /// <remarks>
    /// The regression guard for what block 5's first full run produced. The frontend folder had no
    /// TypeScript in it yet, so <c>typescript-language-server</c> had no project loaded and
    /// answered <c>workspace/symbol</c> with an error — which propagated and failed a query about
    /// the C# that Roslyn had loaded perfectly. It landed on the API agent, whose own template
    /// tells it to look a domain symbol up before writing against it, so the tool failed exactly
    /// where it was most needed.
    /// </remarks>
    [Fact]
    public async Task WorkspaceSymbol_does_not_ask_a_server_that_owns_nothing_here()
    {
        var documentFullPath = _workspace.WriteFile("src/Domain/Tarea.cs", "class Tarea { }");

        var roslyn = new FakeLanguageServerSession(DiagnosticSourceNames.Roslyn, ".cs");
        roslyn.WorkspaceSymbols.Add(new LspSymbolInformation
        {
            Name = "Tarea",
            Kind = 5,
            Location = new LspLocation { Uri = new Uri(documentFullPath).AbsoluteUri, Range = Range(0, 6) },
        });

        var typescript = new FakeLanguageServerSession(DiagnosticSourceNames.TypeScript, ".ts")
        {
            FailsWorkspaceSymbols = true,
        };

        var response = await CreateService(roslyn, typescript)
            .GetWorkspaceSymbolsAsync("Tarea", CancellationToken.None);

        Assert.Equal(IndexingStatusNames.Ready, response.Status);
        Assert.Equal("Tarea", Assert.Single(response.Items).Name);
        Assert.False(
            typescript.WasAskedForWorkspaceSymbols,
            "the TypeScript server owns no document here and should not have been queried.");
    }

    private LspQueryService CreateService(params ILanguageServerSession[] sessions) =>
        CreateService(new FakeLanguageServerRegistry(sessions));

    private LspQueryService CreateService(ILanguageServerRegistry registry) => new(
        registry,
        new WorkspacePaths(_workspace.RootFullPath),
        Options.Create(new LspServerOptions { WorkspaceRoot = _workspace.RootFullPath }),
        NullLogger<LspQueryService>.Instance);

    private static LspRange Range(int zeroBasedLine, int zeroBasedCharacter) => new()
    {
        Start = new LspPosition { Line = zeroBasedLine, Character = zeroBasedCharacter },
        End = new LspPosition { Line = zeroBasedLine, Character = zeroBasedCharacter + 9 },
    };

    private static LspDiagnostic Error(string code, int zeroBasedLine, int zeroBasedCharacter) => new()
    {
        Range = Range(zeroBasedLine, zeroBasedCharacter),
        Severity = 1,
        Code = JsonDocument.Parse($"\"{code}\"").RootElement,
        Message = "'Tarea' does not contain a definition for 'Cerrar'",
    };
}
