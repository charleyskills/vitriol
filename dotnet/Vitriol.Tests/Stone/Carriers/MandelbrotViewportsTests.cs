using System.Security.Cryptography;
using Vitriol.Stone.Carriers;

namespace Vitriol.Tests.Stone.Carriers;

public sealed class MandelbrotViewportsTests
{
    [Fact]
    public void Exactly_64_viewports_loaded()
    {
        MandelbrotViewports.All.Length.ShouldBe(64);
    }

    [Theory]
    [InlineData((byte)0, 0)]
    [InlineData((byte)1, 1)]
    [InlineData((byte)63, 63)]
    [InlineData((byte)64, 0)]
    [InlineData((byte)128, 0)]
    [InlineData((byte)200, 200 % 64)]
    [InlineData((byte)255, 255 % 64)]
    public void Index_from_hash_picks_first_byte_modulo_64(byte firstDigestByte, int expectedIndex)
    {
        byte[] digest = new byte[32];
        digest[0] = firstDigestByte;
        MandelbrotViewports.IndexFromHash(digest).ShouldBe(expectedIndex);
    }

    [Fact]
    public void First_viewport_is_the_whole_set_default()
    {
        MandelbrotViewports.All[0].ShouldBe(new MandelbrotViewports.Viewport(-0.5, 0.0, 1.5));
    }

    [Fact]
    public void Fallback_matches_python_fallback_viewport()
    {
        MandelbrotViewports.Fallback.ShouldBe(new MandelbrotViewports.Viewport(-0.5, 0.0, 1.5));
    }

    [Fact]
    public void Jitter_range_and_palette_count_match_python_constants()
    {
        MandelbrotViewports.JitterRange.ShouldBe(1.2);
        MandelbrotViewports.PaletteCount.ShouldBe(6);
    }

    [Fact]
    public void Empty_digest_rejected()
    {
        Should.Throw<ArgumentException>(() => MandelbrotViewports.IndexFromHash(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Same_digest_same_viewport_index_always()
    {
        byte[] sample = SHA256.HashData("vitriol-mandelbrot-test"u8.ToArray());
        int a = MandelbrotViewports.IndexFromHash(sample);
        int b = MandelbrotViewports.IndexFromHash(sample);
        a.ShouldBe(b);
        (a >= 0 && a < 64).ShouldBeTrue();
    }
}
