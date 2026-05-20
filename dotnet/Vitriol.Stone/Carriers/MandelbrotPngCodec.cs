using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace Vitriol.Stone.Carriers;

/// <summary>
/// Minimal hand-rolled RGB PNG codec for the Stone v3 carrier. Writes
/// W×H 8-bit RGB images with all-Filter-0 scanlines; reads valid PNGs of
/// any bit-depth-8 RGB or RGBA produced by other writers (all five PNG
/// filter types are reversed on read).
///
/// <para>Sibling to <see cref="Vitriol.Stone.Hosts.PngStoneHost"/>'s v1
/// chunk writer; deliberately separate because the v3 path emits multi-MB
/// IDAT chunks and the v1 path emits a 1×1 placeholder + private envelope
/// chunk.</para>
/// </summary>
public static class MandelbrotPngCodec
{
    public static ReadOnlySpan<byte> Signature =>
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

    // byte[] so they can be passed to WriteChunkAsync (ReadOnlyMemory<byte> parameter,
    // incompatible with ReadOnlySpan<byte> across async boundaries).
    private static readonly byte[] TagIhdr = "IHDR"u8.ToArray();
    private static readonly byte[] TagIdat = "IDAT"u8.ToArray();
    private static readonly byte[] TagIend = "IEND"u8.ToArray();

    public sealed record Decoded(int Width, int Height, byte[] PixelsRgb);

    public static async ValueTask WriteRgbAsync(
        Stream destination,
        int width,
        int height,
        byte[] pixelsRgb,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(pixelsRgb);

        long expectedLen = (long)width * height * 3;
        if (pixelsRgb.LongLength != expectedLen)
        {
            throw new ArgumentException(
                $"Pixel buffer length {pixelsRgb.LongLength} does not match {width}×{height}×3 = {expectedLen}.",
                nameof(pixelsRgb));
        }

        await destination.WriteAsync(Signature.ToArray(), cancellationToken).ConfigureAwait(false);

        // IHDR: width, height, bit-depth 8, color-type 2 (RGB), compression 0,
        // filter 0, interlace 0.
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        await WriteChunkAsync(destination, TagIhdr, ihdr, cancellationToken).ConfigureAwait(false);

        // Build the filtered scanline buffer: 1 filter byte per row, Filter
        // 0 (None) for every row, followed by 3*width pixel bytes.
        int stride = width * 3;
        byte[] filtered = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int dstRow = y * (stride + 1);
            filtered[dstRow] = 0; // filter type = None
            Buffer.BlockCopy(pixelsRgb, y * stride, filtered, dstRow + 1, stride);
        }

        // zlib-compress as one IDAT chunk.
        byte[] compressed;
        using (MemoryStream ms = new())
        {
            using (ZLibStream z = new(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(filtered, 0, filtered.Length);
            }
            compressed = ms.ToArray();
        }
        await WriteChunkAsync(destination, TagIdat, compressed, cancellationToken).ConfigureAwait(false);

        await WriteChunkAsync(destination, TagIend, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<Decoded> ReadRgbAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Read into memory in case the caller hands us a non-seekable stream.
        using MemoryStream buffer = new();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        byte[] data = buffer.ToArray();

        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG file (signature mismatch).");
        }

        int p = 8;
        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        int filterMethod = 0;
        int interlaceMethod = 0;
        bool sawIhdr = false;

        using MemoryStream idatCompressed = new();

        while (p + 8 <= data.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p, 4));
            string tag = Encoding.ASCII.GetString(data, p + 4, 4);
            if (p + 8 + length + 4 > data.Length)
            {
                throw new InvalidDataException($"Truncated PNG chunk '{tag}'.");
            }
            ReadOnlySpan<byte> body = data.AsSpan(p + 8, length);

