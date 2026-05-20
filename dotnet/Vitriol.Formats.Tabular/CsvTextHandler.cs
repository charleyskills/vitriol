using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Vitriol.Core.Collections;
using Vitriol.Core.Ir;
using Vitriol.Core.Ir.Adapters;
using Vitriol.Core.Pipeline;
using Vitriol.Formats.Text;
// Inside namespace Vitriol.Formats.Tabular the name 'Tabular' resolves to the
// enclosing namespace itself — use a distinct alias to refer to the IR record type.
using TabularDoc = global::Vitriol.Core.Ir.Tabular;

namespace Vitriol.Formats.Tabular;

/// <summary>
/// CSV (.csv) and TSV (.tsv) reader+writer producing/consuming the
/// <see cref="Tabular"/> IR. Mirrors <c>app/format_handlers/tabular_text.py</c>.
///
/// <para><b>Multi-sheet on write</b>: only the first sheet is emitted; sheets
/// 2..N are dropped with an explicit <see cref="ConversionEvent.Warning"/>
/// rather than silently (matches Sprint 1's "no silent loss" rule).</para>
///
/// <para>CSV is untyped on the wire; the reader returns every <see cref="Cell"/>
/// with a <c>string</c> value. The writer uses the
/// <see cref="TabularToTextDocAdapter.FormatCell"/> helper from Sprint 1
/// (exposed via <c>InternalsVisibleTo</c>) so <see cref="decimal"/> precision
/// and invariant culture are preserved end-to-end.</para>
/// </summary>
public sealed class CsvTextHandler : IFormatReader, IFormatWriter
{
    public DocKind Kind => DocKind.Tabular;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csv", ".tsv" };

    public async ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        // Buffer + decode via the Sprint 5 EncodingDetector. BOM-aware, emits
        // a Warning on non-UTF-8 fallback so the user knows bytes were lost.
        using MemoryStream buffer = new();
        await input.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        EncodingDetector.DetectionResult decoded =
            EncodingDetector.Decode(buffer.ToArray(), context.Progress);

        CsvConfiguration config = new(CultureInfo.InvariantCulture)
        {
            Delimiter = DelimiterFor(context.Extension),
            HasHeaderRecord = false,
            BadDataFound = null,           // tolerate quirky quoting
            DetectColumnCountChanges = false,
            MissingFieldFound = null,
        };

        ImmutableArray<EquatableArray<Cell>>.Builder rows =
            ImmutableArray.CreateBuilder<EquatableArray<Cell>>();

        using StringReader sr = new(decoded.Text);
        using CsvParser parser = new(sr, config);
        while (await parser.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] record = parser.Record ?? Array.Empty<string>();
            ImmutableArray<Cell>.Builder cells = ImmutableArray.CreateBuilder<Cell>(record.Length);
            foreach (string field in record)
            {
                cells.Add(new Cell(field));
            }
            rows.Add(new EquatableArray<Cell>(cells.ToImmutable()));
        }

        string sheetName = SheetNameFromHint(context.SourceHint);
        Sheet sheet = new(sheetName, new EquatableArray<EquatableArray<Cell>>(rows.ToImmutable()));
        return new TabularDoc(EquatableArray.Create(sheet));
    }

    public async ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        TabularDoc tabular = document switch
        {
            TabularDoc t => t,
            _ => throw new InvalidOperationException(
                $"CsvTextHandler.Write expects a Tabular, got {document.GetType().Name}."),
        };

        if (tabular.Sheets.Count == 0)
        {
            return; // empty output for empty input
        }

        if (tabular.Sheets.Count > 1)
        {
            context.Progress?.Report(new ConversionEvent.Warning(
                $"CSV writer can only emit one sheet; {tabular.Sheets.Count - 1} "
                + "additional sheet(s) were dropped. Use .xlsx as the destination "
                + "to preserve multi-sheet workbooks."));
        }

        Sheet sheet = tabular.Sheets[0];

        CsvConfiguration config = new(CultureInfo.InvariantCulture)
        {
            Delimiter = DelimiterFor(context.Extension),
            HasHeaderRecord = false,
        };

        // Leave the underlying stream open — disposing the writer would close it.
        // Use UTF-8 without BOM: Encoding.UTF8 writes a BOM; callers reading back
        // with string(bytes) would see the U+FEFF prefix rather than the raw text.
        await using StreamWriter sw = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        await using CsvWriter writer = new(sw, config, leaveOpen: true);

        foreach (EquatableArray<Cell> row in sheet.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (Cell cell in row)
            {
                writer.WriteField(TabularToTextDocAdapter.FormatCell(cell.Value));
            }
            await writer.NextRecordAsync().ConfigureAwait(false);
        }

        await writer.FlushAsync().ConfigureAwait(false);
    }

    internal static string DelimiterFor(string extension) =>
        string.Equals(extension, ".tsv", StringComparison.OrdinalIgnoreCase) ? "\t" : ",";

    private static string SheetNameFromHint(string? hint)
    {
        if (string.IsNullOrEmpty(hint))
        {
            return "Sheet1";
        }
        string stem = Path.GetFileNameWithoutExtension(hint);
        return string.IsNullOrEmpty(stem) ? "Sheet1" : stem;
    }
}
