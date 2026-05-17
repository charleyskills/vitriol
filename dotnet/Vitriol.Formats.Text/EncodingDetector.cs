using System.Text;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Text;

/// <summary>
/// Detects the text encoding of a byte buffer, preferring an explicit BOM
/// and falling back to strict UTF-8. <b>Loudly warns</b> when bytes don't
/// decode as valid UTF-8 — addresses the silent <c>errors="replace"</c>
/// substitution at <c>app/core/intermediate.py:218, 222</c> noted in
/// Section 11 of <c>docs/dotnet10-lossless-file-conversion-plan.md</c>.
/// </summary>
public static class EncodingDetector
{
    public sealed record DetectionResult(string Text, Encoding Encoding, bool HadReplacements, bool HadBom);

    public static DetectionResult Decode(ReadOnlyMemory<byte> bytes, IProgress<ConversionEvent>? progress = null)
    {
        Encoding? bomEncoding = DetectBom(bytes.Span);
        if (bomEncoding is not null)
        {
            int bomLength = bomEncoding.Preamble.Length;
            string text = bomEncoding.GetString(bytes.Span[bomLength..]);
            return new DetectionResult(text, bomEncoding, HadReplacements: false, HadBom: true);
        }

        // Strict UTF-8 first. On failure, fall back to replacement and warn.
        UTF8Encoding strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            string text = strict.GetString(bytes.Span);
            return new DetectionResult(text, Encoding.UTF8, HadReplacements: false, HadBom: false);
        }
        catch (DecoderFallbackException)
        {
            string fallback = Encoding.UTF8.GetString(bytes.Span);
            progress?.Report(new ConversionEvent.Warning(
                "Source bytes are not valid UTF-8. Decoded with U+FFFD replacement; "
                + "original byte sequences are lost. To preserve binary content "
                + "exactly, route via Philosopher's Stone (use --masquerade)."));
            return new DetectionResult(fallback, Encoding.UTF8, HadReplacements: true, HadBom: false);
        }
    }

    public static Encoding? DetectBom(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }
        if (head.Length >= 4 && head[0] == 0xFF && head[1] == 0xFE && head[2] == 0x00 && head[3] == 0x00)
        {
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        }
        if (head.Length >= 4 && head[0] == 0x00 && head[1] == 0x00 && head[2] == 0xFE && head[3] == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }
        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE)
        {
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        }
        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF)
        {
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        }
        return null;
    }
}
