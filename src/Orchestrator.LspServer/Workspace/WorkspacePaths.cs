namespace Orchestrator.LspServer.Workspace;

/// <summary>
/// Translates between the three ways a file is named in this server: the workspace-relative
/// path the contract speaks, the absolute path the filesystem speaks, and the <c>file://</c>
/// uri the protocol speaks.
/// </summary>
/// <remarks>
/// It is also the boundary check. A tool call naming a path outside the workspace is invalid
/// input, not an empty result — see <see cref="TryResolveFullPath"/>.
/// </remarks>
public sealed class WorkspacePaths
{
    private readonly string _rootFullPath;

    public WorkspacePaths(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _rootFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
    }

    public string RootFullPath => _rootFullPath;

    /// <summary>
    /// Resolves a relative (or absolute) path against the workspace root, refusing anything
    /// that escapes it. Returns false rather than throwing so the caller decides whether an
    /// out-of-workspace path is a protocol error or a filtered-out result.
    /// </summary>
    public bool TryResolveFullPath(string relativeOrAbsolutePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_rootFullPath, relativeOrAbsolutePath));
        var trimmed = Path.TrimEndingDirectorySeparator(candidate);

        var isInsideWorkspace =
            string.Equals(trimmed, _rootFullPath, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(_rootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        if (!isInsideWorkspace)
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>The workspace-relative form the contract exposes: forward slashes, no leading separator.</summary>
    public string ToRelativePath(string fullPath)
    {
        var relative = Path.GetRelativePath(_rootFullPath, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
    }

    public string ToRelativePathFromUri(string uri) => ToRelativePath(FromUri(uri));

    public static string ToUri(string fullPath) => new Uri(fullPath).AbsoluteUri;

    /// <summary>
    /// Turns a <c>file://</c> uri back into a canonical absolute path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately does not trust <see cref="Uri.LocalPath"/> alone, because language servers
    /// do not agree on how to write a Windows path. We send
    /// <c>file:///F:/project/src/tarea.ts</c>; typescript-language-server answers about
    /// <c>file:///f%3A/project/src/tarea.ts</c> — same file, lowercased drive, colon
    /// percent-encoded.
    /// </para>
    /// <para>
    /// Comparing those two as strings says they are different files, and the damage is
    /// specific: diagnostics published for a document are filed under a key nobody looks up,
    /// so the file looks clean. That is the false green again, arriving through path
    /// normalisation instead of through timing. Everything goes through here so both spellings
    /// land on the same key.
    /// </para>
    /// </remarks>
    public static string FromUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
        {
            return uri;
        }

        var path = Uri.UnescapeDataString(parsed.AbsolutePath);

        // "/f:/project" is a rooted path with a drive letter, not a directory called "f:".
        if (path.Length > 2 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':')
        {
            path = path[1..];
        }

        return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
    }
}
