namespace Vitriol.Core.Ir;

/// <summary>
/// Opaque-bytes IR. Used by Stone hosts and same-handler streaming pass-through
/// where no semantic decoding is performed. <c>DocKind.Binary</c> in the
/// Python registry.
/// </summary>
public sealed record BinaryDoc(ReadOnlyMemory<byte> Bytes, string Mime, DocumentMetadata Metadata)
    : IDocument
{
    public BinaryDoc(ReadOnlyMemory<byte> bytes, string mime)
        : this(bytes, mime, DocumentMetadata.Empty) { }

    public virtual bool Equals(BinaryDoc? other)
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
            && Metadata == other.Metadata
            && Bytes.Span.SequenceEqual(other.Bytes.Span);
    }

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Mime);
        hash.Add(Metadata);
        hash.AddBytes(Bytes.Span);
        return hash.ToHashCode();
    }
}
