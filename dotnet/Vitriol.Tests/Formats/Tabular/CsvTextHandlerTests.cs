using System.Text;
using Vitriol.Core.Pipeline;
using Vitriol.Formats.Tabular;

namespace Vitriol.Tests.Formats.TabularHandlers;

public sealed class CsvTextHandlerTests
{
    private readonly CsvTextHandler _handler = new();

    [Fact]
    public async Task Reads_simple_csv_into_single_sheet()
    {
        string csv = "name,score\nAlice,42\nBob,99\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(csv));

        IDocument doc = await _handler.ReadAsync(src, new ReadContext(".csv"), default);
        Tabular t = doc.ShouldBeOfType<Tabular>();
        t.Sheets.Count.ShouldBe(1);
        t.Sheets[0].Name.ShouldBe("Sheet1");
        t.Sheets[0].Rows.Count.ShouldBe(3);
        t.Sheets[0].Rows[0][0].Value.ShouldBe("name");
        t.Sheets[0].Rows[1][1].Value.ShouldBe("42");
        t.Sheets[0].Rows[2][0].Value.ShouldBe("Bob");
    }

    [Fact]
    public async Task Source_hint_drives_sheet_name()
    {
        string csv = "a,b\n1,2\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(csv));

        IDocument doc = await _handler.ReadAsync(src,
            new ReadContext(".csv") { SourceHint = "/tmp/data/sales-report.csv" }, default);
        Tabular t = (Tabular)doc;
        t.Sheets[0].Name.ShouldBe("sales-report");
    }

    [Fact]
    public async Task Tsv_uses_tab_delimiter()
    {
        string tsv = "col1\tcol2\nfoo\tbar\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(tsv));

        Tabular t = (Tabular)await _handler.ReadAsync(src, new ReadContext(".tsv"), default);
        t.Sheets[0].Rows[0][0].Value.ShouldBe("col1");
        t.Sheets[0].Rows[0][1].Value.ShouldBe("col2");
        t.Sheets[0].Rows[1][1].Value.ShouldBe("bar");
    }

    [Fact]
    public async Task Quoting_and_embedded_commas_round_trip()
    {
        string csv = "field1,field2\n\"hello, world\",\"line\nbreak\"\n\"with \"\"quote\"\"\",plain\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(csv));

        Tabular t = (Tabular)await _handler.ReadAsync(src, new ReadContext(".csv"), default);
        t.Sheets[0].Rows[1][0].Value.ShouldBe("hello, world");
        t.Sheets[0].Rows[1][1].Value.ShouldBe("line\nbreak");
        t.Sheets[0].Rows[2][0].Value.ShouldBe("with \"quote\"");
    }

    [Fact]
    public async Task Utf8_bom_is_stripped_before_parsing()
    {
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("a,b\n1,2\n")).ToArray();
        await using MemoryStream src = new(bytes);

        Tabular t = (Tabular)await _handler.ReadAsync(src, new ReadContext(".csv"), default);
        t.Sheets[0].Rows[0][0].Value.ShouldBe("a");
    }

    [Fact]
    public async Task Writes_single_sheet_csv_without_warning()
    {
        Tabular tabular = new(EquatableArray.Create(
            new Sheet("S1", EquatableArray.Create(
                EquatableArray.Create(new Cell("name"), new Cell("score")),
                EquatableArray.Create(new Cell("Alice"), new Cell(42))))));

        List<ConversionEvent> events = new();
        WriteContext ctx = new(".csv") { Progress = new SyncProgress(events.Add) };

        await using MemoryStream dst = new();
        await _handler.WriteAsync(tabular, dst, ctx, default);
        string text = Encoding.UTF8.GetString(dst.ToArray());

        events.OfType<ConversionEvent.Warning>().ShouldBeEmpty();
        text.ShouldContain("name,score");
        text.ShouldContain("Alice,42");
    }

    [Fact]
    public async Task Multi_sheet_write_drops_extra_sheets_with_explicit_warning()
    {
        Tabular tabular = new(EquatableArray.Create(
            new Sheet("Sheet1", EquatableArray.Create(
                EquatableArray.Create(new Cell("kept1"), new Cell("kept2")))),
            new Sheet("Sheet2", EquatableArray.Create(
                EquatableArray.Create(new Cell("dropped1"))))));

        List<ConversionEvent> events = new();
        WriteContext ctx = new(".csv") { Progress = new SyncProgress(events.Add) };

        await using MemoryStream dst = new();
        await _handler.WriteAsync(tabular, dst, ctx, default);

        string text = Encoding.UTF8.GetString(dst.ToArray());
        text.ShouldContain("kept1");
        text.ShouldNotContain("dropped1");

        events.OfType<ConversionEvent.Warning>().ShouldNotBeEmpty();
        events.OfType<ConversionEvent.Warning>().First().Message
            .ShouldContain("can only emit one sheet");
    }

    [Fact]
    public async Task Decimal_precision_preserved_via_invariant_culture()
    {
        Tabular tabular = new(EquatableArray.Create(
            new Sheet("S", EquatableArray.Create(
                EquatableArray.Create(new Cell(0.1m + 0.2m))))));

        await using MemoryStream dst = new();
        await _handler.WriteAsync(tabular, dst, new WriteContext(".csv"), default);
        string text = Encoding.UTF8.GetString(dst.ToArray()).TrimEnd('\r', '\n');

        // 0.1m + 0.2m is exactly 0.3m under decimal semantics — no float drift.
        text.ShouldBe("0.3");
    }

    [Fact]
    public async Task Rejects_non_tabular_document()
    {
        await using MemoryStream dst = new();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(TextDoc.Empty, dst, new WriteContext(".csv"), default));
    }

    [Fact]
    public void Delimiter_for_extension()
    {
        CsvTextHandler.DelimiterFor(".csv").ShouldBe(",");
        CsvTextHandler.DelimiterFor(".tsv").ShouldBe("\t");
        CsvTextHandler.DelimiterFor(".CSV").ShouldBe(",");
    }

    private sealed class SyncProgress : IProgress<ConversionEvent>
    {
        private readonly Action<ConversionEvent> _handler;
        public SyncProgress(Action<ConversionEvent> h) => _handler = h;
        public void Report(ConversionEvent value) => _handler(value);
    }
}
