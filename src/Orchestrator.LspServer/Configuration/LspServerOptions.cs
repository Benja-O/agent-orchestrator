namespace Orchestrator.LspServer.Configuration;

/// <summary>Everything the server needs to know before it can answer a single tool call.</summary>
public sealed class LspServerOptions
{
    public const string SectionName = "LspServer";

    /// <summary>The directory the language servers analyse. Every path in the contract is relative to it.</summary>
    public string WorkspaceRoot { get; set; } = string.Empty;

    /// <summary>Where the language servers write their own logs. They refuse to start without it.</summary>
    public string LogDirectory { get; set; } = string.Empty;

    /// <summary>How many diagnostics a single response carries before it truncates by the tail.</summary>
    public int MaximumDiagnosticItems { get; set; } = 50;

    /// <summary>How many references a single response carries.</summary>
    public int MaximumReferenceItems { get; set; } = 50;

    /// <summary>How many symbols a workspace-wide search returns.</summary>
    public int MaximumSymbolItems { get; set; } = 100;

    /// <summary>Per-request budget. Exceeding it is an infrastructure failure, never an empty result.</summary>
    public int RequestTimeoutSeconds { get; set; } = 90;

    /// <summary>Logs every LSP message. Off by default; the only way to diagnose a server that goes quiet.</summary>
    public bool TraceProtocol { get; set; }

    public RoslynOptions Roslyn { get; set; } = new();

    public TypeScriptOptions TypeScript { get; set; } = new();
}

public sealed class RoslynOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Full path to <c>Microsoft.CodeAnalysis.LanguageServer.exe</c>. Left empty it is
    /// resolved from the NuGet package the build downloaded — see the csproj.
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// The <c>.sln</c> or <c>.csproj</c> to open, relative to the workspace root. Left empty
    /// the server looks for a single solution, then falls back to every project it finds.
    /// </summary>
    public string SolutionPath { get; set; } = string.Empty;

    public string LogLevel { get; set; } = "Information";
}

public sealed class TypeScriptOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>The command that starts typescript-language-server; resolved through the PATH.</summary>
    public string Command { get; set; } = "typescript-language-server";

    /// <summary>Directory holding the TypeScript project, relative to the workspace root.</summary>
    public string ProjectPath { get; set; } = string.Empty;
}
