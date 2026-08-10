using Orchestrator.Domain;

namespace Orchestrator.PipelineVerification;

/// <summary>
/// Wraps the real runner and, once, plants a compile error in the domain layer right after the
/// domain agent's first turn.
/// </summary>
/// <remarks>
/// <para>
/// This is the "error injected on purpose" of the block's exit criterion, and where it is
/// injected is the whole design. <c>CLAUDE.md</c> forbids hand-editing <c>output/</c> to make
/// the pipeline advance; this does the opposite — it makes the pipeline work harder — but the
/// reason the rule exists still applies, so the fault arrives through the same door a real one
/// would: after an agent's turn, in a file the gate is about to read, with nobody having told
/// the graph about it.
/// </para>
/// <para>
/// Seeding the broken file before the run instead would prove less: the agent might notice and
/// fix it on its first pass, and then the review loop — the one thing this harness exists to
/// demonstrate — would never run.
/// </para>
/// </remarks>
internal sealed class FaultInjectingAgentRunner : IAgentRunner
{
    private readonly IAgentRunner _inner;
    private readonly string _workspaceRoot;
    private bool _alreadyInjected;

    public FaultInjectingAgentRunner(IAgentRunner inner, string workspaceRoot)
    {
        _inner = inner;
        _workspaceRoot = workspaceRoot;
    }

    /// <summary>Relative to the workspace root, so it can be matched against what the gate reports.</summary>
    public string InjectedFilePath { get; } = "src/Domain/InjectedFault.cs";

    public async Task<AgentOutcome> RunAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        var outcome = await _inner.RunAsync(invocation, cancellationToken).ConfigureAwait(false);

        var isDomainsFirstTurn =
            string.Equals(invocation.AgentName, LayerCatalog.AgentNameOf(Layer.Domain), StringComparison.Ordinal)
            && invocation.Attempt == 1;

        if (!isDomainsFirstTurn || _alreadyInjected)
        {
            return outcome;
        }

        _alreadyInjected = true;

        var absolutePath = Path.Combine(_workspaceRoot, InjectedFilePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        // A plain unresolved symbol: it produces one unambiguous CS error, it is obviously wrong
        // to whoever has to fix it, and it cannot accidentally be valid.
        File.WriteAllText(absolutePath, """
            namespace Domain;

            public static class InjectedFault
            {
                public static string Describe() => EstaClaseNoExiste.Descripcion;
            }
            """);

        Console.WriteLine();
        Console.WriteLine($"  [fault injected] {InjectedFilePath} now references a type that does not exist.");
        Console.WriteLine();

        return outcome;
    }
}
