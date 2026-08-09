using Orchestrator.Domain;
using Orchestrator.TestSupport;

namespace Orchestrator.Domain.Tests;

public sealed class LayerMapTests
{
    [Theory]
    [InlineData("src/Domain/Tarea.cs", Layer.Domain)]
    [InlineData("src/Api/Controllers/TareasController.cs", Layer.Api)]
    [InlineData("src/Frontend/src/TareaList.tsx", Layer.Frontend)]
    public void Resolves_a_path_to_the_layer_that_owns_it(string path, Layer expected)
    {
        Assert.True(LayerMap.Default.TryResolve(path, out var layer));
        Assert.Equal(expected, layer);
    }

    /// <summary>
    /// The lesson block 2 paid for, applied on this side of the boundary: the moment a file
    /// identity is compared as a string, two spellings of the same path become two files.
    /// </summary>
    [Theory]
    [InlineData("src\\Domain\\Tarea.cs")]
    [InlineData("./src/Domain/Tarea.cs")]
    [InlineData("SRC/DOMAIN/Tarea.cs")]
    public void Resolves_the_same_path_however_it_is_spelled(string path)
    {
        Assert.True(LayerMap.Default.TryResolve(path, out var layer));
        Assert.Equal(Layer.Domain, layer);
    }

    [Theory]
    [InlineData("App.slnx")]
    [InlineData("src/Infrastructure/Startup.cs")]
    [InlineData("src/DomainExtras/Helper.cs")]
    public void Does_not_resolve_a_path_outside_every_layer(string path) =>
        Assert.False(LayerMap.Default.TryResolve(path, out _));

    [Fact]
    public void Groups_diagnostics_by_the_agent_that_has_to_fix_them()
    {
        var attribution = LayerMap.Default.Attribute(
        [
            Diagnostics.MissingMember("src/Api/TareasController.cs"),
            Diagnostics.Error("src/Domain/Tarea.cs", "CS0246", "The type or namespace name 'Estado' could not be found"),
            Diagnostics.Error("src/Api/Program.cs", "CS1002", "; expected"),
        ]);

        Assert.True(attribution.IsSuccess);
        Assert.Single(attribution.Value[Layer.Domain]);
        Assert.Equal(2, attribution.Value[Layer.Api].Count);
        Assert.False(attribution.Value.ContainsKey(Layer.Frontend));
    }

    /// <summary>
    /// The case that must not be silently dropped. There is no agent to send the error back
    /// to, so the run cannot honestly continue — ignoring it is the false green arriving
    /// through the back door.
    /// </summary>
    [Fact]
    public void Refuses_to_attribute_a_diagnostic_that_belongs_to_no_layer()
    {
        var attribution = LayerMap.Default.Attribute(
        [
            Diagnostics.Error("src/Domain/Tarea.cs", "CS1002", "; expected"),
            Diagnostics.Error("build/Generated.cs", "CS1002", "; expected"),
        ]);

        Assert.True(attribution.IsFailure);
        Assert.Contains("build/Generated.cs", attribution.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_an_unattributable_file_once_however_many_diagnostics_it_has()
    {
        var attribution = LayerMap.Default.Attribute(
        [
            Diagnostics.Error("App.slnx", "NU1000", "first"),
            Diagnostics.Error("App.slnx", "NU1001", "second"),
        ]);

        Assert.True(attribution.IsFailure);
        Assert.Equal(1, attribution.FailureReason.Split("App.slnx").Length - 1);
    }

    [Fact]
    public void Gives_the_gate_a_scope_per_layer() =>
        Assert.Equal("src/Api", LayerMap.Default.ScopeOf(Layer.Api));
}
