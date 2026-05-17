using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using Vitriol.Core.Pipeline;
using Vitriol.Stone.Envelope;

namespace Vitriol.Stone.Hosts;

/// <summary>
/// PNG host (UCMSv1, "ucMs" private chunk). Builds a real 1×1 transparent
/// RGBA PNG with the envelope tucked into a private ancillary chunk. Mirrors
/// <c>_png_embed</c> / <c>_png_extract</c> in
/// <c>app/format_handlers/masquerade.py:935-971</c>.
///
/// <para>The UCMSv3 Mandelbrot-XOR variant (which renders a deterministic
/// fractal carrier and bit-packs the encrypted payload into the LSBs) is
/// substantially more complex and is delivered in a follow-up commit. v1
/// PNGs from Vitriol's <c>samples/</c> still round-trip byte-identically
/// through this host.</para>
/// </summary>
public sealed class PngStoneHost : IStoneHost
{
    private static ReadOnlySpan<byte> PngSignature =>
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

    private static ReadOnlySpan<byte> TagIhdr => "IHDR"u8;
    private static ReadOnlySpan<byte> TagIdat => "IDAT"u8;
    private static ReadOnlySpan<byte> TagIend => "IEND"u8;
    private static ReadOnlySpan<byte> TagUcMs => "ucMs"u8;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png" };

    public bool CanEmbed => true;

    public bool CanExtract => true;

    public async ValueTask<bool> HasEnvelopeAsync(Stream source, string extension, CancellationToken cancellationToken)
    {
        if (!await CheckPngSignatureAsync(source, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        await foreach ((string tag, _, _) in ReadChunkHeadersAsync(source, cancellationToken).ConfigureAwait(false))
        {
            if (tag == "ucMs")
            {
                return true;
            }
            if (tag == "IEND")
            {
                break;
            }
        }
        return false;
    }

    public async ValueTask EmbedAsync(
        ReadOnlyMemory<byte> sourceBytes,
        string sourceExtension,
        Stream destination,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        UcmsEnvelope envelope = new(sourceExtension, sourceBytes);
        byte[] envBytes = envelope.Build();

        await destination.WriteAsync(PngSignature.ToArray(), cancellationToken).ConfigureAwait(false);

        // IHDR: 1×1 RGBA, bit-depth 8, color type 6, no compression/filter/interlace.
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 1);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 1);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // color type RGBA
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter
        ihdr[12] = 0;  // interlace
        await WriteChunkAsync(destination, TagIhdr, ihdr, cancellationToken).ConfigureAwait(false);

        // IDAT: one transparent RGBA pixel (filter byte 0 + RGBA 00 00 00 00), zlib-compressed.
        byte[] pixel = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 };
        byte[] compressed = ZlibCompress(pixel);
        await WriteChunkAsync(destination, TagIdat, compressed, cancellationToken).ConfigureAwait(false);

        // Private ucMs chunk carrying the envelope.
        await WriteChunkAsync(destination, TagUcMs, envBytes, cancellationToken).ConfigureAwait(false);

        // IEND chunk (zero data).
        await WriteChunkAsync(destination, TagIend, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<StoneExtractionResult> ExtractAsync(
        Stream source,
        string sourceExtension,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        if (!await CheckPngSignatureAsync(source, cancellationToken).ConfigureAwait(false))
        {
            throw new StoneEnvelopeException("Not a PNG file.");
        }

        await foreach ((string tag, int dataLength, long dataStart) in
            ReadChunkHeadersAsync(source, cancellationToken).ConfigureAwait(false))
        {
            if (tag == "ucMs")
            {
                byte[] envBytes = new byte[dataLength];
                source.Position = dataStart;
                int total = 0;
                while (total < dataLength)
                {
                    int n = await source.ReadAsync(envBytes.AsMemory(total), cancellationToken).ConfigureAwait(false);
                    if (n == 0)
                    {
                        break;
                    }
                    total += n;
                }
                UcmsEnvelope envelope = UcmsEnvelope.Parse(envBytes);
                return new StoneExtractionResult(envelope.Payload, envelope.Extension);
            }
            if (tag == "IEND")
            {
                break;
            }
            // Skip to next chunk (data + 4-byte CRC follow the header).
            source.Position = dataStart + dataLength + 4;
        }
        throw new StoneEnvelopeException("PNG has no ucMs chunk.");
    }

    private static async ValueTask<bool> CheckPngSignatureAsync(Stream source, CancellationToken cancellationToken)
    {
        if (!source.CanSeek)
        {
            throw new InvalidOperationException("PngStoneHost requires a seekable stream.");
        }
        source.Position = 0;
        byte[] sig = new byte[8];
        int read = 0;
        while (read < sig.Length)
        {
            int n = await source.ReadAsync(sig.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                return false;
            }
            read += n;
        }
        return sig.AsSpan().SequenceEqual(PngSignature);
    }

    private static async IAsyncEnumerable<(string Tag, int DataLength, long DataStart)> ReadChunkHeadersAsync(
        Stream source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        byte[] header = new byte[8];
        while (source.Position + 8 <= source.Length)
        {
            int read = 0;
            while (read < header.Length)
            {
                int n = await source.ReadAsync(header.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (n == 0)
                {
                    yield break;
                }
                read += n;
            }
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
            string tag = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            long dataStart = source.Position;
            yield return (tag, length, dataStart);

            if (source.Position == dataStart)
            {
                // Caller didn't reposition — skip data + CRC.
                source.Position = dataStart + length + 4;
            }
        }
    }

    private static async ValueTask WriteChunkAsync(
        Stream destination,
        ReadOnlyMemory<byte> tag,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        await destination.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(data, cancellationToken).ConfigureAwait(false);

        // CRC32 over tag + data.
        Crc32 crc = new();
        crc.Append(tag.Span);
        crc.Append(data.Span);
        byte[] crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc.GetCurrentHashAsUInt32());
        await destination.WriteAsync(crcBytes, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] ZlibCompress(ReadOnlySpan<byte> data)
    {
        using MemoryStream ms = new();
        using (ZLibStream zlib = new(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }
        return ms.ToArray();
    }
}
