using Orchestrator.Domain;

namespace Orchestrator.Application.Graph;

/// <summary>
/// Asks the gate for a verdict, and refuses to accept a non-answer as one.
/// </summary>
/// <remarks>
/// <para>
/// The single rule this class exists to enforce: <c>indexing</c> is never approval. A language
/// server that is still loading returns an empty list, and a consumer that reads that as
/// "compiles clean" approves code that does not compile — which is worse than having no gate,
/// because the graph then advances with unjustified confidence (ADR-006, ADR-010).
/// </para>
/// <para>
/// The second rule, which block 2 paid to learn: waiting has a ceiling. A Roslyn session whose
/// <c>workspace/configuration</c> call was silently rejected answers <c>indexing</c> forever
/// and says nothing about why (ADR-013). Running out of waits produces a terminal failure
/// carrying the server's own <c>statusDetail</c>, so the run stops with a diagnosis instead of
/// hanging.
/// </para>
/// </remarks>
public sealed class GateEvaluator
{
    private readonly ILanguageServerGateway _gateway;
    private readonly IRunObserver _observer;
    private readonly TimeProvider _timeProvider;
    private readonly GraphPolicy _policy;

    public GateEvaluator(ILanguageServerGateway gateway, IRunObserver observer, TimeProvider timeProvider, GraphPolicy policy)
    {
        _gateway = gateway;
        _observer = observer;
        _timeProvider = timeProvider;
        _policy = policy;
    }

    public async Task<Result<DiagnosticSet>> EvaluateAsync(RunId runId, string scope, CancellationToken cancellationToken)
    {
        string? lastStatusDetail = null;

        for (var waitAttempt = 1; waitAttempt <= _policy.MaximumIndexWaitAttempts; waitAttempt++)
        {
            var verdict = await _gateway.GetDiagnosticsAsync(scope, cancellationToken).ConfigureAwait(false);

            if (verdict.Status == GateStatus.Ready)
            {
                return Result<DiagnosticSet>.Success(verdict.Diagnostics);
            }

            lastStatusDetail = verdict.StatusDetail;

            _observer.Observe(new GateWaitingForIndex
            {
                RunId = runId,
                Timestamp = _timeProvider.GetUtcNow(),
                Scope = scope,
                WaitAttempt = waitAttempt,
                MaximumWaitAttempts = _policy.MaximumIndexWaitAttempts,
                StatusDetail = verdict.StatusDetail,
            });

            if (waitAttempt < _policy.MaximumIndexWaitAttempts && _policy.IndexWaitDelay > TimeSpan.Zero)
            {
                await Task.Delay(_policy.IndexWaitDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        return Result<DiagnosticSet>.Failure(
            $"The gate answered 'indexing' {_policy.MaximumIndexWaitAttempts} time(s) for scope '{scope}' and never became ready. "
            + $"Last detail from the server: {lastStatusDetail ?? "none given"}.");
    }
}
