using Vitriol.Core.Pipeline;
using Vitriol.Formats.Tabular;

namespace Vitriol.Tests.Formats.Tabular;

public sealed class XlsxHandlerTests
{
    private readonly XlsxHandler _handler = new();

    [Fact]
    public async Task Round_trip_multi_sheet_preserves_names_values_and_types()
    {
        DateTime when = new(2024, 1, 15, 12, 30, 0, DateTimeKind.Utc);

        Tabular original = new(EquatableArray.Create(
            new Sheet("Numbers", EquatableArray.Create(
                EquatableArray.Create(new Cell("int"), new Cell("float"), new Cell("bool")),
                EquatableArray.Create(new Cell(42L), new Cell(3.14), new Cell(true)))),
            new Sheet("Dates", EquatableArray.Create(
                EquatableArray.Create(new Cell("when")),
                EquatableArray.Create(new Cell(when))))));

        await using MemoryStream buffer = new();
        await _handler.WriteAsync(original, buffer, new WriteContext(".xlsx"), default);

        buffer.Position = 0;
        Tabular round = (Tabular)await _handler.ReadAsync(buffer, new ReadContext(".xlsx"), default);

        round.Sheets.Count.ShouldBe(2);
        round.Sheets[0].Name.ShouldBe("Numbers");
        round.Sheets[1].Name.ShouldBe("Dates");

        // Numbers sheet — 2 rows × 3 cols.
        round.Sheets[0].Rows[0][0].Value.ShouldBe("int");
        round.Sheets[0].Rows[1][0].Value.ShouldBe(42L);
        round.Sheets[0].Rows[1][1].Value.ShouldBe(3.14);
        round.Sheets[0].Rows[1][2].Value.ShouldBe(true);

        // DateTime survives — ClosedXML stores as Excel serial; ConvertCell rehydrates.
        round.Sheets[1].Rows[1][0].Value.ShouldBeOfType<DateTime>();
    }

    [Fact]
    public async Task Formula_round_trips_via_cell_formula_property()
    {
        Tabular original = new(EquatableArray.Create(
            new Sheet("Calc", EquatableArray.Create(
                EquatableArray.Create(new Cell(2L), new Cell(3L), new Cell(null, "A1+B1"))))));

        await using MemoryStream buffer = new();
        await _handler.WriteAsync(original, buffer, new WriteContext(".xlsx"), default);

        buffer.Position = 0;
        Tabular round = (Tabular)await _handler.ReadAsync(buffer, new ReadContext(".xlsx"), default);

        Cell formulaCell = round.Sheets[0].Rows[0][2];
        formulaCell.Formula.ShouldNotBeNull();
        formulaCell.Formula!.Replace(" ", string.Empty).ShouldBe("A1+B1");
    }

    [Fact]
    public async Task Empty_tabular_still_produces_valid_xlsx()
    {
        Tabular empty = new(EquatableArray<Sheet>.Empty);

        await using MemoryStream buffer = new();
        await _handler.WriteAsync(empty, buffer, new WriteContext(".xlsx"), default);

        buffer.Position = 0;
        Tabular round = (Tabular)await _handler.ReadAsync(buffer, new ReadContext(".xlsx"), default);
        round.Sheets.Count.ShouldBe(1); // placeholder sheet
    }

    [Fact]
    public async Task Rejects_non_tabular_document()
    {
        await using MemoryStream dst = new();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(TextDoc.Empty, dst, new WriteContext(".xlsx"), default));
    }

    [Fact]
    public void Sheet_name_de_dup_truncates_to_31_chars_and_suffixes_collisions()
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);

        string a = XlsxHandler.DedupSheetName("Short", used);
        a.ShouldBe("Short");
        used.Add(a);

        string b = XlsxHandler.DedupSheetName("Short", used);
        b.ShouldBe("Short (2)");
        used.Add(b);

        string c = XlsxHandler.DedupSheetName("Short", used);
        c.ShouldBe("Short (3)");
        used.Add(c);
    }

    [Fact]
    public void Sheet_name_truncated_to_31_chars()
    {
        string longName = new('a', 50);
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        string truncated = XlsxHandler.DedupSheetName(longName, used);
        truncated.Length.ShouldBe(31);
        truncated.ShouldBe(new string('a', 31));
    }

    [Fact]
    public void Sheet_name_truncated_collision_keeps_suffix_within_31_chars()
    {
        string longName = new('a', 50);
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase) { new('a', 31) };

        string second = XlsxHandler.DedupSheetName(longName, used);
        second.Length.ShouldBeLessThanOrEqualTo(31);
        second.ShouldEndWith(" (2)");
    }
}
