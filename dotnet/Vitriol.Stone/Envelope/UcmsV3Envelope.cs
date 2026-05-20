using System.Buffers.Binary;
using System.Text;
using Vitriol.Core.Pipeline;
using Vitriol.Stone.Crypto;

namespace Vitriol.Stone.Envelope;

/// <summary>
/// UCMSv3 encrypted Mandelbrot envelope. Wire layout byte-identical to
/// <c>_v3_envelope</c> in <c>app/format_handlers/masquerade.py:2878-2912</c>:
///
/// <code>
/// MAGIC_V3   8 B    "UCMSv3\0\0"
/// width      4 B    uint32 BE — carrier image width
/// height     4 B    uint32 BE — carrier image height
/// iv         16 B   AES-CTR IV (SIV-style: HMAC-SHA256(key, plaintext)[:16])
/// salt       4 B    reserved, currently always zero
/// ciphertext var    AES-256-CTR(inner plaintext)
/// </code>
///
/// Inner plaintext:
/// <code>
/// ext_len     1 B    length of source extension
/// ext         var    UTF-8 of source extension
/// payload_len 8 B    uint64 BE
/// payload     var    source bytes
/// [pad        var    deterministically-random bytes when pad_to_inner is used]
/// </code>
///
/// <para><b>No-oracle parsing.</b> Wrong password produces garbage inner
/// bytes. Parsers must clamp ext_len/payload_len rather than throw, so the
/// failure mode is "produced file doesn't open" instead of "Stone refused the
/// password".</para>
/// </summary>
public sealed record UcmsV3Envelope(int Width, int Height, string Extension, ReadOnlyMemory<byte> Payload)
{
    public const int HeaderSize = 8 /* magic */ + 4 /* W */ + 4 /* H */ + 16 /* IV */ + 4 /* salt */;

    /// <summary>
    /// Builds the v3 envelope: serializes the inner plaintext, optionally
    /// pads it with deterministic random bytes to <paramref name="padToInner"/>,
    /// AES-256-CTR encrypts under <paramref name="password"/>, and prefixes
    /// the header. Same source + same password → byte-identical output.
    /// </summary>
    public byte[] Build(ReadOnlyMemory<byte> password, int? padToInner = null)
    {
        byte[] inner = BuildInnerPlaintext();

        if (padToInner is int target && target > inner.Length)
        {
            inner = PadInnerDeterministically(inner, password, target);
        }

        (byte[] iv, byte[] ciphertext) = StoneCrypto.Encrypt(inner, password);

        int total = HeaderSize + ciphertext.Length;
        byte[] buffer = new byte[total];
        Span<byte> span = buffer;

        UcmsMagic.V3.CopyTo(span);
        int pos = UcmsMagic.V8Length;

        BinaryPrimitives.WriteUInt32BigEndian(span[pos..], (uint)Width);
        pos += 4;
        BinaryPrimitives.WriteUInt32BigEndian(span[pos..], (uint)Height);
        pos += 4;

        iv.CopyTo(span[pos..]);
        pos += StoneCrypto.IvLength;

        // Salt field — 4 reserved bytes, currently all zero.
        pos += StoneCrypto.SaltFieldLength;

        ciphertext.CopyTo(span[pos..]);
        return buffer;
    }

    private byte[] BuildInnerPlaintext()
    {
        string ext = NormalizeExtension(Extension);
        byte[] extBytes = Encoding.UTF8.GetBytes(ext);
        if (extBytes.Length > 255)
        {
            Array.Resize(ref extBytes, 255);
        }

        byte[] inner = new byte[1 + extBytes.Length + 8 + Payload.Length];
        int pos = 0;
        inner[pos++] = (byte)extBytes.Length;
        extBytes.CopyTo(inner, pos);
        pos += extBytes.Length;
        BinaryPrimitives.WriteUInt64BigEndian(inner.AsSpan(pos), (ulong)Payload.Length);
        pos += 8;
        Payload.Span.CopyTo(inner.AsSpan(pos));
        return inner;
    }

