namespace Vitriol.Core.Ir;

/// <summary>
/// First-class sidecar carrying the original source bytes through an IR pass,
/// so reversibility-aware writers (PDF trailer, DOCX <c>_vitriol/original.bin</c>,
/// EPUB <c>META-INF/_vitriol/original.bin</c>) can stash them for byte-perfect
/// recovery. Promotes the Python <c>metadata["_vitriol_origin"]</c> pattern
/// (<c>app/core/intermediate.py:269</c>) to a typed property on
/// <see cref="DocumentMetadata"/>.
/// </summary>
public sealed record VitriolOrigin(ReadOnlyMemory<byte> Bytes, string Extension)
{
    public bool Equals(VitriolOrigin? other)
    {
        if (other is null)
        {
            return false;
        }
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        return Extension == other.Extension && Bytes.Span.SequenceEqual(other.Bytes.Span);
    }

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Extension);
        hash.AddBytes(Bytes.Span);
        return hash.ToHashCode();
    }
}
