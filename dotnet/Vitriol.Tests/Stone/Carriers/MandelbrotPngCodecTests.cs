using Vitriol.Stone.Carriers;

namespace Vitriol.Tests.Stone.Carriers;

public sealed class MandelbrotPngCodecTests
{
    [Fact]
    public async Task Round_trip_64x64_rgb_pattern()
    {
        const int W = 64;
        const int H = 64;
        byte[] pixels = new byte[W * H * 3];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int off = (y * W + x) * 3;
                pixels[off] = (byte)x;       // R: x position
                pixels[off + 1] = (byte)y;   // G: y position
                pixels[off + 2] = (byte)((x + y) & 0xFF); // B: sum
            }
        }

        await using MemoryStream dst = new();
        await MandelbrotPngCodec.WriteRgbAsync(dst, W, H, pixels, default);

        dst.Position = 0;
        MandelbrotPngCodec.Decoded decoded = await MandelbrotPngCodec.ReadRgbAsync(dst, default);

        decoded.Width.ShouldBe(W);
        decoded.Height.ShouldBe(H);
        decoded.PixelsRgb.Length.ShouldBe(W * H * 3);
        decoded.PixelsRgb.ShouldBe(pixels);
    }

    [Fact]
    public async Task Signature_is_canonical_png_header()
    {
        await using MemoryStream dst = new();
        await MandelbrotPngCodec.WriteRgbAsync(dst, 4, 4, new byte[4 * 4 * 3], default);

        byte[] bytes = dst.ToArray();
        bytes[..8].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    [Fact]
    public async Task Pixel_buffer_size_mismatch_rejected()
    {
        await using MemoryStream dst = new();
        await Should.ThrowAsync<ArgumentException>(async () =>
            await MandelbrotPngCodec.WriteRgbAsync(dst, 16, 16, new byte[10], default));
    }

    [Fact]
    public async Task Read_rejects_non_png_input()
    {
        await using MemoryStream src = new(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // JPEG SOI/APP0
        await Should.ThrowAsync<InvalidDataException>(async () =>
            await MandelbrotPngCodec.ReadRgbAsync(src, default));
    }

    [Fact]
    public async Task Read_accepts_rgba_png_and_drops_alpha_channel()
    {
        // The Sprint 3 v1 PngStoneHost writes a 1×1 RGBA PNG. Make sure the
        // RGB codec can still read it back (we may want to use the codec to
        // decode existing v1 PNGs too in some scenarios).
        byte[] rgba1x1 = Build1x1RgbaPng();
        await using MemoryStream src = new(rgba1x1);
        MandelbrotPngCodec.Decoded decoded = await MandelbrotPngCodec.ReadRgbAsync(src, default);
        decoded.Width.ShouldBe(1);
        decoded.Height.ShouldBe(1);
        decoded.PixelsRgb.Length.ShouldBe(3);
    }

    private static byte[] Build1x1RgbaPng()
    {
        // Build a minimal valid 1×1 RGBA PNG by hand (matching what Sprint 3's
        // PngStoneHost v1 produces, transparent black). We don't depend on the
        // production code here so this test stays isolated.
        byte[] signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        byte[] ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 1);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // RGBA
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;

        byte[] pixel = new byte[] { 0, 0, 0, 0, 0 };  // filter byte + RGBA
        byte[] compressed;
        using (MemoryStream ms = new())
        {
            using (System.IO.Compression.ZLibStream z = new(ms,
                System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(pixel);
            }
            compressed = ms.ToArray();
        }

        using MemoryStream output = new();
        output.Write(signature);
        WriteChunk(output, "IHDR"u8.ToArray(), ihdr);
        WriteChunk(output, "IDAT"u8.ToArray(), compressed);
        WriteChunk(output, "IEND"u8.ToArray(), Array.Empty<byte>());
        return output.ToArray();

        static void WriteChunk(Stream s, byte[] tag, byte[] data)
        {
            byte[] length = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
            s.Write(length);
            s.Write(tag);
            s.Write(data);
            System.IO.Hashing.Crc32 crc = new();
            crc.Append(tag);
            crc.Append(data);
            byte[] crcBytes = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc.GetCurrentHashAsUInt32());
            s.Write(crcBytes);
        }
    }
}
