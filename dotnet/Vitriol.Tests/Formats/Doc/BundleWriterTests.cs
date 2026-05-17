using Vitriol.Formats.Doc.Bundle;

namespace Vitriol.Tests.Formats.Doc;

public sealed class BundleWriterTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task Flat_destination_when_no_embedded_images()
    {
        TextDoc doc = new(EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(new Run("just text")))));

        string dir = MakeScope();
        string dst = Path.Combine(dir, "article.md");

        BundleWriter.Plan plan = await BundleWriter.PrepareAsync(doc, dst, default);
        plan.MarkdownPath.ShouldBe(dst);
        plan.BundledDoc.ShouldBe(doc);
    }

    [Fact]
    public async Task Embedded_image_lands_in_images_folder_with_relative_href()
    {
        byte[] imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        TextDoc doc = new(EquatableArray.Create<Block>(
            new ImageBlock(imageBytes, "image/png", "diagram")));

        string dir = MakeScope();
        string dst = Path.Combine(dir, "article.md");

        BundleWriter.Plan plan = await BundleWriter.PrepareAsync(doc, dst, default);

        // The markdown lands inside <dir>/article/.
        plan.MarkdownPath.ShouldBe(Path.Combine(dir, "article", "article.md"));
        Directory.Exists(Path.Combine(dir, "article", "images")).ShouldBeTrue();

        // The image file exists with the right extension.
        File.Exists(Path.Combine(dir, "article", "images", "diagram.png")).ShouldBeTrue();
        byte[] written = File.ReadAllBytes(Path.Combine(dir, "article", "images", "diagram.png"));
        written.ShouldBe(imageBytes);

        // The IR has the href rewritten.
        ImageBlock rewritten = plan.BundledDoc.Blocks[0].ShouldBeOfType<ImageBlock>();
        rewritten.Href.ShouldBe("images/diagram.png");
    }

    [Fact]
    public async Task Image_alt_collisions_get_numeric_suffixes()
    {
        byte[] a = new byte[] { 1 };
        byte[] b = new byte[] { 2 };
        TextDoc doc = new(EquatableArray.Create<Block>(
            new ImageBlock(a, "image/png", "diagram"),
            new ImageBlock(b, "image/png", "diagram")));

        string dir = MakeScope();
        string dst = Path.Combine(dir, "article.md");

        BundleWriter.Plan plan = await BundleWriter.PrepareAsync(doc, dst, default);

        File.Exists(Path.Combine(dir, "article", "images", "diagram.png")).ShouldBeTrue();
        File.Exists(Path.Combine(dir, "article", "images", "diagram_2.png")).ShouldBeTrue();
    }

    [Fact]
    public async Task Images_inside_lists_tables_blockquotes_get_collected()
    {
        byte[] data = new byte[] { 1, 2, 3 };
        ImageBlock listImage = new(data, "image/png", "in_list");
        ImageBlock tableImage = new(data, "image/png", "in_cell");
        ImageBlock quoteImage = new(data, "image/png", "in_quote");

        TextDoc doc = new(EquatableArray.Create<Block>(
            new ListBlock(false, EquatableArray.Create(
                EquatableArray.Create<Block>(listImage))),
            new TableBlock(EquatableArray.Create(
                EquatableArray.Create(
                    EquatableArray.Create<Block>(tableImage)))),
            new Blockquote(EquatableArray.Create<Block>(quoteImage))));

        string dir = MakeScope();
        string dst = Path.Combine(dir, "report.md");

        await BundleWriter.PrepareAsync(doc, dst, default);

        File.Exists(Path.Combine(dir, "report", "images", "in_list.png")).ShouldBeTrue();
        File.Exists(Path.Combine(dir, "report", "images", "in_cell.png")).ShouldBeTrue();
        File.Exists(Path.Combine(dir, "report", "images", "in_quote.png")).ShouldBeTrue();
    }

    private string MakeScope()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vitriol-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (string d in _tempDirs)
        {
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
        }
    }
}
