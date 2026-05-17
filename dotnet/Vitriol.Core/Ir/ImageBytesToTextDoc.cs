using Vitriol.Core.Collections;

namespace Vitriol.Core.Ir;

/// <summary>
/// Wraps an image's raw bytes as a single-block <see cref="TextDoc"/>. Used by
/// the router's cross-category image→document gate (mirrors
/// <c>image_bytes_to_textdoc</c> at <c>app/core/intermediate.py:255–270</c>).
///
/// The original bytes are stashed in <see cref="DocumentMetadata.Origin"/> so
/// reversibility-aware writers (PDF, DOCX, EPUB) can carry them through for
/// byte-perfect round-trip.
/// </summary>
public static class ImageBytesToTextDoc
{
    private static readonly Dictionary<string, string> ExtensionToMime =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".bmp"] = "image/bmp",
            [".tiff"] = "image/tiff",
            [".tif"] = "image/tiff",
            [".heic"] = "image/heic",
            [".heif"] = "image/heic",
            [".svg"] = "image/svg+xml",
            [".avif"] = "image/avif",
            [".jxl"] = "image/jxl",
            [".ico"] = "image/x-icon",
        };

    public static TextDoc Wrap(ReadOnlyMemory<byte> sourceBytes, string sourceExtension, string? alt = null)
    {
        string ext = sourceExtension.StartsWith('.') ? sourceExtension : "." + sourceExtension;
        string mime = ExtensionToMime.TryGetValue(ext, out string? m) ? m : "image/png";
        string altText = alt ?? $"image{ext}";

        ImageBlock image = new(sourceBytes, mime, altText);
        DocumentMetadata metadata = DocumentMetadata.Empty.WithOrigin(
            new VitriolOrigin(sourceBytes, ext));

        return new TextDoc(EquatableArray.Create<Block>(image), metadata);
    }
}
