using System.Reflection;
using Orchestrator.Application.Graph;
using Orchestrator.Domain;
using Orchestrator.TestSupport;

namespace Orchestrator.Application.Tests;

/// <summary>
/// The golden rules of AI.md as executable checks instead of good intentions.
/// </summary>
/// <remarks>
/// AI.md states each rule with a <c>grep</c> that verifies it. A grep is only run by whoever
/// remembers to run it; these fail the build.
/// </remarks>
public sealed class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(GraphState).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(GraphRunner).Assembly;

    /// <summary>Golden rule 1: the graph cannot reach Claude Code or a language server, because it has nothing to reach them with.</summary>
    [Theory]
    [InlineData("Orchestrator.Domain")]
    [InlineData("Orchestrator.Application")]
    public void The_inner_layers_reference_nothing_of_the_outside_world(string assemblyName)
    {
        var assembly = assemblyName == "Orchestrator.Domain" ? DomainAssembly : ApplicationAssembly;

        var forbidden = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Orchestrator.", StringComparison.Ordinal) && name != "Orchestrator.Domain")
            .ToList();

        Assert.Empty(forbidden);
    }

    /// <summary>
    /// Golden rule 2, from the inside: no type of the graph exposes a process anywhere in its
    /// surface. Process handling lives in the adapters, and only there.
    /// </summary>
    [Theory]
    [InlineData("Orchestrator.Domain")]
    [InlineData("Orchestrator.Application")]
    public void No_type_of_the_graph_mentions_a_process_in_its_surface(string assemblyName)
    {
        var assembly = assemblyName == "Orchestrator.Domain" ? DomainAssembly : ApplicationAssembly;

        var offenders = assembly
            .GetTypes()
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(member => (Type: type, Member: member, Signature: DescribeSignature(member))))
            .Where(entry => entry.Signature.Contains("System.Diagnostics.Process", StringComparison.Ordinal))
            .Select(entry => $"{entry.Type.Name}.{entry.Member.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Golden rule 3: everything the suite can plug into the graph is a fake. If a real
    /// adapter ever ends up on a test project's reference graph, this is what says so.
    /// </summary>
    [Fact]
    public void Every_agent_runner_and_gateway_available_to_the_suite_is_a_fake()
    {
        var implementations = new[] { DomainAssembly, ApplicationAssembly, typeof(FakeAgentRunner).Assembly, typeof(ArchitectureTests).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IAgentRunner).IsAssignableFrom(type) || typeof(ILanguageServerGateway).IsAssignableFrom(type))
            .ToList();

        Assert.NotEmpty(implementations);
        Assert.All(implementations, type => Assert.Equal("Orchestrator.TestSupport", type.Assembly.GetName().Name));
    }

    /// <summary>
    /// Golden rule 4: the clock is injected. A hard-coded <c>DateTime.UtcNow</c> would make
    /// the durations in the log and the retry behaviour untestable, so the only way the graph
    /// learns what time it is has to be a constructor parameter.
    /// </summary>
    [Fact]
    public void The_graph_takes_its_clock_from_outside() =>
        Assert.Contains(
            typeof(GraphRunner).GetConstructors().Single().GetParameters(),
            parameter => parameter.ParameterType == typeof(TimeProvider));

    private static string DescribeSignature(MemberInfo member) => member switch
    {
        MethodBase method => string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName))
            + (method is MethodInfo { ReturnType: var returnType } ? returnType.FullName : string.Empty),
        PropertyInfo property => property.PropertyType.FullName ?? string.Empty,
        FieldInfo field => field.FieldType.FullName ?? string.Empty,
        EventInfo eventInfo => eventInfo.EventHandlerType?.FullName ?? string.Empty,
        _ => string.Empty,
    };
}
