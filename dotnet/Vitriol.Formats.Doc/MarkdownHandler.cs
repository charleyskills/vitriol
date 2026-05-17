using System.Collections.Immutable;
using System.Text;
using Markdig;
using Markdig.Syntax.Inlines;
using Vitriol.Core.Collections;
using Vitriol.Core.Ir;
using Vitriol.Core.Pipeline;
using Vitriol.Formats.Doc.Bundle;
using Vitriol.Formats.Text;
using Md = Markdig.Syntax;
using MdTables = Markdig.Extensions.Tables;

namespace Vitriol.Formats.Doc;

/// <summary>
/// Markdown (.md) reader+writer. Reads via Markdig (CommonMark + GFM tables);
/// writes via a hand-rolled emitter. Mirrors
/// <c>app/format_handlers/markdown_parser.py</c>.
///
/// <para><b>Bundle-aware write</b>: if the source document contains
/// <see cref="ImageBlock"/>s with non-empty <see cref="ImageBlock.Data"/>,
/// the writer creates a nested <c>&lt;dir&gt;/&lt;stem&gt;/</c> folder and
/// places the markdown there alongside an <c>images/</c> subfolder. Reuses
/// <see cref="BundleWriter"/>.</para>
/// </summary>
public sealed class MarkdownHandler : IFormatReader, IFormatWriter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .Build();

    public DocKind Kind => DocKind.Text;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md" };

    public async ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        using MemoryStream buf = new();
        await input.CopyToAsync(buf, cancellationToken).ConfigureAwait(false);

        EncodingDetector.DetectionResult decoded =
            EncodingDetector.Decode(buf.ToArray(), context.Progress);

        Md.MarkdownDocument ast = Markdown.Parse(decoded.Text, Pipeline);
        ImmutableArray<Block>.Builder blocks = ImmutableArray.CreateBuilder<Block>();
        foreach (Md.Block mdBlock in ast)
        {
            ConvertBlock(mdBlock, blocks);
        }
        return new TextDoc(new EquatableArray<Block>(blocks.ToImmutable()));
    }

    public async ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        TextDoc doc = document switch
        {
            TextDoc t => t,
            _ => throw new InvalidOperationException(
                $"MarkdownHandler.Write expects a TextDoc, got {document.GetType().Name}."),
        };

        bool hasEmbeddedImages = HasEmbeddedImages(doc.Blocks);
        if (hasEmbeddedImages && !string.IsNullOrEmpty(context.DestinationHint))
        {
            BundleWriter.Plan plan = await BundleWriter.PrepareAsync(
                doc, context.DestinationHint, cancellationToken).ConfigureAwait(false);

            string markdown = Emit(plan.BundledDoc);
            await File.WriteAllTextAsync(plan.MarkdownPath, markdown, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            string breadcrumb = $"# Bundle written to {Path.GetRelativePath(Path.GetDirectoryName(context.DestinationHint)!, plan.MarkdownPath)}\n";
            await output.WriteAsync(Encoding.UTF8.GetBytes(breadcrumb), cancellationToken).ConfigureAwait(false);
            return;
        }

        string flat = Emit(doc);
        await output.WriteAsync(Encoding.UTF8.GetBytes(flat), cancellationToken).ConfigureAwait(false);
    }

    // -- Read: Markdig AST → IR ----------------------------------------------

    private static void ConvertBlock(Md.Block mdBlock, ImmutableArray<Block>.Builder sink)
    {
        switch (mdBlock)
        {
            case Md.HeadingBlock heading:
                sink.Add(new Heading(heading.Level, ConvertInlines(heading.Inline)));
                break;
            case Md.ParagraphBlock para:
                sink.Add(new Paragraph(ConvertInlines(para.Inline)));
                break;
            case Md.ListBlock list:
                ImmutableArray<EquatableArray<Block>>.Builder items =
                    ImmutableArray.CreateBuilder<EquatableArray<Block>>();
                foreach (Md.Block item in list)
                {
                    ImmutableArray<Block>.Builder itemBlocks = ImmutableArray.CreateBuilder<Block>();
                    if (item is Md.ListItemBlock listItem)
                    {
                        foreach (Md.Block inner in listItem)
                        {
                            ConvertBlock(inner, itemBlocks);
                        }
                    }
                    items.Add(new EquatableArray<Block>(itemBlocks.ToImmutable()));
                }
                sink.Add(new ListBlock(list.IsOrdered, new EquatableArray<EquatableArray<Block>>(items.ToImmutable())));
                break;
            case MdTables.Table table:
                ImmutableArray<EquatableArray<EquatableArray<Block>>>.Builder rows =
                    ImmutableArray.CreateBuilder<EquatableArray<EquatableArray<Block>>>();
                foreach (Md.Block row in table)
                {
                    if (row is not MdTables.TableRow tr) { continue; }
                    ImmutableArray<EquatableArray<Block>>.Builder cells =
                        ImmutableArray.CreateBuilder<EquatableArray<Block>>();
                    foreach (Md.Block cellBlock in tr)
                    {
                        if (cellBlock is not MdTables.TableCell cell) { continue; }
                        ImmutableArray<Block>.Builder cellChildren = ImmutableArray.CreateBuilder<Block>();
                        foreach (Md.Block c in cell)
                        {
                            ConvertBlock(c, cellChildren);
                        }
                        cells.Add(new EquatableArray<Block>(cellChildren.ToImmutable()));
                    }
                    rows.Add(new EquatableArray<EquatableArray<Block>>(cells.ToImmutable()));
                }
                sink.Add(new TableBlock(new EquatableArray<EquatableArray<EquatableArray<Block>>>(rows.ToImmutable())));
                break;
            case Md.FencedCodeBlock fenced:
                sink.Add(new CodeBlock(fenced.Info, fenced.Lines.ToString()));
                break;
            case Md.CodeBlock codeBlock:
                sink.Add(new CodeBlock(null, codeBlock.Lines.ToString()));
                break;
            case Md.ThematicBreakBlock:
                sink.Add(new HorizontalRule());
                break;
            case Md.QuoteBlock quote:
                ImmutableArray<Block>.Builder quoteBlocks = ImmutableArray.CreateBuilder<Block>();
                foreach (Md.Block q in quote)
                {
                    ConvertBlock(q, quoteBlocks);
                }
                sink.Add(new Blockquote(new EquatableArray<Block>(quoteBlocks.ToImmutable())));
                break;
        }
    }

    private static EquatableArray<Run> ConvertInlines(ContainerInline? inlines)
    {
        ImmutableArray<Run>.Builder runs = ImmutableArray.CreateBuilder<Run>();
        if (inlines is null)
        {
            return new EquatableArray<Run>(runs.ToImmutable());
        }

        foreach (Inline inline in inlines)
        {
            AppendInline(inline, RunStyle.None, href: null, runs);
        }
        return new EquatableArray<Run>(runs.ToImmutable());
    }

    private static void AppendInline(Inline inline, RunStyle style, string? href, ImmutableArray<Run>.Builder sink)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sink.Add(new Run(lit.Content.ToString(), style, href));
                break;
            case CodeInline code:
                sink.Add(new Run(code.Content, style | RunStyle.Code, href));
                break;
            case EmphasisInline emph:
                RunStyle childStyle = style | (emph.DelimiterCount >= 2 ? RunStyle.Bold : RunStyle.Italic);
                foreach (Inline child in emph)
                {
                    AppendInline(child, childStyle, href, sink);
                }
                break;
            case LinkInline link:
                if (link.IsImage)
                {
                    string alt = ExtractText(link);
                    sink.Add(new Run($"[image: {alt}]({link.Url ?? string.Empty})", style, href));
                }
                else
                {
                    string linkHref = link.Url ?? string.Empty;
                    foreach (Inline child in link)
                    {
                        AppendInline(child, style, linkHref, sink);
                    }
                }
                break;
            case AutolinkInline autolink:
                sink.Add(new Run(autolink.Url, style, autolink.Url));
                break;
            case LineBreakInline lb:
                sink.Add(new Run(lb.IsHard ? "\n" : " ", style, href));
                break;
            case HtmlInline html:
                sink.Add(new Run(html.Tag, style, href));
                break;
            case ContainerInline container:
                foreach (Inline child in container)
                {
                    AppendInline(child, style, href, sink);
                }
                break;
        }
    }

    private static string ExtractText(ContainerInline container)
    {
        StringBuilder sb = new();
        foreach (Inline child in container)
        {
            if (child is LiteralInline lit)
            {
                sb.Append(lit.Content.ToString());
            }
            else if (child is ContainerInline cc)
            {
                sb.Append(ExtractText(cc));
            }
        }
        return sb.ToString();
    }

    // -- Write: IR → Markdown string -----------------------------------------

    private static string Emit(TextDoc doc)
    {
        StringBuilder sb = new();
        for (int i = 0; i < doc.Blocks.Count; i++)
        {
            EmitBlock(doc.Blocks[i], sb, depth: 0);
            if (i + 1 < doc.Blocks.Count)
            {
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    private static void EmitBlock(Block block, StringBuilder sb, int depth)
    {
        string indent = depth > 0 ? new string(' ', depth * 2) : string.Empty;
        switch (block)
        {
            case Heading h:
                sb.Append(indent);
                sb.Append('#', Math.Clamp(h.Level, 1, 6));
                sb.Append(' ');
                EmitRuns(h.Runs, sb);
                sb.Append('\n');
                break;
            case Paragraph p:
                sb.Append(indent);
                EmitRuns(p.Runs, sb);
                sb.Append('\n');
                break;
            case ListBlock list:
                int index = 1;
                foreach (EquatableArray<Block> item in list.Items)
                {
                    string bullet = list.Ordered ? $"{index}. " : "- ";
                    sb.Append(indent);
                    sb.Append(bullet);
                    for (int j = 0; j < item.Count; j++)
                    {
                        if (j == 0 && item[j] is Paragraph para)
                        {
                            EmitRuns(para.Runs, sb);
                            sb.Append('\n');
                        }
                        else
                        {
                            EmitBlock(item[j], sb, depth + 1);
                        }
                    }
                    index++;
                }
                break;
            case TableBlock table:
                EmitTable(table, sb);
                break;
            case CodeBlock code:
                sb.Append(indent);
                sb.Append("```");
                if (!string.IsNullOrEmpty(code.Language))
                {
                    sb.Append(code.Language);
                }
                sb.Append('\n');
                foreach (string line in code.Text.Split('\n'))
                {
                    sb.Append(indent);
                    sb.Append(line);
                    sb.Append('\n');
                }
                sb.Append(indent);
                sb.Append("```\n");
                break;
            case HorizontalRule:
                sb.Append(indent);
                sb.Append("---\n");
                break;
            case Blockquote bq:
                foreach (Block inner in bq.Blocks)
                {
                    StringBuilder qBuf = new();
                    EmitBlock(inner, qBuf, depth: 0);
                    foreach (string line in qBuf.ToString().TrimEnd('\n').Split('\n'))
                    {
                        sb.Append(indent);
                        sb.Append("> ");
                        sb.Append(line);
                        sb.Append('\n');
                    }
                }
                break;
            case ImageBlock img:
                sb.Append(indent);
                sb.Append("![");
                sb.Append(img.Alt ?? string.Empty);
                sb.Append("](");
                sb.Append(img.Href ?? string.Empty);
                sb.Append(")\n");
                break;
        }
    }

    private static void EmitRuns(EquatableArray<Run> runs, StringBuilder sb)
    {
        foreach (Run run in runs)
        {
            EmitRun(run, sb);
        }
    }

    private static void EmitRun(Run run, StringBuilder sb)
    {
        string text = run.Text;
        if (run.Code)
        {
            sb.Append('`').Append(text).Append('`');
            return;
        }
        if (!string.IsNullOrEmpty(run.Href))
        {
            sb.Append('[').Append(text).Append("](").Append(run.Href).Append(')');
            return;
        }
        string formatted = text;
        if (run.Bold && run.Italic)
        {
            formatted = $"***{text}***";
        }
        else if (run.Bold)
        {
            formatted = $"**{text}**";
        }
        else if (run.Italic)
        {
            formatted = $"*{text}*";
        }
        sb.Append(formatted);
    }

    private static void EmitTable(TableBlock table, StringBuilder sb)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }
        int colCount = table.Rows[0].Count;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            EquatableArray<EquatableArray<Block>> row = table.Rows[r];
            sb.Append('|');
            for (int c = 0; c < colCount; c++)
            {
                sb.Append(' ');
                if (c < row.Count)
                {
                    StringBuilder cellBuf = new();
                    foreach (Block cellBlock in row[c])
                    {
                        if (cellBlock is Paragraph p)
                        {
                            EmitRuns(p.Runs, cellBuf);
                        }
                        else if (cellBlock is Heading h)
                        {
                            EmitRuns(h.Runs, cellBuf);
                        }
                    }
                    sb.Append(cellBuf.ToString().Replace("\n", " ").Replace("|", "\\|"));
                }
                sb.Append(" |");
            }
            sb.Append('\n');

            if (r == 0)
            {
                sb.Append('|');
                for (int c = 0; c < colCount; c++)
                {
                    sb.Append(" --- |");
                }
                sb.Append('\n');
            }
        }
    }

    private static bool HasEmbeddedImages(EquatableArray<Block> blocks)
    {
        foreach (Block b in blocks)
        {
            switch (b)
            {
                case ImageBlock img when !img.Data.IsEmpty:
                    return true;
                case ListBlock list:
                    foreach (EquatableArray<Block> item in list.Items)
                    {
                        if (HasEmbeddedImages(item)) { return true; }
                    }
                    break;
                case TableBlock table:
                    foreach (EquatableArray<EquatableArray<Block>> row in table.Rows)
                    {
                        foreach (EquatableArray<Block> cell in row)
                        {
                            if (HasEmbeddedImages(cell)) { return true; }
                        }
                    }
                    break;
                case Blockquote bq:
                    if (HasEmbeddedImages(bq.Blocks)) { return true; }
                    break;
            }
        }
        return false;
    }
}
