using System.Collections.Immutable;
using Vitriol.Core.Collections;
using Vitriol.Core.Ir;

namespace Vitriol.Formats.Crypto;

/// <summary>
/// Parsed crypto material — zero or more X.509 certs, optionally one private
/// key (PKCS#8 DER), optionally one public key (SubjectPublicKeyInfo DER).
/// Mirrors the Python <c>CryptoDoc</c> dataclass at
/// <c>app/format_handlers/crypto_handler.py:33-42</c>.
///
/// <para>All key material is stored as DER bytes — re-serializing to PEM is
/// trivial; going the other way (PEM input) is what the parser does at read
/// time. Encrypted private keys are not supported; the parser surfaces a
/// clear error.</para>
/// </summary>
public sealed record CryptoDoc(
    EquatableArray<ReadOnlyByteArray> CertsDer,
    ReadOnlyByteArray? KeyDer,
    ReadOnlyByteArray? PublicKeyDer,
    DocumentMetadata Metadata) : IDocument
{
    public CryptoDoc() : this(
        EquatableArray<ReadOnlyByteArray>.Empty,
        KeyDer: null,
        PublicKeyDer: null,
        Metadata: DocumentMetadata.Empty)
    {
    }

    public bool IsEmpty =>
        CertsDer.Count == 0 && KeyDer is null && PublicKeyDer is null;
}

/// <summary>
/// Value-equal wrapper around a <c>byte[]</c> so a <see cref="CryptoDoc"/>'s
/// cert list can participate in record equality. (Plain <c>byte[]</c> uses
/// reference equality by default, which would break IR record semantics.)
/// </summary>
public sealed record ReadOnlyByteArray(ImmutableArray<byte> Bytes)
{
    public static readonly ReadOnlyByteArray Empty = new(ImmutableArray<byte>.Empty);

    public ReadOnlyByteArray(ReadOnlySpan<byte> bytes) : this(ImmutableArray.Create(bytes.ToArray()))
    {
    }

    public ReadOnlyByteArray(byte[] bytes) : this(ImmutableArray.Create(bytes))
    {
    }

    public bool Equals(ReadOnlyByteArray? other) =>
        other is not null && Bytes.AsSpan().SequenceEqual(other.Bytes.AsSpan());

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.AddBytes(Bytes.AsSpan());
        return hash.ToHashCode();
    }

    public int Length => Bytes.Length;

    public byte[] ToArray() => Bytes.ToArray();
}
