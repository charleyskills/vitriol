using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;
using Vitriol.Core.Pipeline;
using Vitriol.Stone.Carriers;
using Vitriol.Stone.Envelope;

namespace Vitriol.Stone.Hosts;

/// <summary>
/// PNG Stone host. Routes between two carrier variants:
/// <list type="bullet">
/// <item><b>v1</b> (UCMSv1, private <c>ucMs</c> chunk on a 1×1 RGBA PNG) —
/// the simple "byte envelope inside a placeholder PNG" form for
/// same-category passwordless conversions. Mirrors <c>_png_embed</c> at
/// <c>app/format_handlers/masquerade.py:935-971</c>.</item>
/// <item><b>v3</b> (Sprint 10 — UCMSv3 encrypted envelope scatter-packed
/// into Mandelbrot fractal LSBs) — the headline visual carrier. Mirrors
/// <c>_build_mandelbrot_image</c> at <c>masquerade.py:3283-3332</c>.</item>
/// </list>
///
/// <para><b>Routing rule</b> (mirrors Python <c>_png_embed</c> callers):
/// v3 fires when <see cref="StoneOptions.CrossCategory"/> is set or a
/// password is supplied; otherwise v1.</para>
///
/// <para><b>Extract</b>: probes for the <c>ucMs</c> chunk first; if not
/// found, falls back to v3 (decode pixels, unpack LSBs, parse UCMSv3).
/// Wrong-password v3 extract returns garbled bytes (no oracle).</para>
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
        // First pass: look for the v1 ucMs chunk.
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

        // No v1 chunk → check for v3 (Mandelbrot LSB scatter pack). Decode the
        // PNG, unpack the first 8 envelope bytes, and look for the UCMSv3 magic.
        // This is slower but the only reliable signal for v3 carriers.
        source.Position = 0;
        try
        {
            MandelbrotPngCodec.Decoded decoded =
                await MandelbrotPngCodec.ReadRgbAsync(source, cancellationToken).ConfigureAwait(false);
            long totalPixelBytes = (long)decoded.Width * decoded.Height * 3;
            byte[] prefix = MandelbrotBitPack.Unpack(
                decoded.PixelsRgb,
                maxEnvelopeBytes: UcmsMagic.V8Length,
                totalPixelBytes: totalPixelBytes);
            return prefix.Length >= UcmsMagic.V8Length
                && prefix.AsSpan(0, UcmsMagic.V8Length).SequenceEqual(UcmsMagic.V3);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public ValueTask EmbedAsync(
        ReadOnlyMemory<byte> sourceBytes,
        string sourceExtension,
        Stream destination,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        // Python masquerade.py: v3 fires for cross-category sources or when a
        // password is supplied. Otherwise v1 (ucMs chunk on a 1×1 placeholder).
        bool useV3 = options.CrossCategory || !options.Password.IsEmpty;
        return useV3
            ? EmbedV3Async(sourceBytes, sourceExtension, destination, options, cancellationToken)
            : EmbedV1Async(sourceBytes, sourceExtension, destination, cancellationToken);
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

        // First pass: look for a v1 ucMs chunk.
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

        // No v1 chunk → fall back to v3 (decode pixels and unpack LSBs).
        source.Position = 0;
        return await ExtractV3Async(source, options, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EmbedV1Async(
        ReadOnlyMemory<byte> sourceBytes,
        string sourceExtension,
        Stream destination,
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

    private async ValueTask EmbedV3Async(
        ReadOnlyMemory<byte> sourceBytes,
        string sourceExtension,
        Stream destination,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        // 1. Dry-run envelope at (1, 1) to size the carrier — the inner
        //    plaintext determines the encrypted length, and the header is
        //    fixed at 36 bytes regardless of W/H.
        UcmsV3Envelope sizingEnv = new(1, 1, sourceExtension, sourceBytes);
        byte[] sizingBytes = sizingEnv.Build(options.Password);
        (int width, int height) = MandelbrotDims.ForEnvelope(sizingBytes.Length);

        // 2. Real envelope with the correct W/H baked in.
        UcmsV3Envelope envelope = new(width, height, sourceExtension, sourceBytes);
        byte[] envBytes = envelope.Build(options.Password);

        // 3. Mandelbrot seed: SHA-256 of envelope[:64KB], wrapped with
        //    salt + W + H by MandelbrotSeed.Derive.
        int seedSliceLen = Math.Min(envBytes.Length, 64 * 1024);
        Span<byte> seedHash = stackalloc byte[32];
        SHA256.HashData(envBytes.AsSpan(0, seedSliceLen), seedHash);
        MandelbrotSeed.Result seed = MandelbrotSeed.Derive(width, height, seedHash);

        cancellationToken.ThrowIfCancellationRequested();

        // 4. Render fractal pixels (W·H·3 RGB bytes).
        byte[] pixels = MandelbrotGenerator.Generate(width, height, seed);

        // 5. Scatter-pack envelope into pixel LSBs (in-place).
        MandelbrotBitPack.Pack(envBytes, pixels);

        cancellationToken.ThrowIfCancellationRequested();

        // 6. Emit a valid PNG.
        await MandelbrotPngCodec.WriteRgbAsync(destination, width, height, pixels, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<StoneExtractionResult> ExtractV3Async(
        Stream source,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        MandelbrotPngCodec.Decoded decoded =
            await MandelbrotPngCodec.ReadRgbAsync(source, cancellationToken).ConfigureAwait(false);

        long totalPixelBytes = (long)decoded.Width * decoded.Height * 3;
        int maxEnv = (int)Math.Min(totalPixelBytes / MandelbrotBitPack.PixelBytesPerEnvelopeByte, int.MaxValue);
        byte[] envBytes = MandelbrotBitPack.Unpack(decoded.PixelsRgb, maxEnv);

        UcmsV3Envelope envelope = UcmsV3Envelope.Parse(envBytes, options.Password);
        return new StoneExtractionResult(envelope.Payload, envelope.Extension);
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
