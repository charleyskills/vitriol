using System.Buffers.Binary;
using System.Text;
using Vitriol.Core.Pipeline;

namespace Vitriol.Stone.Envelope;

/// <summary>
/// UCMSv1 plain envelope. Wire layout byte-identical to
/// <c>_build_envelope</c> / <c>_parse_envelope</c> in
/// <c>app/format_handlers/masquerade.py:465-492</c>:
///
/// <code>
/// magic       7 B    "UCMSv1\0"
/// ext_len     1 B    length of source extension (incl. leading dot), ≤ 255
/// ext_str     var    UTF-8 of source extension
/// payload_len 8 B    uint64, big-endian
/// payload     var    original source bytes
/// </code>
///
/// Optional padding after the payload is host-specific and lives outside this
/// record.
/// </summary>
public sealed record UcmsEnvelope(string Extension, ReadOnlyMemory<byte> Payload)
{
    /// <summary>Header overhead: magic (7) + ext_len (1) + payload_len (8) = 16 bytes plus the extension.</summary>
    public int HeaderSize => UcmsMagic.V1Length + 1 + Encoding.UTF8.GetByteCount(NormalizeExtension(Extension)) + 8;

    public int TotalSize => HeaderSize + Payload.Length;

    public byte[] Build()
    {
        string ext = NormalizeExtension(Extension);
        byte[] extBytes = Encoding.UTF8.GetBytes(ext);
        if (extBytes.Length > 255)
        {
            Array.Resize(ref extBytes, 255);
        }

        int totalSize = UcmsMagic.V1Length + 1 + extBytes.Length + 8 + Payload.Length;
        byte[] buffer = new byte[totalSize];
        Span<byte> span = buffer;

        UcmsMagic.V1.CopyTo(span);
        int pos = UcmsMagic.V1Length;

        span[pos++] = (byte)extBytes.Length;
        extBytes.CopyTo(span[pos..]);
        pos += extBytes.Length;

        BinaryPrimitives.WriteUInt64BigEndian(span[pos..], (ulong)Payload.Length);
        pos += 8;

        Payload.Span.CopyTo(span[pos..]);
        return buffer;
    }

    /// <summary>
    /// Finds and parses the first UCMSv1 envelope in <paramref name="blob"/>.
    /// Returns <c>false</c> when the magic isn't present or the envelope is
    /// truncated; throws nothing on bad input so the no-oracle property
    /// holds at the layer above.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> blob, out UcmsEnvelope? envelope, out string? error)
    {
        envelope = null;
        error = null;

        int idx = blob.IndexOf(UcmsMagic.V1);
        if (idx < 0)
        {
            error = "UCMSv1 magic not found.";
            return false;
        }

        int p = idx + UcmsMagic.V1Length;
        if (p >= blob.Length)
        {
            error = "Envelope truncated at ext_len.";
            return false;
        }

        int extLen = blob[p];
        p++;

        if (p + extLen > blob.Length)
        {
            error = "Envelope truncated inside extension.";
            return false;
        }
        string ext = Encoding.UTF8.GetString(blob.Slice(p, extLen));
        p += extLen;

        if (p + 8 > blob.Length)
        {
            error = "Envelope truncated at payload_len.";
            return false;
        }
        ulong payloadLen = BinaryPrimitives.ReadUInt64BigEndian(blob.Slice(p, 8));
        p += 8;

        if ((ulong)(blob.Length - p) < payloadLen)
        {
            error = $"Truncated payload (expected {payloadLen}, got {blob.Length - p}).";
            return false;
        }

        byte[] payload = blob.Slice(p, (int)payloadLen).ToArray();
        envelope = new UcmsEnvelope(NormalizeExtension(ext), payload);
        return true;
    }

    public static UcmsEnvelope Parse(ReadOnlySpan<byte> blob)
    {
        if (!TryParse(blob, out UcmsEnvelope? env, out string? error))
        {
            throw new StoneEnvelopeException(error ?? "UCMSv1 parse failed.");
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
