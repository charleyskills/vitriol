using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Vitriol.Stone.Carriers;

/// <summary>
/// Hashes envelope-derived bytes into a deterministic Mandelbrot seed:
/// viewport pick, position jitter, R/G/B phase, palette algorithm id.
/// Mirrors <c>derive_seed</c> in <c>app/format_handlers/_mandelbrot.py:105-126</c>
/// and the salt + width/height/content_seed wrapper at
/// <c>app/format_handlers/masquerade.py:2768-2772</c>.
///
/// <para>Same source → same seed → same fractal, by construction.</para>
/// </summary>
public static class MandelbrotSeed
{
    public const double Tau = 2.0 * Math.PI;

    /// <summary>Salt mirroring <c>_MANDELBROT_SALT</c> at <c>masquerade.py:2608</c>.</summary>
    public static readonly byte[] Salt = Encoding.ASCII.GetBytes("transmute-mandelbrot-v1");

    public readonly record struct Result(
        double CenterX,
        double CenterY,
        double HalfWidth,
        double RPhase,
        double GPhase,
        double BPhase,
        int PaletteId);

    /// <summary>
    /// Builds the seed input <c>SALT + struct.pack(">II", W, H) + contentSeed</c>,
    /// hashes it, and derives the 7-tuple seed for <see cref="MandelbrotGenerator"/>.
    /// </summary>
    public static Result Derive(int width, int height, ReadOnlySpan<byte> contentSeed)
    {
        byte[] seedInput = new byte[Salt.Length + 8 + contentSeed.Length];
        Salt.CopyTo(seedInput, 0);
        BinaryPrimitives.WriteUInt32BigEndian(seedInput.AsSpan(Salt.Length, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(seedInput.AsSpan(Salt.Length + 4, 4), (uint)height);
        contentSeed.CopyTo(seedInput.AsSpan(Salt.Length + 8));

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(seedInput, digest);
        return FromDigest(digest);
    }

    /// <summary>
    /// Derives the seed directly from a pre-computed SHA-256 digest.
    /// Mirrors the byte windows in <c>_mandelbrot.derive_seed</c>:
    /// viewport index from byte 0; jitter X from bytes [8:16] big-endian uint64;
    /// jitter Y from bytes [16:24]; phases from bytes 24, 25, 26; palette id from byte 27.
    /// </summary>
    public static Result FromDigest(ReadOnlySpan<byte> digest)
    {
        if (digest.Length < 32)
        {
            throw new ArgumentException("Need a full 32-byte SHA-256 digest.", nameof(digest));
        }

        int idx = MandelbrotViewports.IndexFromHash(digest);
        MandelbrotViewports.Viewport vp = MandelbrotViewports.All[idx];

        ulong jxRaw = BinaryPrimitives.ReadUInt64BigEndian(digest.Slice(8, 8));
        ulong jyRaw = BinaryPrimitives.ReadUInt64BigEndian(digest.Slice(16, 8));

        // Python: jx = (struct.unpack(">Q", h[8:16])[0] / float(1 << 64) - 0.5) * hw * _JITTER_RANGE
        // Use uint64 → double via division — float(1 << 64) is exact in IEEE 754
        // (2^64 has an exact double representation).
        const double TwoPow64 = 18446744073709551616.0; // 2.0 ** 64
        double jx = (jxRaw / TwoPow64 - 0.5) * vp.HalfWidth * MandelbrotViewports.JitterRange;
        double jy = (jyRaw / TwoPow64 - 0.5) * vp.HalfWidth * MandelbrotViewports.JitterRange;

        double rPhase = digest[24] / 255.0 * Tau;
        double gPhase = digest[25] / 255.0 * Tau;
        double bPhase = digest[26] / 255.0 * Tau;
        int paletteId = digest[27] % MandelbrotViewports.PaletteCount;

        return new Result(
            CenterX: vp.CenterX + jx,
            CenterY: vp.CenterY + jy,
            HalfWidth: vp.HalfWidth,
            RPhase: rPhase,
            GPhase: gPhase,
            BPhase: bPhase,
            PaletteId: paletteId);
    }
}
