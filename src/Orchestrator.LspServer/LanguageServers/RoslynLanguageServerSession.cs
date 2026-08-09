using System.Diagnostics;
using Orchestrator.LspServer.Configuration;
using Orchestrator.LspServer.Protocol;

namespace Orchestrator.LspServer.LanguageServers;

/// <summary>
/// The C# side: <c>Microsoft.CodeAnalysis.LanguageServer</c>, the server behind the C# Dev
/// Kit, driven over stdio (ADR-006).
/// </summary>
/// <remarks>
/// <para>
/// It is not a plain LSP server. Two things are specific to it and both matter:
/// it does not discover the solution from <c>rootUri</c> — the client has to say
/// <c>solution/open</c> — and it announces the end of the load with the custom notification
/// <c>workspace/projectInitializationComplete</c>.
/// </para>
/// <para>
/// That notification is the whole reason the <c>status</c> field of the contract can be
/// honest. Before it arrives, Roslyn answers a diagnostics pull with an empty list because it
/// has not compiled anything yet — reading that as "compiles clean" is the most expensive
/// failure available to this project. The session reports <c>indexing</c> until the
/// notification lands, and no timer is involved.
/// </para>
/// </remarks>
public sealed class RoslynLanguageServerSession : LanguageServerSession
{
    /// <summary>Baked in by the build from the NuGet package it downloaded; see the csproj.</summary>
    private const string RoslynDirectoryConfigurationKey = "Orchestrator.LspServer.RoslynLanguageServerDirectory";

    private const string ExecutableFileName = "Microsoft.CodeAnalysis.LanguageServer.exe";

    private readonly RoslynOptions _options;
    private readonly string _logDirectory;
    private readonly TaskCompletionSource _projectInitializationComplete =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string _indexingDetail = "the solution has not been opened yet";

    public RoslynLanguageServerSession(
        string workspaceRootFullPath,
        string logDirectory,
        RoslynOptions options,
        TimeSpan requestTimeout,
        bool traceProtocol,
        ILogger<RoslynLanguageServerSession> logger)
        : base(workspaceRootFullPath, requestTimeout, traceProtocol, logger)
    {
        _options = options;
        _logDirectory = logDirectory;
    }

    public override string SourceName => Contract.DiagnosticSourceNames.Roslyn;

    public override IndexingState IndexingState => _projectInitializationComplete.Task.IsCompletedSuccessfully
        ? IndexingState.Ready
        : new IndexingState(false, _indexingDetail);

    protected override IReadOnlyList<string> DocumentExtensions => [".cs"];

    protected override ProcessStartInfo CreateProcessStartInfo()
    {
        var executablePath = ResolveExecutablePath();
        Directory.CreateDirectory(_logDirectory);

        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = WorkspaceRootFullPath,
        };

        // Roslyn ships localized resources and picks them from the machine's UI language, so on
        // a Spanish Windows the diagnostics arrive in Spanish. They do not stay in the log: they
        // are pasted into the prompt of the agent that has to fix the code, next to English
        // source and English instructions. Pinning the language keeps that input in one language.
        // Invariant globalization is what actually does it: with no culture to satisfy, resource
        // lookup falls back to the neutral (English) resources compiled into the assemblies.
        // DOTNET_CLI_UI_LANGUAGE alone does not, because it only governs the host's own messages.
        startInfo.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        startInfo.ArgumentList.Add("--stdio");
        startInfo.ArgumentList.Add("--logLevel");
        startInfo.ArgumentList.Add(_options.LogLevel);
        startInfo.ArgumentList.Add("--extensionLogDirectory");
        startInfo.ArgumentList.Add(_logDirectory);

        return startInfo;
    }

    protected override string GetLanguageId(string documentFullPath) => "csharp";

    protected override LspClientEndpoint CreateClientEndpoint() =>
        new RoslynClientEndpoint(this, () =>
        {
            Logger.LogInformation("Roslyn finished loading the solution; diagnostics can be trusted from here on.");
            _projectInitializationComplete.TrySetResult();
        });

    protected override async Task PrepareWorkspaceAsync(CancellationToken cancellationToken)
    {
        var solutionFullPath = ResolveSolutionPath();

        if (solutionFullPath is not null)
        {
            _indexingDetail = $"Roslyn is loading the solution '{Path.GetFileName(solutionFullPath)}'";
            Logger.LogInformation("Opening solution {SolutionPath}", solutionFullPath);

            await NotifyAsync(LspMethodNames.RoslynOpenSolution, new
            {
                solution = new Uri(solutionFullPath).AbsoluteUri,
            }).ConfigureAwait(false);
        }
        else
        {
            var projectFullPaths = FindProjectFiles();
            if (projectFullPaths.Count == 0)
            {
                throw new LanguageServerException(
                    $"No .sln or .csproj was found under '{WorkspaceRootFullPath}', so Roslyn has nothing to analyse.");
            }

            _indexingDetail = $"Roslyn is loading {projectFullPaths.Count} project(s)";
            Logger.LogInformation("Opening {ProjectCount} project(s)", projectFullPaths.Count);

            await NotifyAsync(LspMethodNames.RoslynOpenProject, new
            {
                projects = projectFullPaths.Select(path => new Uri(path).AbsoluteUri).ToArray(),
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Blocks until Roslyn says the solution is loaded, or until the caller gives up.
    /// </summary>
    /// <returns>
    /// True when the session is trustworthy. False is not an error: it is the honest
    /// <c>indexing</c> answer, and the caller must not turn it into an empty verdict.
    /// </returns>
    public async Task<bool> WaitForIndexingAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_projectInitializationComplete.Task.IsCompletedSuccessfully)
        {
            return true;
        }

        var completed = await Task.WhenAny(
            _projectInitializationComplete.Task,
            Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);

        return completed == _projectInitializationComplete.Task;
    }

    private string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            return EnsureExists(_options.ExecutablePath, "configured in LspServer:Roslyn:ExecutablePath");
        }

        var packageDirectory = AppContext.GetData(RoslynDirectoryConfigurationKey) as string;
        if (string.IsNullOrWhiteSpace(packageDirectory))
        {
            throw new LanguageServerException(
                "The Roslyn language server path is unknown: the build did not record it and " +
                "LspServer:Roslyn:ExecutablePath is empty. Run 'dotnet restore' on Orchestrator.LspServer.");
        }

        return EnsureExists(Path.Combine(packageDirectory, ExecutableFileName), "resolved from the NuGet package");
    }

    private static string EnsureExists(string executablePath, string origin)
    {
        if (!File.Exists(executablePath))
        {
            throw new LanguageServerException(
                $"The Roslyn language server executable ({origin}) does not exist at '{executablePath}'.");
        }

        return executablePath;
    }

    private string? ResolveSolutionPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.SolutionPath))
        {
            var configured = Path.GetFullPath(Path.Combine(WorkspaceRootFullPath, _options.SolutionPath));
            if (!File.Exists(configured))
            {
                throw new LanguageServerException(
                    $"LspServer:Roslyn:SolutionPath points at '{configured}', which does not exist.");
            }

            return configured;
        }

        var solutions = Directory
            .EnumerateFiles(WorkspaceRootFullPath, "*.sln*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".sln" or ".slnx")
            .Take(2)
            .ToList();

        return solutions.Count == 1 ? solutions[0] : null;
    }

    private List<string> FindProjectFiles() =>
        Directory
            .EnumerateFiles(WorkspaceRootFullPath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
