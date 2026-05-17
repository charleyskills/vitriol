using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Vitriol.Core.Collections;
using Vitriol.Core.Ir;
using Vitriol.Core.Pipeline;
using Vitriol.Stone.Envelope;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Vitriol.Formats.Doc;

/// <summary>
/// DOCX reader+writer via DocumentFormat.OpenXml. Mirrors
/// <c>app/format_handlers/docx_handler.py</c> but leans on the OpenXml SDK
/// instead of hand-parsing the ZIP+XML.
///
/// <para>Supports the trailer-envelope sidecar at
/// <c>_vitriol/original.bin</c> on both read and write. When
/// <see cref="DocumentMetadata.Origin"/> is set on a write, the handler
/// re-opens the destination as a ZIP after OpenXml closes the package and
/// appends the envelope as a STORED entry.</para>
/// </summary>
public sealed class DocxHandler : IFormatReader, IFormatWriter
{
    public const string TrailerEntryName = "_vitriol/original.bin";

    public DocKind Kind => DocKind.Text;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx" };

    public async ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        // OpenXml requires a seekable stream.
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
            using WordprocessingDocument doc = WordprocessingDocument.Open(readable, isEditable: false);
            W.Body? body = doc.MainDocumentPart?.Document.Body;
            if (body is null)
            {
                return TextDoc.Empty;
            }

            ImmutableArray<Block>.Builder blocks = ImmutableArray.CreateBuilder<Block>();
            foreach (OpenXmlElement child in body.ChildElements)
            {
                ConvertElement(child, blocks);
            }
            return new TextDoc(new EquatableArray<Block>(blocks.ToImmutable()));
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    public async ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        TextDoc doc = document switch
        {
            TextDoc t => t,
            _ => throw new InvalidOperationException(
                $"DocxHandler.Write expects a TextDoc, got {document.GetType().Name}."),
        };

