namespace Orchestrator.PipelineVerification;

/// <summary>
/// The minimum a C# language server needs in order to have anything to load.
/// </summary>
/// <remarks>
/// <para>
/// Roslyn opens a solution, not a directory of loose files: with no project, it reports the
/// workspace as loaded and finds nothing — which is a false green wearing a different hat, since
/// an empty verdict from a server that is analysing nothing looks exactly like clean code.
/// </para>
/// <para>
/// <strong>That the orchestrator does not scaffold this itself is a real gap, and it belongs to
/// block 5.</strong> It sits here rather than in <c>GeneratedWorkspacePreparer</c> because
/// deciding the generated application's project layout is that block's job, and guessing it now
/// would be deciding it by accident.
/// </para>
/// </remarks>
internal static class CSharpSkeleton
{
    public static void Write(string workspaceRoot)
    {
        var domainDirectory = Path.Combine(workspaceRoot, "src", "Domain");
        Directory.CreateDirectory(domainDirectory);

        File.WriteAllText(Path.Combine(domainDirectory, "Domain.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(workspaceRoot, "App.slnx"), """
            <Solution>
              <Project Path="src/Domain/Domain.csproj" />
            </Solution>
            """);
    }
}
