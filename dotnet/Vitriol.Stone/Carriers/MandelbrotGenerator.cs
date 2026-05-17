namespace Vitriol.Stone.Carriers;

/// <summary>
/// Renders a Mandelbrot fractal to a row-major RGB byte buffer suitable for
/// LSB scatter-pack. Mirrors <c>generate_keystream</c> in
/// <c>app/format_handlers/_mandelbrot.py:337-418</c> (visually — exact
/// per-byte parity with NumPy isn't a requirement; the LSBs are overwritten
/// by <see cref="MandelbrotBitPack"/> downstream so the carrier's top 7
/// bits are decorative only).
///
/// <para><b>Scope</b>: Sprint 10 ships palette 0 (the three-sin original)
/// which is what Python uses 1/6 of the time and is the simplest. The other
/// five palettes are visual variations and are deferred to a follow-up
/// (palette_id from the seed is honored modulo registered count).</para>
///
/// <para><b>Performance</b>: at most 1080×1080 = 1.16M pixels are iterated;
/// larger output dimensions nearest-neighbour up-scale that base buffer.
/// Mirrors Python's <c>_FRACTAL_CAP = 1080</c> behavior.</para>
/// </summary>
public static class MandelbrotGenerator
{
    public const int MaxIter = 255;

    public const float BailoutSquared = 4.0f;

    public const int FractalCap = 1080;

    private const double PaletteRFreq = 0.025;
    private const double PaletteGFreq = 0.018;
    private const double PaletteBFreq = 0.013;

    /// <summary>
    /// Generates a <c>width × height × 3</c> byte buffer of RGB pixels
    /// (row-major). The bottom-1-bit values are unspecified — callers should
    /// run <see cref="MandelbrotBitPack.Pack"/> over the result to clear and
    /// fill them with envelope data.
    /// </summary>
    public static byte[] Generate(int width, int height, MandelbrotSeed.Result seed)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        // Render at FractalCap × FractalCap (or smaller) and up-scale if needed.
        int compW = width;
        int compH = height;
        if (Math.Max(width, height) > FractalCap)
        {
            if (width >= height)
            {
                compW = FractalCap;
                compH = Math.Max(1, (int)Math.Round(FractalCap * (double)height / width));
            }
            else
            {
                compH = FractalCap;
                compW = Math.Max(1, (int)Math.Round(FractalCap * (double)width / height));
            }
        }

        byte[] iterCount = IterateMandelbrot(compW, compH,
            (float)seed.CenterX, (float)seed.CenterY, (float)seed.HalfWidth);

        // Safety net: if the fractal lands in an all-uniform region, swap to
        // the whole-set fallback with an offset derived from the original
        // center. Mirrors masquerade.py / _mandelbrot.py safety_net logic.
        if (IsTooUniform(iterCount))
        {
            (double fbCx, double fbCy, double fbHw) = (
                MandelbrotViewports.Fallback.CenterX,
                MandelbrotViewports.Fallback.CenterY,
                MandelbrotViewports.Fallback.HalfWidth);
            iterCount = IterateMandelbrot(compW, compH,
                (float)(fbCx + Modulo(seed.CenterX, 0.3) - 0.15),
                (float)(fbCy + Modulo(seed.CenterY, 0.2) - 0.1),
                (float)fbHw);
        }

        byte[] compRgb = ApplyPalette(iterCount, compW, compH, seed);

