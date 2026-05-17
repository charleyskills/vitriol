using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Pbm;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Image;

/// <summary>
/// Image transcoder backed by SixLabors.ImageSharp. Mirrors
/// <c>app/format_handlers/image_handler.py</c>'s scope minus the optional
/// Pillow plugins (AVIF / HEIC / JXL): those need native binaries and ship in
/// a later sprint. SVG read is also deferred (requires Svg.Skia).
///
/// <para>This is the first media handler in the .NET port. Its presence makes
/// the router's <see cref="Vitriol.Core.Routing.SameMediaHandlerGate"/> fire
/// for image → image pairs (e.g. <c>.png → .jpg</c>) and the
/// <see cref="Vitriol.Core.Routing.CrossCategoryImageToDocumentGate"/> fire
/// for image → doc pairs (e.g. <c>.png → .txt</c>) via the
/// <c>_vitriol_origin</c> sidecar.</para>
/// </summary>
public sealed class ImageMediaHandler : IMediaHandler
{
    public MediaCategory Category => MediaCategory.Image;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg",
            ".webp", ".bmp",
            ".tiff", ".tif",
            ".gif",
            ".pbm", ".pgm", ".ppm",
            ".tga",
        };

    public async ValueTask ConvertAsync(
        string sourceExtension,
        Stream source,
        string destinationExtension,
        Stream destination,
        MediaContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        using SixLabors.ImageSharp.Image image =
            await SixLabors.ImageSharp.Image.LoadAsync(source, cancellationToken).ConfigureAwait(false);

        context.Progress?.Report(new ConversionEvent.Progress(0.5));

        IImageEncoder encoder = GetEncoder(destinationExtension);
        await image.SaveAsync(destination, encoder, cancellationToken).ConfigureAwait(false);

        context.Progress?.Report(new ConversionEvent.Progress(1.0));
    }

    internal static IImageEncoder GetEncoder(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => new PngEncoder(),
        ".jpg" or ".jpeg" => new JpegEncoder(),
        ".webp" => new WebpEncoder(),
        ".bmp" => new BmpEncoder(),
        ".tiff" or ".tif" => new TiffEncoder(),
        ".gif" => new GifEncoder(),
        ".pbm" or ".pgm" or ".ppm" => new PbmEncoder(),
        ".tga" => new TgaEncoder(),
        _ => throw new UnsupportedConversionException(
            $"ImageMediaHandler has no encoder for {extension}."),
    };
}
