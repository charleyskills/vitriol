using System.Collections.Immutable;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;
using Vitriol.Core.Collections;
using Vitriol.Core.Ir;
using Vitriol.Core.Pipeline;
using Vitriol.Stone.Envelope;
// QuestPDF.Infrastructure also exports IDocument; alias to avoid ambiguity.
using IDocument = Vitriol.Core.Ir.IDocument;

namespace Vitriol.Formats.Doc;

/// <summary>
/// PDF reader+writer. Reads via UglyToad.PdfPig (Apache 2.0), writes via
/// QuestPDF (Community license — free for individuals and companies under
/// $1M revenue; set at DI time). Mirrors
/// <c>app/format_handlers/pdf_read.py</c> + <c>pdf_write.py</c> at the level
/// the .NET libraries make easy; defers DejaVu Sans embedding and real
/// PDF image embedding to a later sprint.
///
/// <para><b>Trailer envelope</b>: write-side appends the UCMSv1 envelope
/// after <c>%%EOF</c> when <see cref="DocumentMetadata.Origin"/> is set.
/// Read-side detection lives in
/// <see cref="Trailer.PdfTrailerEnvelopeReader"/> and the router's
/// <c>TrailerEnvelopeGate</c>; the handler's read path runs only when the
/// gate didn't short-circuit.</para>
///
/// <para><b>Embedded fonts</b>: QuestPDF's Lato TTF files are embedded in
/// this assembly (see Vitriol.Formats.Doc.csproj) so that single-file
/// published binaries need no <c>LatoFont/</c> directory alongside them.
/// <see cref="EnsureFontsRegistered"/> loads them once on the first write.</para>
/// </summary>
public sealed class PdfHandler : IFormatReader, IFormatWriter
{
    // ── embedded-font registration ────────────────────────────────────────
    private static volatile bool s_fontsRegistered;
    private static readonly Lock s_fontLock = new();

    /// <summary>
    /// Loads every *.ttf embedded in this assembly into QuestPDF's
    /// <see cref="FontManager"/> so that no external LatoFont/ directory is
    /// required at runtime.  Called once before the first PDF write.
    /// </summary>
    private static void EnsureFontsRegistered()
    {
        if (s_fontsRegistered) return;
        lock (s_fontLock)
        {
            if (s_fontsRegistered) return;
            foreach (string name in typeof(PdfHandler).Assembly.GetManifestResourceNames())
            {
                if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                    continue;
                using Stream? stream = typeof(PdfHandler).Assembly.GetManifestResourceStream(name);
                if (stream is not null)
                    FontManager.RegisterFont(stream);
            }
            s_fontsRegistered = true;
        }
    }
    public DocKind Kind => DocKind.Text;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    public async ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        // PdfPig prefers byte arrays.
        using var buf = new MemoryStream();
        await input.CopyToAsync(buf, cancellationToken).ConfigureAwait(false);
        byte[] bytes = buf.ToArray();

        try
        {
            using var pdf = PdfDocument.Open(bytes);
            ImmutableArray<Block>.Builder blocks = ImmutableArray.CreateBuilder<Block>();
            int totalChars = 0;
            int pageCount = 0;

            foreach (Page page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                pageCount++;
                string pageText = page.Text;
                totalChars += pageText.Length;

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    continue;
                }
                foreach (string chunk in SplitParagraphs(pageText))
                {
                    blocks.Add(new Paragraph(EquatableArray.Create(new Run(chunk))));
                }
            }

            // Low-extraction warning: matches Python pdf_read.py:133-145
            // behavior of flagging suspected scanned/encrypted PDFs.
            if (bytes.Length > 100 * 1024 && totalChars < 50)
            {
                context.Progress?.Report(new ConversionEvent.Warning(
                    "PDF text extraction yielded almost no text. "
                    + "This is typically a scanned (image-only) PDF or one that "
                    + "uses font encodings PdfPig can't reverse — try running OCR first."));
            }

