using Vitriol.Core.Collections;

namespace Vitriol.Core.Ir;

/// <summary>
/// Marker base for all block-level IR types. Mirrors the union
/// <c>Block = Union[Heading, Paragraph, List_, Table, Image, CodeBlock, HorizontalRule, Blockquote]</c>
/// in <c>app/core/intermediate.py:74</c>.
/// </summary>
public abstract record Block;

public sealed record Heading(int Level, EquatableArray<Run> Runs) : Block;

public sealed record Paragraph(EquatableArray<Run> Runs) : Block;

/// <summary>
/// Ordered/bulleted list. Each item is a sequence of blocks (nesting supported).
/// </summary>
public sealed record ListBlock(bool Ordered, EquatableArray<EquatableArray<Block>> Items) : Block;

/// <summary>
/// A table. <c>Rows[r][c]</c> is the sequence of blocks inside cell (r, c).
/// </summary>
public sealed record TableBlock(EquatableArray<EquatableArray<EquatableArray<Block>>> Rows) : Block;

/// <summary>
/// A raster or vector image. <c>Href</c> is the source reference used by the
/// bundle-aware Markdown round-trip (<c>app/format_handlers/markdown_parser.py</c>).
/// </summary>
public sealed record ImageBlock(
    ReadOnlyMemory<byte> Data,
    string Mime,
    string? Alt = null,
    int? Width = null,
    int? Height = null,
    string? Href = null) : Block
{
    public bool Equals(ImageBlock? other)
    {
        if (other is null)
        {
            return false;
        }
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        return Mime == other.Mime
            && Alt == other.Alt
            && Width == other.Width
            && Height == other.Height
            && Href == other.Href
            && Data.Span.SequenceEqual(other.Data.Span);
    }

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Mime);
        hash.Add(Alt);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(Href);
        hash.AddBytes(Data.Span);
        return hash.ToHashCode();
    }
}

public sealed record CodeBlock(string? Language, string Text) : Block;

public sealed record HorizontalRule : Block;

public sealed record Blockquote(EquatableArray<Block> Blocks) : Block;