        if (compW == width && compH == height)
        {
            return compRgb;
        }
        return UpscaleNearest(compRgb, compW, compH, width, height);
    }

    private static byte[] IterateMandelbrot(int width, int height, float centerX, float centerY, float halfWidth)
    {
        // Match Python's float32 iteration (NumPy default for the Mandelbrot
        // path) — close enough visually; LSBs are decorative.
        byte[] outBuf = new byte[width * height];
        Array.Fill(outBuf, (byte)MaxIter);

        float aspect = width > 0 ? (float)height / width : 1.0f;
        float halfHeight = halfWidth * aspect;

        for (int y = 0; y < height; y++)
        {
            float ci = height > 1
                ? centerY - halfHeight + 2.0f * halfHeight * y / (height - 1)
                : centerY;
            int rowBase = y * width;

            for (int x = 0; x < width; x++)
            {
                float cr = width > 1
                    ? centerX - halfWidth + 2.0f * halfWidth * x / (width - 1)
                    : centerX;

                float zr = 0f;
                float zi = 0f;
                int escape = MaxIter;
                for (int n = 0; n < MaxIter; n++)
                {
                    float zr2 = zr * zr;
                    float zi2 = zi * zi;
                    if (zr2 + zi2 > BailoutSquared)
                    {
                        escape = n;
                        break;
                    }
                    float newZi = 2.0f * zr * zi + ci;
                    zr = zr2 - zi2 + cr;
                    zi = newZi;
                }
                outBuf[rowBase + x] = (byte)escape;
            }
        }
        return outBuf;
    }

    private static bool IsTooUniform(byte[] iter)
    {
        int inside = 0;
        for (int i = 0; i < iter.Length; i++)
        {
            if (iter[i] >= MaxIter)
            {
                inside++;
            }
        }
        double frac = (double)inside / iter.Length;
        return frac > 0.92 || frac < 0.001;
    }

    private static byte[] ApplyPalette(byte[] iter, int width, int height, MandelbrotSeed.Result seed)
    {
        byte[] rgb = new byte[width * height * 3];
        // Palette 0 (three-sin) is the canonical Python first palette and the
        // most recognizable. Other palettes are deferred (see class header).
        for (int i = 0; i < iter.Length; i++)
        {
            double n = iter[i];
            byte r, g, b;
            if (iter[i] >= MaxIter)
            {
                // Body of the set: black, regardless of palette. Mirrors
                // _mandelbrot.py:403-405.
                r = g = b = 0;
            }
            else
            {
                double rf = Math.Sin(n * PaletteRFreq + seed.RPhase) * 127.0 + 128.0;
                double gf = Math.Sin(n * PaletteGFreq + seed.GPhase) * 127.0 + 128.0;
                double bf = Math.Sin(n * PaletteBFreq + seed.BPhase) * 127.0 + 128.0;
                r = ClampToByte(rf);
                g = ClampToByte(gf);
                b = ClampToByte(bf);
            }
            int p = i * 3;
            rgb[p] = r;
            rgb[p + 1] = g;
            rgb[p + 2] = b;
        }
        return rgb;
    }

    private static byte[] UpscaleNearest(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        byte[] dst = new byte[dstW * dstH * 3];
        // Integer nearest-neighbour. For destination pixel (x, y), source
        // index is (x * srcW / dstW, y * srcH / dstH). Each pixel is 3 bytes.
        for (int y = 0; y < dstH; y++)
        {
            int sy = (int)((long)y * srcH / dstH);
            if (sy >= srcH)
            {
                sy = srcH - 1;
            }
            int srcRow = sy * srcW * 3;
            int dstRow = y * dstW * 3;
            for (int x = 0; x < dstW; x++)
            {
                int sx = (int)((long)x * srcW / dstW);
                if (sx >= srcW)
                {
                    sx = srcW - 1;
                }
                int srcOff = srcRow + sx * 3;
                int dstOff = dstRow + x * 3;
                dst[dstOff] = src[srcOff];
                dst[dstOff + 1] = src[srcOff + 1];
                dst[dstOff + 2] = src[srcOff + 2];
            }
        }
        return dst;
    }

    private static byte ClampToByte(double v)
    {
        if (v <= 0.0)
        {
            return 0;
        }
        if (v >= 255.0)
        {
            return 255;
        }
        return (byte)v;
    }

    /// <summary>Python-style modulo: result has the same sign as the divisor.</summary>
    private static double Modulo(double x, double y)
    {
        double r = x - Math.Floor(x / y) * y;
        return r;
    }
}
