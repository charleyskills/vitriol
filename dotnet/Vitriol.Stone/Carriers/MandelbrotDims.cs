namespace Vitriol.Stone.Carriers;

/// <summary>
/// Image-dimension tier table for the Stone v3 PNG carrier. Picks a square
/// <c>(W, H)</c> sized so the bit-packed UCMSv3 envelope fits with the
/// Mandelbrot fractal rendering across the whole image.
///
/// <para>Mirrors <c>_mandelbrot_calc_image_dims</c> at
/// <c>app/format_handlers/masquerade.py:2731-2750</c> and the
/// <c>_IMAGE_TIERS</c> table at line 2469.</para>
///
/// <para>The tier table is interpreted against the full envelope byte count
/// (after AES-CTR encryption). Pixel bytes needed = envelopeBytes × 8 (k=1
/// bit-pack); pixels needed = ceil(pixelBytes / 3) since each pixel is 3 RGB
/// bytes. The minimum dimension is 1080 even for tiny payloads.</para>
/// </summary>
public static class MandelbrotDims
{
    public const int MinDimension = 1080;

    public const int PixelBytesPerEnvelopeByte = 8; // k=1 bit-pack

    /// <summary>
    /// Tiered (cap, side) pairs in increasing order. <c>cap</c> is the maximum
    /// pixel count for that tier. Mirrors Python's <c>_IMAGE_TIERS</c> exactly.
    /// </summary>
    private static readonly (long Cap, int Side)[] Tiers =
    {
        (1080L * 1080L, 1080),
        (1920L * 1920L, 2048),
        (3840L * 3840L, 4096),
        (7680L * 7680L, 8192),
    };

    public static (int Width, int Height) ForEnvelope(int envelopeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(envelopeBytes);

        long pixelBytesNeeded = (long)envelopeBytes * PixelBytesPerEnvelopeByte;
        long pixelsNeeded = (pixelBytesNeeded + 2) / 3;

        foreach ((long cap, int side) in Tiers)
        {
            if (pixelsNeeded <= cap)
            {
                return (side, side);
            }
        }

        // Above the largest preset — grow naturally, round up to next 1024-multiple.
        long sideCandidate = (long)Math.Ceiling(Math.Sqrt(pixelsNeeded));
        long rounded = ((sideCandidate + 1023) / 1024) * 1024;
        long sideFinal = Math.Max(MinDimension, rounded);
        if (sideFinal > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"Envelope of {envelopeBytes} bytes requires image side {sideFinal}, exceeding int range.");
        }
        return ((int)sideFinal, (int)sideFinal);
    }
}
