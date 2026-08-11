namespace Orchestrator.Agents;

/// <summary>
/// Resolves a command name to its full path by walking the <c>PATH</c>, the way a shell would.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This exists because of a failure mode that is specific to batch-file shims, and it is
/// worth writing down because the symptom points nowhere near the cause.</strong> npm ships on
/// Windows as <c>npm.cmd</c>, which derives everything it needs from <c>%~dp0</c> — the directory
/// of its own <c>argv[0]</c>. Started by bare name, <c>argv[0]</c> carries no directory, so
/// <c>%~dp0</c> expands to the <em>caller's working directory</em> and npm goes looking for its
/// own internals inside the project it was asked to install into:
/// </para>
/// <code>
/// Cannot find module '…\output\src\Frontend\node_modules\npm\bin\npm-cli.js'
/// </code>
/// <para>
/// Which reads like a corrupt install and is nothing of the sort. Handing the shim its full path
/// makes <c>%~dp0</c> right again.
/// </para>
/// <para>
/// The <c>.exe</c> tools this project launches — <c>claude</c>, <c>node</c>, <c>dotnet</c> — never
/// had the problem, which is exactly why it took until block 5 to appear.
/// </para>
/// </remarks>
public static class ExecutableLocator
{
    /// <summary>
    /// The full path of <paramref name="command"/>, or the command unchanged when it cannot be
    /// found.
    /// </summary>
    /// <remarks>
    /// Returning the name unchanged rather than throwing is deliberate: the caller launches it
    /// anyway, and the error from a failed launch names the command a person actually typed. A
    /// "not on the PATH" exception raised here would be a second, less informative report of the
    /// same fact.
    /// </remarks>
    public static string Resolve(string command)
    {
        if (Path.IsPathRooted(command))
        {
            return command;
        }

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in directories)
        {
            string candidate;

            try
            {
                candidate = Path.Combine(directory, command);
            }
            catch (ArgumentException)
            {
                // A PATH entry with invalid characters. Skip it rather than fail the run over
                // somebody else's environment.
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return command;
    }
}
