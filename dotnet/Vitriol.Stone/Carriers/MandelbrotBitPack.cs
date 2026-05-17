namespace Vitriol.Stone.Carriers;

/// <summary>
/// k=1 LSB scatter-pack of UCMSv3 envelope bytes into Mandelbrot pixel bytes.
/// Each envelope byte takes 8 pixel-byte LSBs at a scatter position derived
/// from a golden-ratio fraction of the total octet count, coprime to it.
/// Pixel byte top 7 bits = fractal color (perceptually pristine);
/// bottom 1 bit = payload or zero at non-scatter positions.
///
/// <para>Mirrors <c>_mandelbrot_pack_envelope_into_fractal</c> and
/// <c>_mandelbrot_unpack_envelope_from_pixels</c> at
/// <c>app/format_handlers/masquerade.py:2652-2728</c>. Cross-implementation
/// payload parity hinges on this scatter-stride math being byte-exact.</para>
/// </summary>
public static class MandelbrotBitPack
{
    public const int PixelBytesPerEnvelopeByte = 8;

    public const byte PayloadMask = 0x01;

    public const byte FractalMask = 0xFE;

    private const double GoldenRatioFraction = 0.6180339887498949;

    /// <summary>
    /// Returns the scatter stride (and a reserved zero, mirroring Python's
    /// tuple return for API parity). Deterministic from <paramref name="numOctets"/>
    /// alone, so the decoder uses the identical stride.
    ///
    /// <para>Algorithm (port of <c>_mandelbrot_scatter_indices</c>,
    /// <c>masquerade.py:2630-2649</c>): start with <c>max(7, round(numOctets × 0.618))</c>;
    /// ensure odd; bump by 2 until coprime to <paramref name="numOctets"/>.
    /// Degenerate fallback when no coprime in range is 1.</para>
    /// </summary>
    public static long ScatterStride(long numOctets)
    {
        if (numOctets <= 1)
        {
            return 1;
        }

        long target = Math.Max(7L, (long)(numOctets * GoldenRatioFraction));
        if ((target & 1) == 0)
        {
            target++;
        }
        while (Gcd(target, numOctets) != 1)
        {
            target += 2;
            if (target >= numOctets)
            {
                return 1; // degenerate fallback — always coprime to 1
            }
        }
        return target;
    }

    /// <summary>
    /// Pack envelope bytes into the LSBs of <paramref name="pixelBytes"/>
    /// in-place. Pixel byte LSBs at non-scatter positions are forced to zero;
    /// LSBs at scatter positions get one envelope bit each (MSB-first within
    /// the envelope byte).
    /// </summary>
    public static void Pack(ReadOnlySpan<byte> envelope, Span<byte> pixelBytes)
    {
        // Clear every LSB first; the non-scatter positions stay at zero.
        for (int i = 0; i < pixelBytes.Length; i++)
        {
            pixelBytes[i] = (byte)(pixelBytes[i] & FractalMask);
        }

        long totalPixelBytes = pixelBytes.Length;
        long numOctets = totalPixelBytes / PixelBytesPerEnvelopeByte;
        if (numOctets == 0 || envelope.IsEmpty)
        {
            return;
        }

        long nEnv = Math.Min(envelope.Length, numOctets);
        long stride = ScatterStride(numOctets);

        for (long i = 0; i < nEnv; i++)
        {
            byte envByte = envelope[(int)i];
            long octetIdx = (i * stride) % numOctets;
            long basePos = octetIdx * PixelBytesPerEnvelopeByte;

            // MSB-first: bit 7 lands at offset 0, bit 0 at offset 7. Mirrors
            // np.unpackbits(...).reshape(n, 8)[:, ::-1] which reverses each
            // row to make np.packbits happy on decode.
            for (int j = 0; j < PixelBytesPerEnvelopeByte; j++)
            {
                int bit = (envByte >> (7 - j)) & 1;
                pixelBytes[(int)(basePos + j)] = (byte)(pixelBytes[(int)(basePos + j)] | bit);
            }
        }
    }

    /// <summary>
    /// Unpack up to <paramref name="maxEnvelopeBytes"/> envelope bytes from
    /// the LSBs of <paramref name="pixelBytes"/> using the scatter stride
    /// derived from <paramref name="totalPixelBytes"/>. When the caller
    /// passes a partial buffer (e.g. a 64 KB has-envelope probe), pass the
    /// full image size as <paramref name="totalPixelBytes"/> so the stride
    /// matches the encoder.
    /// </summary>
    public static byte[] Unpack(ReadOnlySpan<byte> pixelBytes, int maxEnvelopeBytes, long totalPixelBytes = -1)
    {
        if (totalPixelBytes < 0)
        {
            totalPixelBytes = pixelBytes.Length;
        }

        long numOctetsFull = totalPixelBytes / PixelBytesPerEnvelopeByte;
        if (numOctetsFull == 0 || maxEnvelopeBytes <= 0)
        {
            return Array.Empty<byte>();
        }

        long nEnv = Math.Min(maxEnvelopeBytes, numOctetsFull);
        long stride = ScatterStride(numOctetsFull);
        byte[] result = new byte[nEnv];

        for (long i = 0; i < nEnv; i++)
        {
            long octetIdx = (i * stride) % numOctetsFull;
            long basePos = octetIdx * PixelBytesPerEnvelopeByte;
            if (basePos + PixelBytesPerEnvelopeByte - 1 >= pixelBytes.Length)
            {
                // Octet falls outside the partial buffer — leave its byte at zero.
                continue;
            }
            byte assembled = 0;
            for (int j = 0; j < PixelBytesPerEnvelopeByte; j++)
            {
                int bit = pixelBytes[(int)(basePos + j)] & PayloadMask;
                // MSB-first: pixel offset 0 contributes bit 7 of envelope byte.
                assembled |= (byte)(bit << (7 - j));
            }
            result[(int)i] = assembled;
        }
        return result;
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