            switch (tag)
            {
                case "IHDR":
                    if (length != 13)
                    {
                        throw new InvalidDataException("PNG IHDR chunk has unexpected length.");
                    }
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(body[..4]);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4));
                    bitDepth = body[8];
                    colorType = body[9];
                    int compressionMethod = body[10];
                    filterMethod = body[11];
                    interlaceMethod = body[12];
                    if (bitDepth != 8 || (colorType != 2 && colorType != 6))
                    {
                        throw new NotSupportedException(
                            $"Only 8-bit RGB (color type 2) and RGBA (color type 6) PNGs are supported "
                            + $"by MandelbrotPngCodec; got bit-depth {bitDepth}, color-type {colorType}.");
                    }
                    if (compressionMethod != 0 || filterMethod != 0 || interlaceMethod != 0)
                    {
                        throw new NotSupportedException(
                            "Only compression 0, filter 0, interlace 0 are supported.");
                    }
                    sawIhdr = true;
                    break;
                case "IDAT":
                    idatCompressed.Write(body);
                    break;
                case "IEND":
                    p = data.Length; // signal end
                    break;
                default:
                    // Skip ancillary chunks (e.g. ucMs from v1, gAMA, sRGB, tEXt).
                    break;
            }

            if (tag == "IEND")
            {
                break;
            }
            p += 8 + length + 4;
        }

        if (!sawIhdr)
        {
            throw new InvalidDataException("PNG has no IHDR chunk.");
        }

        // Inflate the concatenated IDAT bytes.
        byte[] filtered;
        idatCompressed.Position = 0;
        using (MemoryStream inflated = new())
        using (ZLibStream z = new(idatCompressed, CompressionMode.Decompress))
        {
            await z.CopyToAsync(inflated, cancellationToken).ConfigureAwait(false);
            filtered = inflated.ToArray();
        }

        int bytesPerPixel = colorType == 6 ? 4 : 3;
        int rowBytes = width * bytesPerPixel;
        int expected = (rowBytes + 1) * height;
        if (filtered.Length != expected)
        {
            throw new InvalidDataException(
                $"PNG inflated stream length {filtered.Length} does not match expected {expected} for {width}×{height} {(colorType == 6 ? "RGBA" : "RGB")}.");
        }

        byte[] rawWithChannel = Unfilter(filtered, width, height, bytesPerPixel);

        // Drop the alpha channel if the source was RGBA so the caller always
        // sees a pure RGB buffer.
        byte[] rgb;
        if (colorType == 6)
        {
            rgb = new byte[width * height * 3];
            for (int i = 0, j = 0; i < rawWithChannel.Length; i += 4, j += 3)
            {
                rgb[j] = rawWithChannel[i];
                rgb[j + 1] = rawWithChannel[i + 1];
                rgb[j + 2] = rawWithChannel[i + 2];
            }
        }
        else
        {
            rgb = rawWithChannel;
        }

        return new Decoded(width, height, rgb);
    }

    /// <summary>
    /// Reverses the PNG row filter sequence. Supports all 5 filter types so
    /// PNGs produced by Python or other encoders round-trip correctly.
    /// </summary>
    private static byte[] Unfilter(byte[] filtered, int width, int height, int bpp)
    {
        int rowBytes = width * bpp;
        byte[] result = new byte[rowBytes * height];

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * (rowBytes + 1);
            int dstRow = y * rowBytes;
            byte filterType = filtered[srcRow];
            ReadOnlySpan<byte> srcRowData = filtered.AsSpan(srcRow + 1, rowBytes);
            Span<byte> dstRowData = result.AsSpan(dstRow, rowBytes);
            ReadOnlySpan<byte> prevRow = y == 0 ? ReadOnlySpan<byte>.Empty : result.AsSpan(dstRow - rowBytes, rowBytes);

            switch (filterType)
            {
                case 0: // None
                    srcRowData.CopyTo(dstRowData);
                    break;
                case 1: // Sub
                    for (int x = 0; x < rowBytes; x++)
                    {
                        byte left = x >= bpp ? dstRowData[x - bpp] : (byte)0;
                        dstRowData[x] = (byte)(srcRowData[x] + left);
                    }
                    break;
                case 2: // Up
                    for (int x = 0; x < rowBytes; x++)
                    {
                        byte up = prevRow.IsEmpty ? (byte)0 : prevRow[x];
                        dstRowData[x] = (byte)(srcRowData[x] + up);
                    }
                    break;
                case 3: // Average
                    for (int x = 0; x < rowBytes; x++)
                    {
                        byte left = x >= bpp ? dstRowData[x - bpp] : (byte)0;
                        byte up = prevRow.IsEmpty ? (byte)0 : prevRow[x];
                        dstRowData[x] = (byte)(srcRowData[x] + (byte)((left + up) >> 1));
                    }
                    break;
                case 4: // Paeth
                    for (int x = 0; x < rowBytes; x++)
                    {
                        byte left = x >= bpp ? dstRowData[x - bpp] : (byte)0;
                        byte up = prevRow.IsEmpty ? (byte)0 : prevRow[x];
                        byte upLeft = (x >= bpp && !prevRow.IsEmpty) ? prevRow[x - bpp] : (byte)0;
                        dstRowData[x] = (byte)(srcRowData[x] + PaethPredictor(left, up, upLeft));
                    }
                    break;
                default:
                    throw new InvalidDataException($"PNG row {y}: unknown filter type {filterType}.");
            }
        }
        return result;
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }
        return pb <= pc ? b : c;
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

        Crc32 crc = new();
        crc.Append(tag.Span);
        crc.Append(data.Span);
        byte[] crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc.GetCurrentHashAsUInt32());
        await destination.WriteAsync(crcBytes, cancellationToken).ConfigureAwait(false);
    }
}
