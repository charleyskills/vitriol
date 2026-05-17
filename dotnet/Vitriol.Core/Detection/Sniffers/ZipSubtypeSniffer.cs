using System.IO.Compression;
using System.Text;

namespace Vitriol.Core.Detection.Sniffers;

/// <summary>
/// When the magic bytes say ZIP, peek inside the archive to distinguish OOXML
/// (.docx / .xlsx / .pptx), OpenDocument (.odt), and EPUB. Mirrors
/// <c>_zip_subtype</c> in <c>app/core/file_detector.py:122-146</c>.
/// </summary>
public sealed class ZipSubtypeSniffer : IFileSniffer
{
    private static ReadOnlySpan<byte> ZipMagic =>
        new byte[] { (byte)'P', (byte)'K', 0x03, 0x04 };

    public int Order => 100; // runs after MagicByteSniffer

    public string? TrySniff(ReadOnlySpan<byte> head, string path)
    {
        if (!head.StartsWith(ZipMagic))
        {
            return null;
        }
        if (!File.Exists(path))
        {
            // Can't open the archive — fall back to extension hint.
            return ".zip";
        }
        return Probe(path);
    }

    internal static string Probe(string path)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);
            using ZipArchive zip = new(fs, ZipArchiveMode.Read, leaveOpen: false);

            // Track presence of OOXML / ODF / EPUB markers.
            bool hasContentTypes = false;
            bool hasWord = false;
            bool hasXl = false;
            bool hasPpt = false;
            ZipArchiveEntry? mimetypeEntry = null;

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName;
                if (name == "[Content_Types].xml")
                {
                    hasContentTypes = true;
                }
                if (name.StartsWith("word/", StringComparison.Ordinal))
                {
                    hasWord = true;
                }
                if (name.StartsWith("xl/", StringComparison.Ordinal))
                {
                    hasXl = true;
                }
                if (name.StartsWith("ppt/", StringComparison.Ordinal))
                {
                    hasPpt = true;
                }
                if (name == "mimetype" && mimetypeEntry is null)
                {
                    mimetypeEntry = entry;
                }
            }

            if (hasContentTypes)
            {
                if (hasWord)
                {
                    return ".docx";
                }
                if (hasXl)
                {
                    return ".xlsx";
                }
                if (hasPpt)
                {
                    return ".pptx";
                }
            }

            if (mimetypeEntry is not null)
            {
                string mimetype = ReadMimetype(mimetypeEntry);
                if (mimetype == "application/epub+zip")
                {
                    return ".epub";
                }
                if (mimetype.StartsWith("application/vnd.oasis.opendocument.text", StringComparison.Ordinal))
                {
                    return ".odt";
                }
            }
        }
        catch (InvalidDataException) { /* truncated ZIP — fall through */ }
        catch (IOException) { /* file in use, etc. */ }
        catch (UnauthorizedAccessException) { /* permission denied */ }

        return ".zip";
    }

    private static string ReadMimetype(ZipArchiveEntry entry)
    {
        try
        {
            using Stream s = entry.Open();
            using StreamReader sr = new(s, Encoding.ASCII);
            return sr.ReadToEnd().Trim();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
