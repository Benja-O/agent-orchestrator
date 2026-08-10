using Orchestrator.LspServer.LanguageServers;

namespace Orchestrator.LspServer.Tests;

/// <summary>
/// The regression suite for the false red of block 4.
/// </summary>
/// <remarks>
/// A language server answers about the text it was given, not about the file on disk. Opening
/// each document once and never mentioning it again is correct for anything that reads a file a
/// single time — and that is precisely what block 2's manual verification did, which is why this
/// survived until a real review loop ran. The agent fixed the code, the gate kept reporting the
/// error it had seen the first time, and the run stopped for non-progress.
/// </remarks>
public sealed class DocumentSynchronizerTests
{
    private const string Path = "F:/run/output/src/Domain/Tarea.cs";

    [Fact]
    public void The_first_sight_of_a_document_opens_it_at_version_one()
    {
        var synchronizer = new DocumentSynchronizer();

        var decision = synchronizer.Reconcile(Path, "class Tarea { }");

        Assert.Equal(DocumentSyncAction.Open, decision.Action);
        Assert.Equal(1, decision.Version);
    }

    /// <summary>The one that was broken, stated as plainly as it can be.</summary>
    [Fact]
    public void A_file_an_agent_rewrote_is_reported_as_a_change_rather_than_ignored()
    {
        var synchronizer = new DocumentSynchronizer();
        synchronizer.Reconcile(Path, "class Tarea { int Roto => NoExiste; }");

        var decision = synchronizer.Reconcile(Path, "class Tarea { int Arreglado => 1; }");

        Assert.Equal(DocumentSyncAction.Change, decision.Action);
        Assert.Equal(2, decision.Version);
    }

    [Fact]
    public void Asking_twice_about_an_unchanged_document_says_nothing_to_the_server()
    {
        var synchronizer = new DocumentSynchronizer();
        synchronizer.Reconcile(Path, "class Tarea { }");

        var decision = synchronizer.Reconcile(Path, "class Tarea { }");

        Assert.Equal(DocumentSyncAction.Nothing, decision.Action);
    }

    /// <summary>
    /// A version that does not move is a change the server is entitled to ignore, so several
    /// iterations of the review loop over the same file have to keep climbing.
    /// </summary>
    [Fact]
    public void The_version_increases_on_every_change_across_a_whole_review_loop()
    {
        var synchronizer = new DocumentSynchronizer();

        Assert.Equal(1, synchronizer.Reconcile(Path, "one").Version);
        Assert.Equal(2, synchronizer.Reconcile(Path, "two").Version);
        Assert.Equal(2, synchronizer.Reconcile(Path, "two").Version);
        Assert.Equal(3, synchronizer.Reconcile(Path, "three").Version);
    }

    /// <summary>
    /// The range a whole-document rewrite has to declare. Roslyn rejects the range-less form of a
    /// content change by throwing inside its request queue and then going quiet for good, so this
    /// span is not a detail of formatting — it is what keeps the language server alive.
    /// </summary>
    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("abc", 0, 3)]
    [InlineData("abc\n", 1, 0)]
    [InlineData("abc\ndef", 1, 3)]
    [InlineData("abc\r\ndef", 1, 3)]
    [InlineData("a\nb\nc\n", 3, 0)]
    public void The_end_of_the_previous_text_is_where_the_replacement_range_stops(
        string text, int expectedLine, int expectedCharacter)
    {
        Assert.Equal(new TextPosition(expectedLine, expectedCharacter), DocumentSynchronizer.EndOf(text));
    }

    [Fact]
    public void A_change_declares_the_span_of_the_text_it_replaces()
    {
        var synchronizer = new DocumentSynchronizer();
        synchronizer.Reconcile(Path, "line one\nline two");

        var decision = synchronizer.Reconcile(Path, "replaced");

        Assert.Equal(DocumentSyncAction.Change, decision.Action);
        Assert.Equal(new TextPosition(1, 8), decision.EndOfPreviousText);
    }

    [Fact]
    public void Documents_are_tracked_independently_of_each_other()
    {
        var synchronizer = new DocumentSynchronizer();
        const string Other = "F:/run/output/src/Api/TareasController.cs";

        synchronizer.Reconcile(Path, "a");
        synchronizer.Reconcile(Path, "b");

        Assert.Equal(DocumentSyncAction.Open, synchronizer.Reconcile(Other, "a").Action);
    }

    /// <summary>
    /// Two spellings of one path are one document. Block 2 paid for this lesson on the wire; the
    /// same mistake here would open a second copy and answer about whichever one is staler.
    /// </summary>
    [Fact]
    public void One_document_written_two_ways_is_still_one_document()
    {
        var synchronizer = new DocumentSynchronizer();
        synchronizer.Reconcile("F:/run/output/src/Domain/Tarea.cs", "a");

        var decision = synchronizer.Reconcile("f:/run/output/src/domain/Tarea.cs", "a");

        Assert.Equal(DocumentSyncAction.Nothing, decision.Action);
    }

    /// <summary>Whitespace is not noise to a compiler, so it is not noise here either.</summary>
    [Fact]
    public void A_change_that_only_moves_lines_around_is_still_a_change()
    {
        var synchronizer = new DocumentSynchronizer();
        synchronizer.Reconcile(Path, "class Tarea { }");

        var decision = synchronizer.Reconcile(Path, "class Tarea {\n}");

        Assert.Equal(DocumentSyncAction.Change, decision.Action);
    }
}
