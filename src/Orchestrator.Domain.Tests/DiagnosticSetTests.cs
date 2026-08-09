using Orchestrator.Domain;
using Orchestrator.TestSupport;

namespace Orchestrator.Domain.Tests;

public sealed class DiagnosticSetTests
{
    private static readonly Diagnostic First = Diagnostics.MissingMember("src/Api/TareasController.cs");
    private static readonly Diagnostic Second = Diagnostics.Error("src/Api/Program.cs", "CS1002", "; expected", line: 8);

    [Fact]
    public void Two_sets_with_the_same_diagnostics_in_a_different_order_are_the_same_lack_of_progress() =>
        Assert.Equal(DiagnosticSet.Of(First, Second).Fingerprint(), DiagnosticSet.Of(Second, First).Fingerprint());

    [Fact]
    public void A_different_message_on_the_same_line_is_a_different_set() =>
        Assert.NotEqual(
            DiagnosticSet.Of(Diagnostics.MissingMember("src/Api/TareasController.cs", "Cerrar")).Fingerprint(),
            DiagnosticSet.Of(Diagnostics.MissingMember("src/Api/TareasController.cs", "Completar")).Fingerprint());

    /// <summary>
    /// The truncation case, stated rather than hidden: the visible window can be identical
    /// while the real count moved, so the count is folded into the fingerprint.
    /// </summary>
    [Fact]
    public void A_truncated_set_whose_total_moved_is_a_different_set()
    {
        var before = new DiagnosticSet { Items = [First], Total = 40, Truncated = true };
        var after = new DiagnosticSet { Items = [First], Total = 12, Truncated = true };

        Assert.NotEqual(before.Fingerprint(), after.Fingerprint());
    }

    [Fact]
    public void Only_errors_block_the_graph()
    {
        var set = DiagnosticSet.Of(Diagnostics.Warning("src/Domain/Tarea.cs", "CS0168", "unused variable"));

        Assert.False(set.HasBlockingItems);
        Assert.Empty(set.BlockingItems);
    }

    [Fact]
    public void Reports_what_an_iteration_changed()
    {
        var previous = DiagnosticSet.Of(First, Second);
        var current = DiagnosticSet.Of(Second, Diagnostics.Error("src/Api/Dtos.cs", "CS0103", "does not exist"));

        var delta = current.CompareWith(previous);

        Assert.Equal(1, delta.Resolved);
        Assert.Equal(1, delta.Introduced);
        Assert.Equal(1, delta.Persisting);
        Assert.False(delta.IsUnchanged);
    }

    [Fact]
    public void An_iteration_that_changed_nothing_says_so() =>
        Assert.True(DiagnosticSet.Of(First).CompareWith(DiagnosticSet.Of(First)).IsUnchanged);
}
