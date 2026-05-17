namespace Vitriol.Stone.Envelope;

/// <summary>
/// Magic prefixes identifying each Stone envelope variant. Byte-for-byte
/// identical to <c>app/format_handlers/masquerade.py:39-44</c>. <b>Do not
/// change.</b> Backward compatibility with Vitriol-produced files is the gating
/// property of the Stone engine.
///
/// <para>Note: V1 is <b>7 bytes</b> (six ASCII characters plus one terminating
/// zero), while every newer variant is <b>8 bytes</b>. This asymmetry must be
/// preserved exactly so that <c>find(MAGIC)</c> probes in existing
/// Vitriol-produced files locate the prefix at the same byte offset.</para>
/// </summary>
public static class UcmsMagic
{
    public const int V1Length = 7;

    public const int V8Length = 8;

    /// <summary>Plain UCMSv1 envelope: <c>b"UCMSv1\0"</c> — 7 bytes.</summary>
    public static ReadOnlySpan<byte> V1 =>
        new byte[] { (byte)'U', (byte)'C', (byte)'M', (byte)'S', (byte)'v', (byte)'1', 0x00 };

    /// <summary>UCMSv2 tiered-image envelope (PNG/BMP) — 8 bytes.</summary>
    public static ReadOnlySpan<byte> V2 =>
        new byte[] { (byte)'U', (byte)'C', (byte)'M', (byte)'S', (byte)'v', (byte)'2', 0x00, 0x00 };

    /// <summary>UCMSv3 encrypted Mandelbrot envelope (PNG/BMP) — 8 bytes.</summary>
    public static ReadOnlySpan<byte> V3 =>
        new byte[] { (byte)'U', (byte)'C', (byte)'M', (byte)'S', (byte)'v', (byte)'3', 0x00, 0x00 };

    /// <summary>UCMSv3 encrypted music envelope (WAV/AIFF/FLAC) — 8 bytes.</summary>
    public static ReadOnlySpan<byte> V3Audio =>
        new byte[] { (byte)'u', (byte)'M', (byte)'0', (byte)'3', 0x00, 0x00, 0x00, 0x00 };

    /// <summary>UCMSv3 encrypted 3D envelope (PLY/OBJ/GLB) — 8 bytes.</summary>
    public static ReadOnlySpan<byte> V3Model =>
        new byte[] { (byte)'U', (byte)'C', (byte)'3', (byte)'D', (byte)'v', (byte)'3', 0x00, 0x00 };

    /// <summary>UCMSv3 encrypted animated-Mandelbrot envelope (MKV/MP4) — 8 bytes.</summary>
    public static ReadOnlySpan<byte> V3Video =>
        new byte[] { (byte)'U', (byte)'C', (byte)'M', (byte)'v', (byte)'3', 0x00, 0x00, 0x00 };
}
