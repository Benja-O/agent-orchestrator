namespace Orchestrator.Domain;

/// <summary>
/// The outcome of something that is expected to fail sometimes: a spec that cannot be
/// decomposed, a plan that cannot be parsed, a node that ran out of attempts.
/// </summary>
/// <remarks>
/// The line AI.md draws: these are <em>states of the graph</em>, not exceptions. The runner
/// has to be able to reason about them to pick the next edge. Genuinely exceptional things —
/// <c>claude</c> missing from the PATH, a dead language server, invalid configuration — stay
/// exceptions.
/// </remarks>
public readonly record struct Result<TValue>
{
    private readonly TValue? _value;

    private Result(bool isSuccess, TValue? value, string failureReason)
    {
        IsSuccess = isSuccess;
        _value = value;
        FailureReason = failureReason;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>Empty on success.</summary>
    public string FailureReason { get; }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The result is a failure and has no value: {FailureReason}");

    public static Result<TValue> Success(TValue value) => new(true, value, string.Empty);

    public static Result<TValue> Failure(string failureReason) => new(false, default, failureReason);
}
