using System.Collections.Immutable;
using System.Text;
using Vitriol.Core.Collections;
using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Ir.Adapters;

/// <summary>
/// Pull <see cref="TableBlock"/>s out of a document; each becomes a sheet.
/// Mirrors <c>textdoc_to_tabular</c> at <c>app/core/intermediate.py:123–144</c>.
///
/// <para>Vitriol drops prose silently here. The .NET port emits a
/// <see cref="ConversionEvent.Warning"/> when prose blocks would be discarded
/// — addresses Section 12 of the design doc
/// (<c>docs/dotnet10-lossless-file-conversion-plan.md</c>).</para>
/// </summary>
public sealed class TextDocToTabularAdapter : IDocumentAdapter<TextDoc, Tabular>
{
    private const int ExcelSheetNameLimit = 31;

    public Tabular Adapt(TextDoc source, IProgress<ConversionEvent>? progress = null)
    {
        ImmutableArray<Sheet>.Builder sheets = ImmutableArray.CreateBuilder<Sheet>();
        string currentName = "Sheet1";
        int tableIndex = 0;
        bool droppedProse = false;

        foreach (Block block in source.Blocks)
        {
            switch (block)
            {
                case Heading heading:
                    string headingText = RunsToText(heading.Runs).Trim();
                    currentName = headingText.Length > 0 ? headingText : $"Sheet{sheets.Count + 1}";
                    break;

                case TableBlock table:
                    ImmutableArray<EquatableArray<Cell>>.Builder rows =
                        ImmutableArray.CreateBuilder<EquatableArray<Cell>>();

                    foreach (EquatableArray<EquatableArray<Block>> row in table.Rows)
                    {
                        ImmutableArray<Cell>.Builder cells = ImmutableArray.CreateBuilder<Cell>();
                        foreach (EquatableArray<Block> cell in row)
                        {
                            cells.Add(new Cell(BlocksToText(cell)));
                        }
                        rows.Add(new EquatableArray<Cell>(cells.ToImmutable()));
                    }

                    string name = string.IsNullOrEmpty(currentName)
                        ? $"Sheet{tableIndex + 1}"
                        : currentName;

                    if (sheets.Any(s => s.Name == name))
                    {
                        name = $"{name} ({tableIndex + 1})";
                    }

                    sheets.Add(new Sheet(
                        Truncate(name, ExcelSheetNameLimit),
                        new EquatableArray<EquatableArray<Cell>>(rows.ToImmutable())));

                    tableIndex++;
                    currentName = string.Empty;
                    break;

                default:
                    droppedProse = true;
                    break;
            }
        }

        if (sheets.Count == 0)
        {
            sheets.Add(new Sheet("Sheet1", EquatableArray<EquatableArray<Cell>>.Empty));
        }

        if (droppedProse)
        {
            progress?.Report(new ConversionEvent.Warning(
                "TextDoc → Tabular adapter dropped prose blocks. Only Table blocks survive; "
                + "Headings, Paragraphs, Lists, Images, and CodeBlocks were discarded."));
        }

        return new Tabular(new EquatableArray<Sheet>(sheets.ToImmutable()), source.Metadata);
    }

    internal static string RunsToText(EquatableArray<Run> runs)
    {
        StringBuilder sb = new();
        foreach (Run run in runs)
        {
            sb.Append(run.Text);
        }
        return sb.ToString();
    }

    private static string BlocksToText(EquatableArray<Block> blocks)
    {
        StringBuilder sb = new();
        bool first = true;
        foreach (Block block in blocks)
        {
            if (!first)
            {
                sb.Append(' ');
            }
            sb.Append(BlockToPlain(block));
            first = false;
        }
        return sb.ToString().Trim();
    }

    internal static string BlockToPlain(Block block) => block switch
    {
        Heading h => RunsToText(h.Runs),
        Paragraph p => RunsToText(p.Runs),
        ListBlock l => ListToText(l),
        TableBlock t => TableToText(t),
        ImageBlock i => string.IsNullOrEmpty(i.Alt) ? "[image]" : $"[image: {i.Alt}]",
        CodeBlock c => c.Text,
        HorizontalRule => "----",
        Blockquote bq => BlockquoteToText(bq),
        _ => string.Empty,
    };

    private static string ListToText(ListBlock list)
    {
        StringBuilder sb = new();
        int index = 1;
        foreach (EquatableArray<Block> item in list.Items)
        {
            string prefix = list.Ordered ? $"{index}. " : "- ";
            sb.Append(prefix);
            sb.Append(BlocksToText(item));
            sb.AppendLine();
            index++;
        }
        return sb.ToString().TrimEnd();
    }

    private static string TableToText(TableBlock table)
    {
        StringBuilder sb = new();
        foreach (EquatableArray<EquatableArray<Block>> row in table.Rows)
        {
            bool first = true;
            foreach (EquatableArray<Block> cell in row)
            {
                if (!first)
                {
                    sb.Append(" | ");
                }
                sb.Append(BlocksToText(cell));
                first = false;
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string BlockquoteToText(Blockquote bq)
    {
        StringBuilder sb = new();
        foreach (Block inner in bq.Blocks)
        {
            string text = BlockToPlain(inner);
            foreach (string line in text.Split('\n'))
            {
                sb.Append("> ");
                sb.AppendLine(line);
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
