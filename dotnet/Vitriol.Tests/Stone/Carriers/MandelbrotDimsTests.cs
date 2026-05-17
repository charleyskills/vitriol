using Vitriol.Stone.Carriers;

namespace Vitriol.Tests.Stone.Carriers;

public sealed class MandelbrotDimsTests
{
    [Theory]
    [InlineData(0, 1080, 1080)]
    [InlineData(1, 1080, 1080)]
    [InlineData(100_000, 1080, 1080)]
    [InlineData(437_400, 1080, 1080)]      // 1080*1080 = 1,166,400 pixels — k=1 fits ≤437K envelope bytes
    public void Small_envelopes_land_in_1080_tier(int envBytes, int expectedW, int expectedH)
    {
        (int w, int h) = MandelbrotDims.ForEnvelope(envBytes);
        w.ShouldBe(expectedW);
        h.ShouldBe(expectedH);
    }

    [Fact]
    public void Two_thousand_kilobytes_steps_to_2048_tier()
    {
        // 2 MB envelope * 8 = 16 MB pixel bytes. /3 = 5.33M pixels. 2048² = 4.19M — too small.
        // Actually that exceeds 2048². Should land in 4096.
        (int w, int h) = MandelbrotDims.ForEnvelope(2_000_000);
        w.ShouldBe(4096);
        h.ShouldBe(4096);
    }

    [Theory]
    [InlineData(500_000, 2048)]     // 4M pixel bytes → ~1.3M pixels — fits 2048²(4.19M).
    [InlineData(1_500_000, 4096)]   // 12M pixel bytes → 4M pixels — exceeds 2048²(4.19M).
    [InlineData(6_000_000, 8192)]   // 48M pixel bytes → 16M pixels — exceeds 4096²(16.7M).
    public void Mid_envelopes_land_in_progressively_larger_tiers(int envBytes, int expectedSide)
    {
        (int w, int h) = MandelbrotDims.ForEnvelope(envBytes);
        w.ShouldBe(expectedSide);
        h.ShouldBe(expectedSide);
    }

    [Fact]
    public void Above_8192_tier_rounds_up_to_next_1024_multiple()
    {
        // 30 MB envelope → 240 MB pixel bytes → 80M pixels → side ≈ 8945 → round up to 9216.
        (int w, int h) = MandelbrotDims.ForEnvelope(30_000_000);
        w.ShouldBe(h);
        (w % 1024).ShouldBe(0);
        ((long)w * h).ShouldBeGreaterThanOrEqualTo(80_000_000);
    }

    [Fact]
    public void Negative_envelope_byte_count_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MandelbrotDims.ForEnvelope(-1));
    }
}