            return new TextDoc(new EquatableArray<Block>(blocks.ToImmutable()));
        }
        catch (PdfDocumentEncryptedException e)
        {
            throw new UnsupportedConversionException(
                "Encrypted PDFs are not supported. Decrypt with Acrobat or "
                + "`qpdf --decrypt input.pdf output.pdf` first. Underlying: " + e.Message);
        }
    }

    public async ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        TextDoc doc = document switch
        {
            TextDoc t => t,
            _ => throw new InvalidOperationException(
                $"PdfHandler.Write expects a TextDoc, got {document.GetType().Name}."),
        };

        EnsureFontsRegistered();
        QuestPDF.Settings.License = LicenseType.Community;

        // Render into a memory buffer so we can append the trailer envelope.
        using MemoryStream buf = new();
        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(11));
                page.Content().Column(col =>
                {
                    foreach (Block block in doc.Blocks)
                    {
                        RenderBlock(col, block);
                    }
                });
            });
        }).GeneratePdf(buf);

        buf.Position = 0;
        await buf.CopyToAsync(output, cancellationToken).ConfigureAwait(false);

        if (doc.Metadata.Origin is VitriolOrigin origin)
        {
            // PDF readers ignore bytes after the final %%EOF, so appending the
            // envelope keeps the file valid as a PDF and recoverable via the
            // trailer-envelope reader. Mirrors pdf_write.py:580+.
            byte[] envBytes = new UcmsEnvelope(origin.Extension, origin.Bytes).Build();
            await output.WriteAsync(envBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void RenderBlock(ColumnDescriptor col, Block block)
    {
        switch (block)
        {
            case Heading h:
                col.Item().PaddingTop(h.Level == 1 ? 10 : 6)
                    .Text(t => RenderRunsAsText(t, h.Runs, isHeading: true, h.Level));
                break;
            case Paragraph p:
                col.Item().PaddingVertical(2)
                    .Text(t => RenderRunsAsText(t, p.Runs, isHeading: false, level: 0));
                break;
            case ListBlock list:
                int idx = 1;
                foreach (EquatableArray<Block> item in list.Items)
                {
                    string bullet = list.Ordered ? $"{idx}. " : "• ";
                    foreach (Block inner in item)
                    {
                        if (inner is Paragraph ip)
                        {
                            col.Item().PaddingLeft(15)
                                .Text(t =>
                                {
                                    t.Span(bullet);
                                    RenderRunsAsText(t, ip.Runs, isHeading: false, level: 0);
                                });
                        }
                        else
                        {
                            RenderBlock(col, inner);
                        }
                    }
                    idx++;
                }
                break;
            case TableBlock table:
                if (table.Rows.Count == 0)
                {
                    break;
                }
                int colCount = table.Rows[0].Count;
                col.Item().PaddingVertical(4).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        for (int i = 0; i < colCount; i++)
                        {
                            c.RelativeColumn();
                        }
                    });
                    foreach (EquatableArray<EquatableArray<Block>> row in table.Rows)
                    {
                        foreach (EquatableArray<Block> cell in row)
                        {
                            t.Cell().Border(0.5f).Padding(3).Text(text =>
                            {
                                foreach (Block c in cell)
                                {
                                    if (c is Paragraph cp)
                                    {
                                        RenderRunsAsText(text, cp.Runs, isHeading: false, level: 0);
                                    }
                                }
                            });
                        }
                    }
                });
                break;
            case CodeBlock code:
                col.Item().PaddingVertical(3).Background(Colors.Grey.Lighten4)
                    .Padding(4).Text(code.Text).FontFamily(Fonts.CourierNew);
                break;
            case HorizontalRule:
                col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                break;
            case Blockquote bq:
                foreach (Block inner in bq.Blocks)
                {
                    if (inner is Paragraph bp)
                    {
                        col.Item().PaddingLeft(12).BorderLeft(2).BorderColor(Colors.Grey.Medium)
                            .PaddingLeft(8)
                            .Text(t => RenderRunsAsText(t, bp.Runs, isHeading: false, level: 0));
                    }
                    else
                    {
                        RenderBlock(col, inner);
                    }
                }
                break;
            case ImageBlock img:
                string alt = string.IsNullOrEmpty(img.Alt) ? "[image]" : $"[image: {img.Alt}]";
                col.Item().Text(alt).FontColor(Colors.Grey.Darken1).Italic();
                break;
        }
    }

    private static void RenderRunsAsText(TextDescriptor textDesc, EquatableArray<Run> runs, bool isHeading, int level)
    {
        foreach (Run run in runs)
        {
            TextSpanDescriptor span = textDesc.Span(run.Text);
            if (isHeading)
            {
                int size = level switch
                {
                    1 => 18,
                    2 => 16,
                    3 => 14,
                    _ => 12,
                };
                span.FontSize(size).Bold();
            }
            else
            {
                if (run.Bold) { span.Bold(); }
                if (run.Italic) { span.Italic(); }
                if (run.Underline) { span.Underline(); }
                if (run.Code) { span.FontFamily(Fonts.CourierNew); }
            }
        }
    }

    private static IEnumerable<string> SplitParagraphs(string pageText)
    {
        // PdfPig page.Text often packs newlines mid-paragraph. A simple
        // heuristic: split on double newlines; otherwise the whole page is
        // one paragraph. Matches Python's per-page paragraph emission at
        // pdf_read.py.
        string normalized = pageText.Replace("\r\n", "\n").Replace("\r", "\n");
        if (normalized.Contains("\n\n"))
        {
            foreach (string chunk in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = chunk.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }
        else
        {
            string trimmed = normalized.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }
}
