using Microsoft.Extensions.Options;
using Orchestrator.LspServer.Configuration;
using Orchestrator.LspServer.LanguageServers;
using Orchestrator.LspServer.Tools;
using Orchestrator.LspServer.Workspace;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<LspServerOptions>()
    .Bind(builder.Configuration.GetSection(LspServerOptions.SectionName))
    .ValidateOnStart();

// Fail fast, before a single language server is launched: discovering in the third graph node
// that a dependency of the environment is missing wastes everything spent up to there (AI.md).
var options = builder.Configuration.GetSection(LspServerOptions.SectionName).Get<LspServerOptions>()
    ?? new LspServerOptions();

if (string.IsNullOrWhiteSpace(options.WorkspaceRoot))
{
    throw new InvalidOperationException(
        "LspServer:WorkspaceRoot is required. Pass it as --LspServer:WorkspaceRoot=<path>.");
}

var workspaceRootFullPath = Path.GetFullPath(options.WorkspaceRoot);
if (!Directory.Exists(workspaceRootFullPath))
{
    throw new InvalidOperationException($"The workspace root '{workspaceRootFullPath}' does not exist.");
}

var logDirectory = string.IsNullOrWhiteSpace(options.LogDirectory)
    ? Path.Combine(Path.GetTempPath(), "orchestrator-lsp-logs")
    : Path.GetFullPath(options.LogDirectory);

builder.Services.AddSingleton(new WorkspacePaths(workspaceRootFullPath));

if (options.Roslyn.Enabled)
{
    builder.Services.AddSingleton<ILanguageServerSession>(serviceProvider =>
        new RoslynLanguageServerSession(
            workspaceRootFullPath,
            logDirectory,
            serviceProvider.GetRequiredService<IOptions<LspServerOptions>>().Value.Roslyn,
            TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            options.TraceProtocol,
            serviceProvider.GetRequiredService<ILogger<RoslynLanguageServerSession>>()));
}

if (options.TypeScript.Enabled)
{
    builder.Services.AddSingleton<ILanguageServerSession>(serviceProvider =>
        new TypeScriptLanguageServerSession(
            workspaceRootFullPath,
            serviceProvider.GetRequiredService<IOptions<LspServerOptions>>().Value.TypeScript,
            TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            options.TraceProtocol,
            serviceProvider.GetRequiredService<ILogger<TypeScriptLanguageServerSession>>()));
}

builder.Services.AddSingleton<LanguageServerRegistry>();
builder.Services.AddSingleton<ILanguageServerRegistry>(
    serviceProvider => serviceProvider.GetRequiredService<LanguageServerRegistry>());
builder.Services.AddHostedService<LanguageServerStartupService>();
builder.Services.AddSingleton<LspQueryService>();

builder.Services
    .AddMcpServer(server => server.ServerInfo = new() { Name = "lsp", Version = "1.0.0" })
    .WithHttpTransport()
    .WithToolsFromAssembly();

var application = builder.Build();

application.MapMcp("/mcp");

// Not part of the MCP contract: it is what lets the orchestrator verify at startup that the
// server is up and whether it can be trusted yet, instead of assuming it (risk R5).
application.MapGet("/health", (ILanguageServerRegistry registry) => Results.Ok(new
{
    workspaceRoot = workspaceRootFullPath,
    languageServers = registry.Sessions.Select(session => new
    {
        source = session.SourceName,
        status = session.IndexingState.IsReady ? "ready" : "indexing",
        detail = session.IndexingState.Detail,
    }),
}));

application.Logger.LogInformation(
    "LSP MCP server starting for workspace {WorkspaceRoot}; language server logs go to {LogDirectory}",
    workspaceRootFullPath,
    logDirectory);

await application.RunAsync();
