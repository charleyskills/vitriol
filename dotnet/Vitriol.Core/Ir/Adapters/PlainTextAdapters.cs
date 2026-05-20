using System.Collections.Immutable;
using System.Text;
using Vitriol.Core.Collections;
using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Ir.Adapters;

/// <summary>
/// Flattens a <see cref="TextDoc"/> to a plain-text string. Mirrors
/// <c>textdoc_to_plain</c> at <c>app/core/intermediate.py:147–152</c>.
/// </summary>
public sealed class TextDocToPlainAdapter
{
    public static string Adapt(TextDoc source, IProgress<ConversionEvent>? progress = null)
    {
        StringBuilder sb = new();
        bool first = true;
        foreach (Block block in source.Blocks)
        {
            string text = TextDocToTabularAdapter.BlockToPlain(block);
            if (text.Length == 0)
            {
                continue;
            }
            if (!first)
            {
                sb.Append("\n\n");
            }
            sb.Append(text);
            first = false;
        }
        return sb.ToString();
    }
}

/// <summary>
/// Splits a plain-text string into blank-line-separated paragraphs.
/// Mirrors <c>plain_to_textdoc</c> at <c>app/core/intermediate.py:155–163</c>.
/// </summary>
public sealed class PlainToTextDocAdapter
{
    public static TextDoc Adapt(string text, IProgress<ConversionEvent>? progress = null)
    {
        ImmutableArray<Block>.Builder blocks = ImmutableArray.CreateBuilder<Block>();
        string normalized = text.ReplaceLineEndings("\n");

        foreach (string chunk in normalized.Split("\n\n"))
        {
            string trimmed = chunk.Trim('\n');
            if (trimmed.Length == 0)
            {
                continue;
            }
            blocks.Add(new Paragraph(EquatableArray.Create(new Run(trimmed))));
        }

        return new TextDoc(new EquatableArray<Block>(blocks.ToImmutable()));
    }
}
