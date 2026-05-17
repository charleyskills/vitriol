using FsCheck.Xunit;
using Vitriol.Stone.Carriers;

namespace Vitriol.Tests.Stone.Carriers;

public sealed class MandelbrotBitPackTests
{
    [Fact]
    public void Round_trip_recovers_envelope_bytes_exactly()
    {
        byte[] envelope = "UCMSv3\0\0Hello, fractal world!"u8.ToArray();
        byte[] pixels = NoiseyPixels(envelope.Length * MandelbrotBitPack.PixelBytesPerEnvelopeByte + 100);

        // Pack clears LSBs of pixels and OR-s in envelope bits at scatter positions.
        MandelbrotBitPack.Pack(envelope, pixels);

        // The first 8 unpacked bytes should match the envelope prefix.
        byte[] unpacked = MandelbrotBitPack.Unpack(pixels, envelope.Length);

        unpacked.ShouldBe(envelope);
    }

    [Fact]
    public void Pack_preserves_top_seven_bits_of_each_pixel_byte()
    {
        byte[] envelope = new byte[] { 0xAB, 0xCD };
        byte[] original = NoiseyPixels(64);
        byte[] pixels = (byte[])original.Clone();

        MandelbrotBitPack.Pack(envelope, pixels);

        for (int i = 0; i < pixels.Length; i++)
        {
            ((pixels[i] & MandelbrotBitPack.FractalMask)).ShouldBe(
                (byte)(original[i] & MandelbrotBitPack.FractalMask),
                $"top 7 bits at pixel {i} should be untouched");
        }
    }

    [Fact]
    public void Non_scatter_lsbs_are_zero_after_pack()
    {
        byte[] envelope = new byte[] { 0xFF }; // 1 byte → 8 pixel-byte LSBs touched
        byte[] pixels = new byte[80]; // 10 octets × 8 bytes; only one octet is at the scatter position
        // Fill all LSBs with 1 so we can see them get cleared.
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = 0xFF;
        }

        MandelbrotBitPack.Pack(envelope, pixels);

        int scatterLsbsCovered = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if ((pixels[i] & MandelbrotBitPack.PayloadMask) == 1)
            {
                scatterLsbsCovered++;
            }
        }
        // For envelope=0xFF, all 8 bits are 1, so 8 pixel-byte LSBs are 1.
        scatterLsbsCovered.ShouldBe(8);
    }

    [Theory]
    [InlineData(0, 0)]   // empty envelope → no scatter
    [InlineData(1, 8)]   // one envelope byte → 8 pixel LSBs touched
    [InlineData(10, 80)] // ten bytes → 80 LSBs
    public void Pack_writes_exactly_eight_lsbs_per_envelope_byte(int envBytes, int expectedLsbsTouched)
    {
        byte[] envelope = new byte[envBytes];
        for (int i = 0; i < envBytes; i++)
        {
            envelope[i] = 0xFF; // all bits set
        }
        // Image must be big enough — 8 bytes per envelope byte plus a margin.
        byte[] pixels = new byte[envBytes * 8 + 1024];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = 0xFE; // top 7 bits set, LSB clear so we can count exact 1s after pack
        }

        MandelbrotBitPack.Pack(envelope, pixels);

        int lsbsSet = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if ((pixels[i] & MandelbrotBitPack.PayloadMask) == 1)
            {
                lsbsSet++;
            }
        }
        lsbsSet.ShouldBe(expectedLsbsTouched);
    }

    [Fact]
    public void Scatter_stride_is_coprime_with_octet_count()
    {
        // Spot-check a range of octet counts: stride must be coprime so the
        // scattered positions visit every octet without repeats before wrapping.
        long[] counts = { 16, 144, 1024, 145_800, 1_398_101 };
        foreach (long n in counts)
        {
            long stride = MandelbrotBitPack.ScatterStride(n);
            Gcd(stride, n).ShouldBe(1L);
        }
    }

    [Fact]
    public void Scatter_stride_for_one_octet_returns_one()
    {
        MandelbrotBitPack.ScatterStride(1).ShouldBe(1);
        MandelbrotBitPack.ScatterStride(0).ShouldBe(1);
    }

    [Property(MaxTest = 30)]
    public bool Round_trip_holds_for_arbitrary_envelope_bytes(byte[] envelope)
    {
        envelope ??= Array.Empty<byte>();
        // Plenty of room for the scatter pattern.
        byte[] pixels = NoiseyPixels(envelope.Length * 8 + 4096);
        MandelbrotBitPack.Pack(envelope, pixels);
        byte[] unpacked = MandelbrotBitPack.Unpack(pixels, envelope.Length);
        return unpacked.AsSpan().SequenceEqual(envelope);
    }

    [Fact]
    public void Partial_pixel_buffer_unpack_returns_zeros_for_octets_outside_range()
    {
        byte[] envelope = new byte[100];
        for (int i = 0; i < envelope.Length; i++)
        {
            envelope[i] = (byte)(i + 1); // distinguishable non-zero values
        }
        byte[] fullPixels = new byte[envelope.Length * 8 + 256];
        MandelbrotBitPack.Pack(envelope, fullPixels);

        // Hand the unpacker a half-sized buffer but tell it the total size
        // via the third arg so it uses the same stride.
        byte[] partial = fullPixels.AsSpan(0, fullPixels.Length / 2).ToArray();
        byte[] recovered = MandelbrotBitPack.Unpack(partial, envelope.Length,
            totalPixelBytes: fullPixels.Length);

        // Some bytes should match; out-of-range octets show as zero.
        int matches = 0;
        for (int i = 0; i < envelope.Length; i++)
        {
            if (recovered[i] == envelope[i])
            {
                matches++;
            }
        }
        matches.ShouldBeGreaterThan(0);
        matches.ShouldBeLessThan(envelope.Length);
    }

    private static byte[] NoiseyPixels(int n)
    {
        // Deterministic non-uniform pixel data so tests don't rely on actual
        // Mandelbrot output.
        byte[] pixels = new byte[n];
        for (int i = 0; i < n; i++)
        {
            pixels[i] = (byte)((i * 137 + 41) & 0xFF);
        }
        return pixels;
    }

    private static long Gcd(long a, long b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a;
    }
}
