using Vitriol.Core.Collections;

namespace Vitriol.Core.Ir;

/// <summary>
/// Prose/document IR: an ordered sequence of <see cref="Block"/> with
/// document-level metadata. Mirrors <c>TextDoc</c> in
/// <c>app/core/intermediate.py:77–80</c>.
/// </summary>
public sealed record TextDoc(EquatableArray<Block> Blocks, DocumentMetadata Metadata) : IDocument
{
    public static TextDoc Empty { get; } = new(EquatableArray<Block>.Empty, DocumentMetadata.Empty);

    public TextDoc(EquatableArray<Block> blocks) : this(blocks, DocumentMetadata.Empty) { }
}
