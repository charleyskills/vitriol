using Vitriol.Core.Pipeline;
using Vitriol.Stone.Envelope;

namespace Vitriol.Formats.Doc.Trailer;

/// <summary>
/// Scans the tail of a PDF for the Vitriol <c>UCMSv1\0</c> magic. PDF
/// readers ignore bytes after the final <c>%%EOF</c>, so Vitriol's PDF
/// writer appends the envelope after the trailer. This reader inverts
/// that: scan the last 64 KB for the magic and parse from there.
///
/// <para>Mirrors <c>_try_read_trailer_envelope</c> in
/// <c>app/format_handlers/pdf_read.py:71-115</c> (with a smaller scan
/// window — Python scans 64 MB which is wasteful; the envelope is always
/// near the end).</para>
/// </summary>
public sealed class PdfTrailerEnvelopeReader : ITrailerEnvelopeReader
{
    private const int ScanWindow = 64 * 1024;

    public bool CanRead(string sourceExtension) =>
        string.Equals(sourceExtension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public async ValueTask<TrailerEnvelopeResult?> TryReadAsync(
        Stream source,
        string sourceExtension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanSeek)
        {
            return null;
        }

        long length = source.Length;
        if (length < UcmsMagic.V1Length)
        {
            return null;
        }

        long readFrom = Math.Max(0, length - ScanWindow);
        int readLen = (int)(length - readFrom);
        source.Position = readFrom;

        byte[] tail = new byte[readLen];
        int total = 0;
        while (total < readLen)
        {
            int n = await source.ReadAsync(tail.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }
            total += n;
        }

        // Find the last occurrence of the magic; the envelope is always
        // appended at the very end, so the rightmost hit is the live one.
        ReadOnlySpan<byte> window = tail.AsSpan(0, total);
        int idx = LastIndexOf(window, UcmsMagic.V1);
        if (idx < 0)
        {
            return null;
        }

        if (!UcmsEnvelope.TryParse(window[idx..], out UcmsEnvelope? envelope, out _))
        {
            return null;
        }

        return new TrailerEnvelopeResult(envelope!.Payload, envelope.Extension);
    }

    private static int LastIndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty || haystack.Length < needle.Length)
        {
            return -1;
        }
        for (int i = haystack.Length - needle.Length; i >= 0; i--)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }
        return -1;
    }
}
