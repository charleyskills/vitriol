using System.Collections.Immutable;
using System.Globalization;
using Vitriol.Core.Collections;
using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Ir.Adapters;

/// <summary>
/// Each sheet becomes a Heading + a Table block. Mirrors
/// <c>tabular_to_textdoc</c> at <c>app/core/intermediate.py:105–120</c>.
/// </summary>
public sealed class TabularToTextDocAdapter : IDocumentAdapter<Tabular, TextDoc>
{
    public TextDoc Adapt(Tabular source, IProgress<ConversionEvent>? progress = null)
    {
        ImmutableArray<Block>.Builder blocks = ImmutableArray.CreateBuilder<Block>();

        foreach (Sheet sheet in source.Sheets)
        {
            blocks.Add(new Heading(1, EquatableArray.Create(new Run(sheet.Name))));

            if (sheet.Rows.Count == 0)
            {
                continue;
            }

            ImmutableArray<EquatableArray<EquatableArray<Block>>>.Builder tableRows =
                ImmutableArray.CreateBuilder<EquatableArray<EquatableArray<Block>>>();

            foreach (EquatableArray<Cell> row in sheet.Rows)
            {
                ImmutableArray<EquatableArray<Block>>.Builder cells =
                    ImmutableArray.CreateBuilder<EquatableArray<Block>>();

                foreach (Cell cell in row)
                {
                    string text = FormatCell(cell.Value);
                    var cellBlocks = EquatableArray.Create<Block>(
                        new Paragraph(EquatableArray.Create(new Run(text))));
                    cells.Add(cellBlocks);
                }

                tableRows.Add(new EquatableArray<EquatableArray<Block>>(cells.ToImmutable()));
            }

            blocks.Add(new TableBlock(new EquatableArray<EquatableArray<EquatableArray<Block>>>(tableRows.ToImmutable())));
        }

        return new TextDoc(new EquatableArray<Block>(blocks.ToImmutable()), source.Metadata);
    }

    /// <summary>
    /// Type-aware cell formatting. Unlike Vitriol's <c>str(c.value)</c> at
    /// <c>intermediate.py:116</c>, this preserves <see cref="decimal"/>
    /// precision and uses invariant culture.
    /// <see cref="InternalsVisibleTo"/> exposes this to
    /// <c>Vitriol.Formats.Tabular</c> so the CSV writer reuses the same
    /// precision-sensitive formatting.
    /// </summary>
    internal static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double dd => dd.ToString("R", CultureInfo.InvariantCulture),
        float ff => ff.ToString("R", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