    /// <summary>
    /// Pads the inner plaintext with deterministically-derived bytes so the
    /// resulting ciphertext fills the carrier's full capacity (closes the
    /// "trailing scatter slots have zero LSBs" forensic tell). Seeded from
    /// <c>SHA-256(inner || password || "v3-image-pad")</c> exactly as
    /// <c>masquerade.py:2902</c>.
    /// </summary>
    private static byte[] PadInnerDeterministically(byte[] inner, ReadOnlyMemory<byte> password, int target)
    {
        // Python hashes exactly `inner + password + b"v3-image-pad"` — 12 ASCII bytes, no terminator.
        ReadOnlySpan<byte> tag = "v3-image-pad"u8;
        byte[] seedInput = new byte[inner.Length + password.Length + tag.Length];
        inner.CopyTo(seedInput, 0);
        password.Span.CopyTo(seedInput.AsSpan(inner.Length));
        tag.CopyTo(seedInput.AsSpan(inner.Length + password.Length));

        byte[] padded = new byte[target];
        inner.CopyTo(padded, 0);

        // Python's random.Random seeded from the first 8 bytes of SHA-256
        // produces a specific PRNG stream. The full byte-identical match
        // with Python would require porting CPython's Mersenne Twister output
        // format — that's possible but deferred. For .NET-only round-trip
        // correctness we use a deterministic SHAKE/SHA-256 expansion: hash
        // the seed in 32-byte chunks (with a counter) and concatenate.
        // <b>Files padded by this code can be extracted by this code, but
        // padded files written by Python may not extract byte-identically.</b>
        // Unpadded envelopes (the common case) round-trip identically.
        byte[] digest = System.Security.Cryptography.SHA256.HashData(seedInput);
        int padStart = inner.Length;
        int padLen = target - inner.Length;
        Span<byte> counterBuffer = stackalloc byte[digest.Length + 4];
        digest.CopyTo(counterBuffer);

        int written = 0;
        uint counter = 0;
        Span<byte> block = stackalloc byte[32]; // moved outside loop to avoid CA2014 (stackalloc in loop)
        while (written < padLen)
        {
            BinaryPrimitives.WriteUInt32BigEndian(counterBuffer[digest.Length..], counter);
            System.Security.Cryptography.SHA256.HashData(counterBuffer, block);
            int n = Math.Min(block.Length, padLen - written);
            block[..n].CopyTo(padded.AsSpan(padStart + written));
            written += n;
            counter++;
        }
        return padded;
    }

    public static bool TryParse(
        ReadOnlySpan<byte> blob,
        ReadOnlyMemory<byte> password,
        out UcmsV3Envelope? envelope,
        out string? error)
    {
        envelope = null;
        error = null;

        if (blob.Length < HeaderSize)
        {
            error = "v3 envelope: too short for header.";
            return false;
        }
        if (!blob[..UcmsMagic.V8Length].SequenceEqual(UcmsMagic.V3))
        {
            error = "v3 envelope: magic not found.";
            return false;
        }

        int pos = UcmsMagic.V8Length;
        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(blob[pos..(pos + 4)]);
        pos += 4;
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(blob[pos..(pos + 4)]);
        pos += 4;
        ReadOnlySpan<byte> iv = blob.Slice(pos, StoneCrypto.IvLength);
        pos += StoneCrypto.IvLength;
        pos += StoneCrypto.SaltFieldLength; // skip reserved salt
        ReadOnlySpan<byte> ciphertext = blob[pos..];

        byte[] inner = StoneCrypto.Decrypt(iv, ciphertext, password);

        if (inner.Length < 1)
        {
            envelope = new UcmsV3Envelope(width, height, ".bin", ReadOnlyMemory<byte>.Empty);
            return true;
        }

        // No-oracle clamp: if the wrong password yields nonsense, return
        // *something* rather than raise.
        int extLen = inner[0];
        if (extLen > 64 || 1 + extLen + 8 > inner.Length)
        {
            envelope = new UcmsV3Envelope(width, height, ".bin", inner.AsMemory(1));
            return true;
        }

        string ext = Encoding.UTF8.GetString(inner, 1, extLen);
        if (!ext.StartsWith('.'))
        {
            ext = ext.Length > 0 ? "." + ext : ".bin";
        }

        int p = 1 + extLen;
        ulong payloadLen = BinaryPrimitives.ReadUInt64BigEndian(inner.AsSpan(p, 8));
        p += 8;

        byte[] payload;
        if ((ulong)(inner.Length - p) < payloadLen)
        {
            // Wrong-password garbage — return what we have.
            payload = inner[p..];
        }
        else
        {
            payload = inner.AsSpan(p, (int)payloadLen).ToArray();
        }
        envelope = new UcmsV3Envelope(width, height, ext, payload);
        return true;
    }

    public static UcmsV3Envelope Parse(ReadOnlySpan<byte> blob, ReadOnlyMemory<byte> password)
    {
        if (!TryParse(blob, password, out UcmsV3Envelope? env, out string? error))
        {
            throw new StoneEnvelopeException(error ?? "UCMSv3 parse failed.");
        }
        return env!;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }
        return extension.StartsWith('.') ? extension : "." + extension;
    }
}
