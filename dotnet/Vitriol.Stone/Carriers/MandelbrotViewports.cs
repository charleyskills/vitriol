namespace Vitriol.Stone.Carriers;

/// <summary>
/// Curated set of 64 hand-picked Mandelbrot viewports. Transcribed verbatim
/// from <c>app/format_handlers/_mandelbrot.py:49-88</c>. Each tuple is
/// <c>(center_x, center_y, half_width)</c>; all chosen to land squarely on
/// the boundary of the set (the only region where iteration counts vary
/// enough to render a recognizable fractal).
///
/// <para>Per-source variety comes from picking one of the 64 viewports by
/// the SHA-256 digest's first byte, then jittering the center by ±60% of
/// the half-width using bytes [8:24] of the digest.</para>
/// </summary>
public static class MandelbrotViewports
{
    public readonly record struct Viewport(double CenterX, double CenterY, double HalfWidth);

    /// <summary>Whole-set fallback viewport used when the source-picked viewport lands in an all-uniform region.</summary>
    public static readonly Viewport Fallback = new(-0.5, 0.0, 1.5);

    /// <summary>Jitter range as a fraction of the viewport's half-width (Python's <c>_JITTER_RANGE</c>).</summary>
    public const double JitterRange = 1.2;

    /// <summary>Number of palette algorithms registered in <see cref="MandelbrotGenerator"/>.</summary>
    public const int PaletteCount = 6;

    public static readonly Viewport[] All =
    {
        // Whole-set + wide-field views.
        new(-0.5, 0.0, 1.5),
        new(-0.7, 0.0, 1.4),
        new(-0.5, 0.5, 0.7),
        new(-0.5, -0.5, 0.7),

        // Cardioid edge zooms.
        new(0.28, 0.01, 0.06),
        new(0.275, -0.01, 0.05),
        new(-0.235, 0.0, 0.05),
        new(-0.4, 0.6, 0.18),
        new(-0.1, 0.65, 0.15),
        new(-0.1, 0.85, 0.2),
        new(-0.235, 0.625, 0.04),
        new(0.36, 0.1, 0.04),
        new(0.34, 0.05, 0.06),
        new(-0.69, 0.31, 0.06),

        // Period-bulb boundaries.
        new(-1.25, 0.0, 0.15),
        new(-1.305, 0.0, 0.04),
        new(-1.255, 0.045, 0.025),
        new(-0.125, 0.745, 0.05),
        new(-0.158, 1.033, 0.012),
        new(-0.16, 1.04, 0.04),
        new(-1.401155, 0.0, 0.02),
        new(-1.476, 0.0, 0.012),
        new(-0.747, 0.105, 0.018),
        new(-1.39, 0.005, 0.025),

        // Filaments + antennas.
        new(-1.7689, 0.0, 0.012),
        new(-1.99, 0.0, 0.005),
        new(-1.985, 0.0, 0.008),
        new(-1.93, 0.0, 0.014),
        new(-1.85, 0.0, 0.03),
        new(-1.6735, 0.0006, 0.0015),
        new(-1.4002, 0.0, 0.005),
        new(-1.4, 0.0, 0.025),
        new(-0.74, 0.21, 0.022),
        new(-0.745, 0.186, 0.04),

        // Seahorse / spiral valleys.
        new(-0.745, 0.113, 0.012),
        new(-0.7445, 0.1217, 0.005),
        new(-0.7440, 0.1245, 0.0014),
        new(-0.7269, 0.1889, 0.025),
        new(-0.748, 0.085, 0.05),
        new(-0.748, 0.0975, 0.025),
        new(-0.756, 0.07, 0.013),
        new(-0.74, 0.205, 0.018),
        new(-0.7475, 0.115, 0.0075),
        new(-0.7269, 0.18, 0.012),

        // Mini-Mandelbrots.
        new(-1.7493, 0.000, 0.0015),
        new(-1.62917, 0.0, 0.0025),
        new(-0.15891, 1.03244, 0.0035),
        new(-0.10109637, 0.95628651, 0.001),
        new(-1.985409, 0.0, 0.0008),
        new(0.359, 0.0865, 0.012),
        new(-1.7396, 0.0, 0.005),

        // Misiurewicz points.
        new(-0.77568377, 0.13646737, 0.005),
        new(-0.1011, 0.9563, 0.001),
        new(-1.543689, 0.0, 0.0025),
        new(-0.7752, 0.1361, 0.0025),
        new(-1.401155, 0.0, 0.0025),

        // Custom / miscellany.
        new(-0.7440, 0.1340, 0.005),
        new(-0.6840039, 0.4604141, 0.005),
        new(0.3736, 0.0917, 0.012),
        new(-1.0, 0.275, 0.04),
        new(-0.95, 0.265, 0.025),
        new(-1.07, 0.265, 0.03),
        new(-0.633, 0.4, 0.05),
        new(-0.6905, 0.379, 0.018),
    };

    public static int IndexFromHash(ReadOnlySpan<byte> sha256Digest)
    {
        if (sha256Digest.Length == 0)
        {
            throw new ArgumentException("SHA-256 digest must be non-empty.", nameof(sha256Digest));
        }
        return sha256Digest[0] % All.Length;
    }
}
