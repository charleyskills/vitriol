using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Vitriol.Tests.Ir;

public sealed class AdapterRoundTripTests
{
    [Fact]
    public void TabularToTextDoc_emits_heading_per_sheet()
    {
        Tabular tabular = new(EquatableArray.Create(
            new Sheet("Sales", EquatableArray.Create(
                EquatableArray.Create(new Cell("Region"), new Cell("Revenue")),
                EquatableArray.Create(new Cell("EU"), new Cell(1000m)),
                EquatableArray.Create(new Cell("US"), new Cell(2500m)))),
            new Sheet("Returns", EquatableArray.Create(
                EquatableArray.Create(new Cell("Region"), new Cell("Refunds")),
                EquatableArray.Create(new Cell("EU"), new Cell(50m))))));

        TabularToTextDocAdapter adapter = new();
        TextDoc result = adapter.Adapt(tabular);

        result.Blocks.Count.ShouldBe(4); // Heading + Table for each of 2 sheets
        result.Blocks[0].ShouldBeOfType<Heading>().Runs[0].Text.ShouldBe("Sales");
        result.Blocks[1].ShouldBeOfType<TableBlock>();
        result.Blocks[2].ShouldBeOfType<Heading>().Runs[0].Text.ShouldBe("Returns");
        result.Blocks[3].ShouldBeOfType<TableBlock>();
    }

    [Fact]
    public void TabularToTextDoc_preserves_decimal_precision_via_invariant_culture()
    {
        Tabular tabular = new(EquatableArray.Create(
            new Sheet("S", EquatableArray.Create(
                EquatableArray.Create(new Cell(0.1m + 0.2m))))));

        TextDoc result = new TabularToTextDocAdapter().Adapt(tabular);
        TableBlock table = result.Blocks[1].ShouldBeOfType<TableBlock>();
        Paragraph cellPara = table.Rows[0][0][0].ShouldBeOfType<Paragraph>();

        // 0.1m + 0.2m == 0.3m exactly under decimal semantics (no float drift).
        cellPara.Runs[0].Text.ShouldBe("0.3");
    }

    [Fact]
    public void TextDocToTabular_emits_warning_when_prose_dropped()
    {
        TextDoc doc = new(EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(new Run("prose that will be dropped"))),
            new TableBlock(EquatableArray.Create(
                EquatableArray.Create(EquatableArray.Create<Block>(
                    new Paragraph(EquatableArray.Create(new Run("kept")))))))));

        List<ConversionEvent> events = new();
        // Synchronous reporter — System.Progress<T> dispatches via SynchronizationContext,
        // which isn't pumped in xUnit, so events would never land before the assertion.
        SyncProgress<ConversionEvent> sync = new(events.Add);

        Tabular result = new TextDocToTabularAdapter().Adapt(doc, sync);

        result.Sheets.Count.ShouldBe(1);
        events.OfType<ConversionEvent.Warning>().ShouldNotBeEmpty();
        events.OfType<ConversionEvent.Warning>().First().Message
            .ShouldContain("dropped prose blocks");
    }

    [Fact]
    public void TextDocToTabular_with_no_table_returns_single_empty_sheet()
    {
        TextDoc doc = new(EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(new Run("only prose")))));

        SyncProgress<ConversionEvent> sync = new(_ => { });
        Tabular result = new TextDocToTabularAdapter().Adapt(doc, sync);

        result.Sheets.Count.ShouldBe(1);
        result.Sheets[0].Name.ShouldBe("Sheet1");
        result.Sheets[0].Rows.Count.ShouldBe(0);
    }

    [Fact]
    public void TextDocToTabular_dedupes_sheet_names()
    {
        TextDoc doc = new(EquatableArray.Create<Block>(
            new Heading(1, EquatableArray.Create(new Run("Dup"))),
            new TableBlock(EquatableArray.Create(
                EquatableArray.Create(EquatableArray.Create<Block>(
                    new Paragraph(EquatableArray.Create(new Run("a"))))))),
            new Heading(1, EquatableArray.Create(new Run("Dup"))),
            new TableBlock(EquatableArray.Create(
                EquatableArray.Create(EquatableArray.Create<Block>(
                    new Paragraph(EquatableArray.Create(new Run("b")))))))));

        Tabular result = new TextDocToTabularAdapter().Adapt(doc);

        result.Sheets.Count.ShouldBe(2);
        result.Sheets[0].Name.ShouldBe("Dup");
        result.Sheets[1].Name.ShouldNotBe("Dup");
    }

    [Fact]
    public void Plain_round_trip_collapses_extra_blank_lines()
    {
        string text = "Paragraph one.\n\nParagraph two.\n\n\n\nParagraph three.";

        TextDoc doc = PlainToTextDocAdapter.Adapt(text);
        string flat = TextDocToPlainAdapter.Adapt(doc);

        doc.Blocks.Count.ShouldBe(3);
        flat.ShouldBe("Paragraph one.\n\nParagraph two.\n\nParagraph three.");
    }

    [Fact]
    public void Tabular_then_TextDoc_then_Tabular_preserves_tabular_only_input()
    {
        Tabular original = new(EquatableArray.Create(
            new Sheet("Sheet1", EquatableArray.Create(
                EquatableArray.Create(new Cell("col1"), new Cell("col2")),
                EquatableArray.Create(new Cell("a"), new Cell("b"))))));

        TabularToTextDocAdapter forward = new();
        TextDocToTabularAdapter backward = new();

        Tabular round = backward.Adapt(forward.Adapt(original));

        round.Sheets.Count.ShouldBe(1);
        round.Sheets[0].Name.ShouldBe("Sheet1");
        round.Sheets[0].Rows.Count.ShouldBe(2);
        Cell topLeft = round.Sheets[0].Rows[0][0];
        Cell topRight = round.Sheets[0].Rows[0][1];
        topLeft.Value.ShouldBe("col1");
        topRight.Value.ShouldBe("col2");
    }

    [Property(MaxTest = 100)]
    public Property Plain_text_round_trip_preserves_paragraphs(NonEmptyArray<NonWhiteSpaceString> paragraphs)
    {
        // Build paragraphs from arbitrary non-whitespace tokens joined with blank lines.
        // ReplaceLineEndings(" ") normalises ALL Unicode line-terminators that
        // String.ReplaceLineEndings (and therefore PlainToTextDocAdapter) recognises:
        // LF, CR, CRLF, FF (\f), VT (\v), NEL (U+0085), LS (U+2028), PS (U+2029).
        // Using only Replace("\r"/"\ n") would miss form-feed and the rest, causing
        // the adapter to replace them with "\n" while 'text' still carries the original
        // character — making round != text even for correct production code.
        string[] cleanParagraphs = paragraphs.Get
            .Select(p => p.Get.ReplaceLineEndings(" ").Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        if (cleanParagraphs.Length == 0)
        {
            return true.ToProperty();
        }

        string text = string.Join("\n\n", cleanParagraphs);

        TextDoc doc = PlainToTextDocAdapter.Adapt(text);
        string round = TextDocToPlainAdapter.Adapt(doc);

        return (round == text).Label($"round=\"{round}\", text=\"{text}\"");
    }
}

/// <summary>
/// Synchronous <c>IProgress&lt;T&gt;</c> that invokes the handler on the calling
/// thread so xUnit assertions can read the events immediately. Avoids the
/// thread-pool dispatch of <c>System.Progress&lt;T&gt;</c>.
/// </summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SyncProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
