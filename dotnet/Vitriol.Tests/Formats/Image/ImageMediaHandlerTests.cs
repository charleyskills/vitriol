using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;
using Vitriol.Formats.Image;
using Vitriol.Formats.Text;

namespace Vitriol.Tests.Formats.Image;

public sealed class ImageMediaHandlerTests
{
    private readonly ImageMediaHandler _handler = new();

    [Fact]
    public void Reports_image_category_and_canonical_extensions()
    {
        _handler.Category.ShouldBe(MediaCategory.Image);
        _handler.SupportedExtensions.ShouldContain(".png");
        _handler.SupportedExtensions.ShouldContain(".jpg");
        _handler.SupportedExtensions.ShouldContain(".jpeg");
        _handler.SupportedExtensions.ShouldContain(".webp");
        _handler.SupportedExtensions.ShouldContain(".bmp");
        _handler.SupportedExtensions.ShouldContain(".tiff");
        _handler.SupportedExtensions.ShouldContain(".gif");
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    [InlineData(".bmp")]
    [InlineData(".tiff")]
    [InlineData(".gif")]
    public async Task Converts_png_to_each_supported_target(string targetExt)
    {
        using SixLabors.ImageSharp.Image<Rgba32> src = BuildCheckerboard(16, 16);
        await using MemoryStream srcStream = new();
        await src.SaveAsPngAsync(srcStream);
        srcStream.Position = 0;

        await using MemoryStream dst = new();
        await _handler.ConvertAsync(".png", srcStream, targetExt, dst, new MediaContext(), default);

        dst.Length.ShouldBeGreaterThan(0);

        // Round-trip parse: the destination bytes must decode as a valid image.
        dst.Position = 0;
        using SixLabors.ImageSharp.Image roundTrip =
            await SixLabors.ImageSharp.Image.LoadAsync(dst, default);
        roundTrip.Width.ShouldBe(16);
        roundTrip.Height.ShouldBe(16);
    }

    [Fact]
    public async Task Lossless_target_round_trip_preserves_dimensions_and_pixels_exactly()
    {
        using SixLabors.ImageSharp.Image<Rgba32> src = BuildCheckerboard(8, 8);
        await using MemoryStream srcStream = new();
        await src.SaveAsPngAsync(srcStream);
        srcStream.Position = 0;

        await using MemoryStream bmp = new();
        await _handler.ConvertAsync(".png", srcStream, ".bmp", bmp, new MediaContext(), default);

        bmp.Position = 0;
        using SixLabors.ImageSharp.Image<Rgba32> round =
            await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(bmp, default);

        round.Width.ShouldBe(8);
        round.Height.ShouldBe(8);

        // BMP is lossless, so the checkerboard pattern must survive pixel-for-pixel.
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                round[x, y].ShouldBe(src[x, y]);
            }
        }
    }

    [Fact]
    public async Task Rejects_unsupported_destination_extension()
    {
        await using MemoryStream srcStream = await PngBytesAsync(4, 4);
        await using MemoryStream dst = new();

        await Should.ThrowAsync<UnsupportedConversionException>(async () =>
            await _handler.ConvertAsync(".png", srcStream, ".unknown", dst, new MediaContext(), default));
    }

    [Fact]
    public async Task Same_media_handler_gate_dispatches_image_to_image_via_router()
    {
        // Sanity check: the router resolves the singleton ImageMediaHandler for
        // both source and destination extensions, so SameMediaHandlerGate's
        // ReferenceEquals(srcHandler, dstHandler) check succeeds.
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddVitriolImage();
        ServiceProvider sp = services.BuildServiceProvider();

        IFormatRegistry registry = sp.GetRequiredService<IFormatRegistry>();
        IMediaHandler? pngHandler = registry.GetMediaHandler(".png");
        IMediaHandler? jpgHandler = registry.GetMediaHandler(".jpg");

        pngHandler.ShouldNotBeNull();
        jpgHandler.ShouldNotBeNull();
        ReferenceEquals(pngHandler, jpgHandler).ShouldBeTrue();

        // End-to-end via router.
        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();
        string dir = Path.Combine(Path.GetTempPath(), $"vitriol-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string srcPath = Path.Combine(dir, "in.png");
            string dstPath = Path.Combine(dir, "out.jpg");

            using (MemoryStream ms = await PngBytesAsync(32, 32))
            {
                await File.WriteAllBytesAsync(srcPath, ms.ToArray());
            }

            ConversionJob job = new(srcPath, dstPath, ".png", ".jpg");
            await router.ConvertAsync(job, progress: null, default);

            File.Exists(dstPath).ShouldBeTrue();
            new FileInfo(dstPath).Length.ShouldBeGreaterThan(0);

            // Output is parseable as JPEG.
            await using FileStream fs = File.OpenRead(dstPath);
            using SixLabors.ImageSharp.Image probe =
                await SixLabors.ImageSharp.Image.LoadAsync(fs, default);
            probe.Width.ShouldBe(32);
            probe.Height.ShouldBe(32);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Cross_category_image_to_text_wraps_in_textdoc_with_origin_sidecar()
    {
        // .png → .txt: router's CrossCategoryImageToDocumentGate must wrap the
        // PNG bytes as a single-block TextDoc with the origin sidecar, then
        // PlainTextHandler renders an "[image]" placeholder. The origin
        // bytes are dropped because plain text has no way to carry them — but
        // the test verifies the dispatch fires and produces a file.
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddVitriolImage();
        services.AddVitriolText();
        ServiceProvider sp = services.BuildServiceProvider();

        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();
        string dir = Path.Combine(Path.GetTempPath(), $"vitriol-img2txt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string srcPath = Path.Combine(dir, "in.png");
            string dstPath = Path.Combine(dir, "out.txt");
            using (MemoryStream ms = await PngBytesAsync(4, 4))
            {
                await File.WriteAllBytesAsync(srcPath, ms.ToArray());
            }

            ConversionJob job = new(srcPath, dstPath, ".png", ".txt");
            await router.ConvertAsync(job, progress: null, default);

            File.Exists(dstPath).ShouldBeTrue();
            string text = await File.ReadAllTextAsync(dstPath);
            text.ShouldContain("[image");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static async ValueTask<MemoryStream> PngBytesAsync(int width, int height)
    {
        using SixLabors.ImageSharp.Image<Rgba32> img = BuildCheckerboard(width, height);
        MemoryStream ms = new();
        await img.SaveAsPngAsync(ms);
        ms.Position = 0;
        return ms;
    }

    private static SixLabors.ImageSharp.Image<Rgba32> BuildCheckerboard(int width, int height)
    {
        SixLabors.ImageSharp.Image<Rgba32> img = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool light = ((x ^ y) & 1) == 0;
                img[x, y] = light ? new Rgba32(255, 255, 255, 255) : new Rgba32(0, 0, 0, 255);
            }
        }
        return img;
    }
}