        using MemoryStream buf = new();
        using (WordprocessingDocument pkg = WordprocessingDocument.Create(buf, WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = pkg.AddMainDocumentPart();
            W.Body body = new();
            foreach (Block block in doc.Blocks)
            {
                foreach (OpenXmlElement element in EmitBlock(block))
                {
                    body.AppendChild(element);
                }
            }
            mainPart.Document = new W.Document(body);
        }

        if (doc.Metadata.Origin is VitriolOrigin origin)
        {
            buf.Position = 0;
            byte[] envelopeBytes = new UcmsEnvelope(origin.Extension, origin.Bytes).Build();
            AddTrailerEntry(buf, envelopeBytes);
        }

        buf.Position = 0;
        await buf.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void AddTrailerEntry(MemoryStream package, byte[] envelopeBytes)
    {
        using ZipArchive zip = new(package, ZipArchiveMode.Update, leaveOpen: true);
        ZipArchiveEntry? existing = zip.GetEntry(TrailerEntryName);
        existing?.Delete();

        ZipArchiveEntry entry = zip.CreateEntry(TrailerEntryName, CompressionLevel.NoCompression);
        using Stream es = entry.Open();
        es.Write(envelopeBytes, 0, envelopeBytes.Length);
    }

    // -- Read: OpenXml → IR --------------------------------------------------

    private static void ConvertElement(OpenXmlElement element, ImmutableArray<Block>.Builder sink)
    {
        switch (element)
        {
            case W.Paragraph p:
                Block? converted = ConvertParagraph(p);
                if (converted is not null)
                {
                    sink.Add(converted);
                }
                break;
            case W.Table t:
                sink.Add(ConvertTable(t));
                break;
        }
    }

    private static Block? ConvertParagraph(W.Paragraph p)
    {
        string? styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        EquatableArray<Run> runs = ConvertRuns(p);

        if (styleId is not null)
        {
            int? level = HeadingLevelFromStyle(styleId);
            if (level is int lvl)
            {
                return new Heading(lvl, runs);
            }
        }

        if (runs.Count == 0)
        {
            return null;
        }

        return new Paragraph(runs);
    }

    private static int? HeadingLevelFromStyle(string styleId)
    {
        if (string.Equals(styleId, "Title", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(styleId.AsSpan(7), out int lvl)
            && lvl >= 1 && lvl <= 6)
        {
            return lvl;
        }
        return null;
    }

    private static EquatableArray<Run> ConvertRuns(W.Paragraph p)
    {
        ImmutableArray<Run>.Builder runs = ImmutableArray.CreateBuilder<Run>();
        foreach (W.Run wRun in p.Elements<W.Run>())
        {
            RunStyle style = RunStyle.None;
            W.RunProperties? rPr = wRun.RunProperties;
            if (rPr is not null)
            {
                if (rPr.Bold is not null && rPr.Bold.Val?.Value != false)
                {
                    style |= RunStyle.Bold;
                }
                if (rPr.Italic is not null && rPr.Italic.Val?.Value != false)
                {
                    style |= RunStyle.Italic;
                }
                if (rPr.Underline is not null)
                {
                    style |= RunStyle.Underline;
                }
            }

            StringBuilder textBuf = new();
            foreach (OpenXmlElement child in wRun.ChildElements)
            {
                if (child is W.Text t)
                {
                    textBuf.Append(t.Text);
                }
                else if (child is W.Break)
                {
                    textBuf.Append('\n');
                }
                else if (child is W.TabChar)
                {
                    textBuf.Append('\t');
                }
            }
            string text = textBuf.ToString();
            if (text.Length > 0)
            {
                runs.Add(new Run(text, style));
            }
        }
        return new EquatableArray<Run>(runs.ToImmutable());
    }

    private static Block ConvertTable(W.Table table)
    {
        ImmutableArray<EquatableArray<EquatableArray<Block>>>.Builder rows =
            ImmutableArray.CreateBuilder<EquatableArray<EquatableArray<Block>>>();

        foreach (W.TableRow tr in table.Elements<W.TableRow>())
        {
            ImmutableArray<EquatableArray<Block>>.Builder cells =
                ImmutableArray.CreateBuilder<EquatableArray<Block>>();

            foreach (W.TableCell tc in tr.Elements<W.TableCell>())
            {
                ImmutableArray<Block>.Builder cellBlocks = ImmutableArray.CreateBuilder<Block>();
                foreach (OpenXmlElement child in tc.ChildElements)
                {
                    ConvertElement(child, cellBlocks);
                }
                cells.Add(new EquatableArray<Block>(cellBlocks.ToImmutable()));
            }
            rows.Add(new EquatableArray<EquatableArray<Block>>(cells.ToImmutable()));
        }
        return new TableBlock(new EquatableArray<EquatableArray<EquatableArray<Block>>>(rows.ToImmutable()));
    }

    // -- Write: IR → OpenXml -------------------------------------------------

    private static IEnumerable<OpenXmlElement> EmitBlock(Block block)
    {
        switch (block)
        {
            case Heading h:
                yield return BuildParagraph(h.Runs, headingLevel: h.Level);
                break;
            case Paragraph p:
                yield return BuildParagraph(p.Runs, headingLevel: null);
                break;
            case ListBlock list:
                int index = 1;
                foreach (EquatableArray<Block> item in list.Items)
                {
                    foreach (Block inner in item)
                    {
                        if (inner is Paragraph ip)
                        {
                            string bullet = list.Ordered ? $"{index}. " : "• ";
                            ImmutableArray<Run>.Builder bulletRuns = ImmutableArray.CreateBuilder<Run>();
                            bulletRuns.Add(new Run(bullet));
                            foreach (Run r in ip.Runs)
                            {
                                bulletRuns.Add(r);
                            }
                            yield return BuildParagraph(
                                new EquatableArray<Run>(bulletRuns.ToImmutable()),
                                headingLevel: null);
                        }
                        else
                        {
                            foreach (OpenXmlElement el in EmitBlock(inner))
                            {
                                yield return el;
                            }
                        }
                    }
                    index++;
                }
                break;
            case TableBlock table:
                yield return BuildTable(table);
                break;
            case CodeBlock code:
                foreach (string line in code.Text.Split('\n'))
                {
                    yield return BuildParagraph(
                        EquatableArray.Create(new Run(line, RunStyle.Code)),
                        headingLevel: null);
                }
                break;
            case HorizontalRule:
                yield return BuildParagraph(
                    EquatableArray.Create(new Run("―――")),
                    headingLevel: null);
                break;
            case Blockquote bq:
                foreach (Block inner in bq.Blocks)
                {
                    foreach (OpenXmlElement el in EmitBlock(inner))
                    {
                        yield return el;
                    }
                }
                break;
            case ImageBlock img:
                string altText = string.IsNullOrEmpty(img.Alt) ? "[image]" : $"[image: {img.Alt}]";
                yield return BuildParagraph(
                    EquatableArray.Create(new Run(altText)),
                    headingLevel: null);
                break;
        }
    }

    private static W.Paragraph BuildParagraph(EquatableArray<Run> runs, int? headingLevel)
    {
        W.Paragraph p = new();
        if (headingLevel is int lvl)
        {
            p.ParagraphProperties = new W.ParagraphProperties(
                new W.ParagraphStyleId { Val = $"Heading{lvl}" });
        }

        foreach (Run run in runs)
        {
            W.Run wRun = new();
            W.RunProperties? rPr = null;
            if (run.Style != RunStyle.None)
            {
                rPr = new W.RunProperties();
                if (run.Bold) { rPr.Append(new W.Bold()); }
                if (run.Italic) { rPr.Append(new W.Italic()); }
                if (run.Underline)
                {
                    rPr.Append(new W.Underline { Val = W.UnderlineValues.Single });
                }
            }
            if (rPr is not null)
            {
                wRun.AppendChild(rPr);
            }
            wRun.AppendChild(new W.Text(run.Text)
            {
                Space = SpaceProcessingModeValues.Preserve,
            });
            p.AppendChild(wRun);
        }
        return p;
    }

    private static W.Table BuildTable(TableBlock table)
    {
        W.Table t = new();
        foreach (EquatableArray<EquatableArray<Block>> row in table.Rows)
        {
            W.TableRow tr = new();
            foreach (EquatableArray<Block> cell in row)
            {
                W.TableCell tc = new();
                foreach (Block inner in cell)
                {
                    foreach (OpenXmlElement el in EmitBlock(inner))
                    {
                        tc.AppendChild(el);
                    }
                }
                if (!tc.HasChildren)
                {
                    tc.AppendChild(new W.Paragraph());
                }
                tr.AppendChild(tc);
            }
            t.AppendChild(tr);
        }
        return t;
    }
}
