namespace Vitriol.Tests.Ir;

public sealed class ImageBytesToTextDocTests
{
    [Fact]
    public void Wraps_image_bytes_and_stashes_origin_sidecar()
    {
        ReadOnlyMemory<byte> bytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        };

        TextDoc doc = ImageBytesToTextDoc.Wrap(bytes, ".png", alt: "hero");

        doc.Blocks.Count.ShouldBe(1);
        ImageBlock image = doc.Blocks[0].ShouldBeOfType<ImageBlock>();
        image.Mime.ShouldBe("image/png");
        image.Alt.ShouldBe("hero");

        doc.Metadata.Origin.ShouldNotBeNull();
        doc.Metadata.Origin!.Extension.ShouldBe(".png");
        doc.Metadata.Origin.Bytes.Span.SequenceEqual(bytes.Span).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".heic", "image/heic")]
    [InlineData(".svg", "image/svg+xml")]
    [InlineData(".unknown", "image/png")] // fallback for unknown ext
    public void Mime_is_inferred_from_extension(string ext, string expectedMime)
    {
        TextDoc doc = ImageBytesToTextDoc.Wrap(new byte[] { 1, 2, 3 }, ext);
        ImageBlock image = doc.Blocks[0].ShouldBeOfType<ImageBlock>();
        image.Mime.ShouldBe(expectedMime);
    }

    [Fact]
    public void Accepts_extension_without_leading_dot()
    {
        TextDoc doc = ImageBytesToTextDoc.Wrap(new byte[] { 1 }, "png");
        doc.Blocks[0].ShouldBeOfType<ImageBlock>().Mime.ShouldBe("image/png");
        doc.Metadata.Origin!.Extension.ShouldBe(".png");
    }
}
