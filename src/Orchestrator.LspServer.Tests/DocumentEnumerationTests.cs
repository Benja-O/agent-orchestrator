using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Orchestrator.LspServer.LanguageServers;

namespace Orchestrator.LspServer.Tests;

/// <summary>
/// Which files in a workspace a language server is asked about — and, more to the point, which
/// ones it is never asked about.
/// </summary>
/// <remarks>
/// The list matters more than it looks, because everything on it is a file that would otherwise
/// end up in a gate verdict. Build output and <c>node_modules</c> are the obvious ones. The one
/// that actually bit, in block 5, is <c>.claude</c>: the orchestrator injects a <c>.js</c> hook
/// into the workspace it generates, <c>typescript-language-server</c> owns <c>.js</c>, and no
/// layer owns <c>.claude</c> — so one diagnostic there would end the run with "the gate reported
/// diagnostics in files that belong to no layer".
/// </remarks>
public sealed class DocumentEnumerationTests : IDisposable
{
    private readonly string _workspaceRoot =
        Path.Combine(Path.GetTempPath(), "orchestrator-enumeration-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void Application_sources_are_enumerated()
    {
        Write("src/Frontend/TareasView.tsx");
        Write("src/Frontend/api.ts");

        var documents = NewSession().EnumerateDocuments(_workspaceRoot);

        Assert.Equal(2, documents.Count);
    }

    /// <summary>
    /// The orchestrator's own plumbing is invisible to the gate.
    /// </summary>
    /// <remarks>
    /// This is a regression guard for a run-killing interaction, not tidiness: the hook is a real
    /// <c>.js</c> file that a real run puts there, and it belongs to no layer by construction —
    /// the agents are forbidden from writing outside <c>src/&lt;layer&gt;/</c>, which is exactly
    /// why nobody could fix a diagnostic reported in it.
    /// </remarks>
    [Theory]
    [InlineData(".claude/hooks/restrict-to-layer.js")]
    [InlineData("src/Frontend/node_modules/typescript/lib/tsc.js")]
    [InlineData("src/Api/bin/Debug/net10.0/Api.js")]
    [InlineData("src/Api/obj/Debug/generated.ts")]
    [InlineData("src/Frontend/dist/bundle.js")]
    public void Tooling_and_build_output_never_reach_the_gate(string relativePath)
    {
        Write("src/Frontend/App.tsx");
        Write(relativePath);

        var documents = NewSession().EnumerateDocuments(_workspaceRoot);

        Assert.Equal(Path.Combine(_workspaceRoot, "src", "Frontend", "App.tsx"), Assert.Single(documents));
    }

    private void Write(string relativePath)
    {
        var fullPath = Path.Combine(_workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "// content");
    }

    private TypeScriptShapedSession NewSession() => new(_workspaceRoot);

    /// <summary>
    /// A session with the TypeScript extension list and nothing else.
    /// </summary>
    /// <remarks>
    /// Enumeration is pure filesystem work on the base class, so exercising it needs no process —
    /// which is the point: golden rule 3 forbids the suite from starting a real language server,
    /// and this is the behaviour whose absence would end a run.
    /// </remarks>
    private sealed class TypeScriptShapedSession : LanguageServerSession
    {
        public TypeScriptShapedSession(string workspaceRootFullPath)
            : base(workspaceRootFullPath, TimeSpan.FromSeconds(1), traceProtocol: false, NullLogger.Instance)
        {
        }

        public override string SourceName => "typescript";

        public override IndexingState IndexingState => IndexingState.Ready;

        protected override IReadOnlyList<string> DocumentExtensions =>
            [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"];

        protected override ProcessStartInfo CreateProcessStartInfo() =>
            throw new NotSupportedException("This session never starts a process.");

        protected override string GetLanguageId(string documentFullPath) => "typescript";

        protected override Task PrepareWorkspaceAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
