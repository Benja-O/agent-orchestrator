using Orchestrator.Domain;

namespace Orchestrator.TestSupport;

/// <summary>
/// The runtime gate, without an application.
/// </summary>
/// <remarks>
/// <para>
/// Reads <see cref="FakeWorkspace.RuntimeFailure"/> for the same reason
/// <see cref="FakeLanguageServer"/> reads the diagnostics: the agent and the verifier have to be
/// looking at one world, or a test can pass while describing a run that could not happen
/// (ADR-014). A scripted verifier would let a test show the loop converging after a turn in which
/// the agent changed nothing.
/// </para>
/// <para>
/// And it means the suite exercises the whole runtime gate — the merge into the API layer's
/// verdict, the attempt ceiling over it, the non-progress fingerprint — without starting a single
/// process, which is what golden rule 3 demands of a check whose real implementation runs
/// <c>dotnet run</c> and waits minutes.
/// </para>
/// </remarks>
public sealed class FakeApplicationVerifier : IApplicationVerifier
{
    private readonly FakeWorkspace _workspace;
    private readonly LayerMap _layerMap;

    public FakeApplicationVerifier(FakeWorkspace workspace, LayerMap? layerMap = null)
    {
        _workspace = workspace;
        _layerMap = layerMap ?? LayerMap.Default;
    }

    /// <summary>How many times the graph asked. A run that never asks is a run with no runtime gate.</summary>
    public int Invocations { get; private set; }

    public Task<ApplicationVerification> VerifyAsync(CancellationToken cancellationToken)
    {
        Invocations++;

        if (_workspace.DiscoverableRoutes == 0)
        {
            return Task.FromResult(ApplicationVerification.Broken(
                0,
                RuntimeDiagnostics.Failure(_layerMap, "La aplicación no publica ningún endpoint que se pueda ejercitar.")));
        }

        return Task.FromResult(_workspace.RuntimeFailure is { } reason
            ? ApplicationVerification.Broken(_workspace.DiscoverableRoutes, RuntimeDiagnostics.Failure(_layerMap, reason))
            : ApplicationVerification.Working(_workspace.DiscoverableRoutes));
    }
}
