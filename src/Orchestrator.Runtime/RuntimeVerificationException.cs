namespace Orchestrator.Runtime;

/// <summary>
/// The orchestrator's own machinery failed, as opposed to the generated application failing.
/// </summary>
/// <remarks>
/// The line AI.md draws, applied here: an application that will not start is an expected state
/// and comes back as a diagnostic the API agent can act on. A missing project directory or a
/// <c>dotnet</c> that cannot be launched is nobody's code to fix, so it is an exception.
/// </remarks>
public sealed class RuntimeVerificationException : Exception
{
    public RuntimeVerificationException(string message)
        : base(message)
    {
    }

    public RuntimeVerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
