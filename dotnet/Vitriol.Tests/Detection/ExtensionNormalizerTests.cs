using Vitriol.Core.Detection;

namespace Vitriol.Tests.Detection;

public sealed class ExtensionNormalizerTests
{
    [Theory]
    [InlineData("PNG", ".png")]
    [InlineData(".png", ".png")]
    [InlineData(".JPEG", ".jpg")]
    [InlineData("jpeg", ".jpg")]
    [InlineData(".TIF", ".tiff")]
    [InlineData(".htm", ".html")]
    [InlineData(".yml", ".yaml")]
    [InlineData(".aif", ".aiff")]
    [InlineData(".tgz", ".tar.gz")]
    [InlineData(".tbz2", ".tar.bz2")]
    [InlineData(".txz", ".tar.xz")]
    [InlineData(".tzst", ".tar.zst")]
    public void Normalize_returns_canonical_extension(string input, string expected)
    {
        ExtensionNormalizer.Normalize(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("archive.tar.gz", ".tar.gz")]
    [InlineData("backup.TAR.BZ2", ".tar.bz2")]
    [InlineData("photos.tar.xz", ".tar.xz")]
    [InlineData("dump.tar.zst", ".tar.zst")]
    [InlineData("simple.png", ".png")]
    [InlineData("image.JPEG", ".jpg")]
    [InlineData("no-extension", "")]
    public void FromPath_handles_compound_and_simple_suffixes(string path, string expected)
    {
        ExtensionNormalizer.FromPath(path).ShouldBe(expected);
    }

    [Fact]
    public void FromPath_handles_full_paths()
    {
        ExtensionNormalizer.FromPath("/tmp/archive/photo.PNG").ShouldBe(".png");
        ExtensionNormalizer.FromPath("/home/user/data/backup.tar.gz").ShouldBe(".tar.gz");
    }
}
