using System.Collections.Immutable;
using ClosedXML.Excel;
using Vitriol.Core.Collections;
using Vitriol.Core.Ir;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Tabular;

/// <summary>
/// XLSX (.xlsx) reader+writer via ClosedXML. Mirrors the scope of
/// <c>app/format_handlers/xlsx_handler.py</c>: multi-sheet, typed cell values,
/// formula preservation, Excel-compliant sheet-name truncation and de-dup.
///
/// <para>Out-of-scope (matching Python): charts, conditional formatting,
/// merged cells, styles. Macro-enabled <c>.xlsm</c> is not handled either —
/// would be a separate handler with a security policy decision.</para>
/// </summary>
public sealed class XlsxHandler : IFormatReader, IFormatWriter
{
    private const int ExcelSheetNameLimit = 31;

    public DocKind Kind => DocKind.Tabular;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx" };

    public async ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        // ClosedXML wants a seekable stream; copy first if the caller handed
        // us a non-seekable one.
        Stream readable = input;
        MemoryStream? buffered = null;
        if (!input.CanSeek)
        {
            buffered = new MemoryStream();
            await input.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
            buffered.Position = 0;
            readable = buffered;
        }

        try
        {
            using XLWorkbook workbook = new(readable);

            ImmutableArray<Sheet>.Builder sheets = ImmutableArray.CreateBuilder<Sheet>();
            foreach (IXLWorksheet worksheet in workbook.Worksheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sheets.Add(ReadWorksheet(worksheet));
            }

            return new Tabular(new EquatableArray<Sheet>(sheets.ToImmutable()));
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    public ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        Tabular tabular = document switch
        {
            Tabular t => t,
            _ => throw new InvalidOperationException(
                $"XlsxHandler.Write expects a Tabular, got {document.GetType().Name}."),
        };

        using XLWorkbook workbook = new();
        HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
        int sheetIndex = 0;
        foreach (Sheet sheet in tabular.Sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string proposed = string.IsNullOrEmpty(sheet.Name) ? $"Sheet{sheetIndex + 1}" : sheet.Name;
            string name = DedupSheetName(proposed, usedNames);
            usedNames.Add(name);

            IXLWorksheet ws = workbook.Worksheets.Add(name);
            WriteWorksheet(ws, sheet);
            sheetIndex++;
        }

        if (sheetIndex == 0)
        {
            // ClosedXML refuses to save a workbook with zero sheets; emit one
            // empty placeholder so the file is still valid.
            workbook.Worksheets.Add("Sheet1");
        }

        workbook.SaveAs(output);
        context.Progress?.Report(new ConversionEvent.Progress(1.0));
        return ValueTask.CompletedTask;
    }

    private static Sheet ReadWorksheet(IXLWorksheet worksheet)
    {
        IXLRange? rangeUsed = worksheet.RangeUsed();
        if (rangeUsed is null)
        {
            // Empty sheet — no data.
            return new Sheet(worksheet.Name);
        }

        int firstRow = rangeUsed.FirstRow().RowNumber();
        int lastRow = rangeUsed.LastRow().RowNumber();
        int firstCol = rangeUsed.FirstColumn().ColumnNumber();
        int lastCol = rangeUsed.LastColumn().ColumnNumber();

        ImmutableArray<EquatableArray<Cell>>.Builder rows =
            ImmutableArray.CreateBuilder<EquatableArray<Cell>>();

        for (int r = firstRow; r <= lastRow; r++)
        {
            ImmutableArray<Cell>.Builder cells = ImmutableArray.CreateBuilder<Cell>(lastCol - firstCol + 1);
            for (int c = firstCol; c <= lastCol; c++)
            {
                IXLCell xlCell = worksheet.Cell(r, c);
                cells.Add(ConvertCell(xlCell));
            }
            rows.Add(new EquatableArray<Cell>(cells.ToImmutable()));
        }

        return new Sheet(worksheet.Name, new EquatableArray<EquatableArray<Cell>>(rows.ToImmutable()));
    }

    private static Cell ConvertCell(IXLCell xl)
    {
        string? formula = xl.HasFormula ? xl.FormulaA1 : null;

        object? value = xl.DataType switch
        {
            XLDataType.Blank => null,
            XLDataType.Boolean => xl.GetBoolean(),
            XLDataType.DateTime => xl.GetDateTime(),
            XLDataType.Text => xl.GetString(),
            XLDataType.Error => xl.GetString(),
            XLDataType.Number => NormalizeNumber(xl.GetDouble()),
            // TimeSpan and other less-common types fall back to the canonical
            // string representation. Matches Python's behavior at
            // xlsx_handler.py:192-209 which treats anything outside its
            // s/b/n/str/e set as a string.
            _ => xl.GetString(),
        };

        return new Cell(value, formula);
    }

    /// <summary>
    /// Tighten the Python-equivalent <c>int</c>-vs-<c>float</c> distinction:
    /// if the double is integral and fits in <see cref="long"/>, prefer the
    /// integer representation so adapters and round-trip tests see consistent
    /// types.
    /// </summary>
    private static object NormalizeNumber(double d)
    {
        if (double.IsFinite(d) && d == Math.Truncate(d) && d >= long.MinValue && d <= long.MaxValue)
        {
            return (long)d;
        }
        return d;
    }

    private static void WriteWorksheet(IXLWorksheet ws, Sheet sheet)
    {
        for (int r = 0; r < sheet.Rows.Count; r++)
        {
            EquatableArray<Cell> row = sheet.Rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                Cell cell = row[c];
                IXLCell xl = ws.Cell(r + 1, c + 1);
                AssignCellValue(xl, cell.Value);
                if (!string.IsNullOrEmpty(cell.Formula))
                {
                    xl.FormulaA1 = cell.Formula;
                }
            }
        }
    }

    private static void AssignCellValue(IXLCell xl, object? value)
    {
        switch (value)
        {
            case null:
                xl.Clear(XLClearOptions.Contents);
                break;
            case string s:
                xl.Value = s;
                break;
            case bool b:
                xl.Value = b;
                break;
            case DateTime dt:
                xl.Value = dt;
                break;
            case TimeSpan ts:
                // Excel stores time as fractional days. Preserves the
                // numeric semantics; the type identity is lost on read-back.
                xl.Value = ts.TotalDays;
                break;
            case double d:
                xl.Value = d;
                break;
            case float f:
                xl.Value = (double)f;
                break;
            case decimal m:
                xl.Value = (double)m;
                break;
            case int i:
                xl.Value = i;
                break;
            case long l:
                xl.Value = l;
                break;
            case short sh:
                xl.Value = sh;
                break;
            case byte by:
                xl.Value = by;
                break;
            default:
                xl.Value = value.ToString() ?? string.Empty;
                break;
        }
    }

    internal static string DedupSheetName(string proposed, HashSet<string> usedNames)
    {
        string truncated = proposed.Length > ExcelSheetNameLimit
            ? proposed[..ExcelSheetNameLimit]
            : proposed;

        if (!usedNames.Contains(truncated))
        {
            return truncated;
        }

        // Append " (N)" suffix; reserve room within the 31-char limit.
        int n = 2;
        while (true)
        {
            string suffix = $" ({n})";
            int budget = ExcelSheetNameLimit - suffix.Length;
            string baseName = truncated.Length > budget ? truncated[..budget] : truncated;
            string candidate = baseName + suffix;
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }
            n++;
        }
    }
}
