using System.Text;

namespace Vitriol.Core.Detection.Sniffers;

/// <summary>
/// Single sniffer that performs the magic-byte checks from
/// <c>app/core/file_detector.py:_sniff</c> (lines 66-119). The ZIP probe is
/// deferred to <see cref="ZipSubtypeSniffer"/> because it has to open the
/// archive (i.e., it needs the path, not just the head).
/// </summary>
public sealed class MagicByteSniffer : IFileSniffer
{
    private static ReadOnlySpan<byte> PngHeader =>
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

    private static ReadOnlySpan<byte> Jpeg => new byte[] { 0xFF, 0xD8, 0xFF };

    private static ReadOnlySpan<byte> Gif87 => "GIF87a"u8;

    private static ReadOnlySpan<byte> Gif89 => "GIF89a"u8;

    private static ReadOnlySpan<byte> Bmp => "BM"u8;

    private static ReadOnlySpan<byte> Riff => "RIFF"u8;

    private static ReadOnlySpan<byte> Webp => "WEBP"u8;

    private static ReadOnlySpan<byte> TiffLe => new byte[] { (byte)'I', (byte)'I', (byte)'*', 0x00 };

    private static ReadOnlySpan<byte> TiffBe => new byte[] { (byte)'M', (byte)'M', 0x00, (byte)'*' };

    private static ReadOnlySpan<byte> Dds => "DDS "u8;

    private static ReadOnlySpan<byte> Ico => new byte[] { 0x00, 0x00, 0x01, 0x00 };

    private static ReadOnlySpan<byte> Gltf => "glTF"u8;

    private static ReadOnlySpan<byte> StlAscii => "solid"u8;

    private static ReadOnlySpan<byte> Pdf => "%PDF-"u8;

    private static ReadOnlySpan<byte> Rtf5 => @"{\rtf"u8;

    private static ReadOnlySpan<byte> Zip => new byte[] { (byte)'P', (byte)'K', 0x03, 0x04 };

    private static ReadOnlySpan<byte> Xml => "<?xml"u8;

    private static ReadOnlySpan<byte> SvgTag => "<svg"u8;

    private static ReadOnlySpan<byte> DoctypeHtml => "<!doctype html"u8;

    private static ReadOnlySpan<byte> HtmlTag => "<html"u8;

    public int Order => 0;

    public string? TrySniff(ReadOnlySpan<byte> head, string path)
    {
        if (head.IsEmpty)
        {
            return null;
        }

        // Images
        if (head.StartsWith(PngHeader))
        {
            return ".png";
        }
        if (head.StartsWith(Jpeg))
        {
            return ".jpg";
        }
        if (head.StartsWith(Gif87) || head.StartsWith(Gif89))
        {
            return ".gif";
        }
        if (head.StartsWith(Bmp))
        {
            return ".bmp";
        }
        if (head.Length >= 12 && head[..4].SequenceEqual(Riff) && head.Slice(8, 4).SequenceEqual(Webp))
        {
            return ".webp";
        }
        if (head.Length >= 4 && (head[..4].SequenceEqual(TiffLe) || head[..4].SequenceEqual(TiffBe)))
        {
            return ".tiff";
        }
        if (head.StartsWith(Dds))
        {
            return ".dds";
        }
        if (head.StartsWith(Ico))
        {
            return ".ico";
        }

        // 3D
        if (head.StartsWith(Gltf))
        {
            return ".glb";
        }
        if (head.StartsWith(StlAscii) || (head.Length >= 80 && CountZeros(head[..80]) > 20))
        {
            // ASCII vs binary STL is ambiguous; only trust this signal when the
            // extension already claims STL.
            if (string.Equals(Path.GetExtension(path), ".stl", StringComparison.OrdinalIgnoreCase))
            {
                return ".stl";
            }
        }

        // Documents
        if (head.StartsWith(Pdf))
        {
            return ".pdf";
        }
        if (head.StartsWith(Rtf5))
        {
            return ".rtf";
        }

        // ZIP — defer to ZipSubtypeSniffer to peek inside.
        if (head.StartsWith(Zip))
        {
            return null; // signal "ZIP detected but subtype probe owns this"
        }

        // XML / SVG / HTML / JSON — work on the left-trimmed head.
        ReadOnlySpan<byte> trimmed = TrimLeadingWhitespace(head);
        if (trimmed.StartsWith(Xml))
        {
            ReadOnlySpan<byte> lookahead = trimmed[..Math.Min(trimmed.Length, 200)];
            if (ContainsCaseInsensitive(lookahead, SvgTag))
            {
                return ".svg";
            }
            return ".xml";
        }
        if (StartsWithCaseInsensitive(trimmed, SvgTag))
        {
            return ".svg";
        }
        if (trimmed.Length > 0 && (trimmed[0] == (byte)'{' || trimmed[0] == (byte)'['))
        {
            if (head.IndexOf("\"asset\""u8) >= 0 && head.IndexOf("\"version\""u8) >= 0)
            {
                return ".gltf";
            }
            return ".json";
        }

        ReadOnlySpan<byte> htmlWindow = head[..Math.Min(head.Length, 512)];
        ReadOnlySpan<byte> htmlTrimmed = TrimLeadingWhitespace(htmlWindow);
        if (StartsWithCaseInsensitive(htmlTrimmed, DoctypeHtml) || StartsWithCaseInsensitive(htmlTrimmed, HtmlTag))
        {
            return ".html";
        }

        return null;
    }

    private static ReadOnlySpan<byte> TrimLeadingWhitespace(ReadOnlySpan<byte> span)
    {
        int i = 0;
        while (i < span.Length && IsWhitespace(span[i]))
        {
            i++;
        }
        return span[i..];
    }

    private static bool IsWhitespace(byte b) => b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D || b == 0x0B || b == 0x0C;

    private static bool StartsWithCaseInsensitive(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (haystack.Length < needle.Length)
        {
            return false;
        }
        for (int i = 0; i < needle.Length; i++)
        {
            if (ToLower(haystack[i]) != ToLower(needle[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ContainsCaseInsensitive(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty || haystack.Length < needle.Length)
        {
            return false;
        }
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (StartsWithCaseInsensitive(haystack[i..], needle))
            {
                return true;
            }
        }
        return false;
    }

    private static byte ToLower(byte b) => (byte)(b >= 'A' && b <= 'Z' ? b + 32 : b);

    private static int CountZeros(ReadOnlySpan<byte> span)
    {
        int n = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == 0)
            {
                n++;
            }
        }
        return n;
    }
}
