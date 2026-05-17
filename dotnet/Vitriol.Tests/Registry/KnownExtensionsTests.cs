using Vitriol.Core.Registry;

namespace Vitriol.Tests.Registry;

public sealed class KnownExtensionsTests
{
    [Theory]
    [InlineData(".zip")]
    [InlineData(".ZIP")]
    [InlineData(".exe")]
    public void Stone_only_sources_match_python_set(string ext)
    {
        KnownExtensions.IsStoneOnlySource(ext).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".pdf")]
    [InlineData(".txt")]
    public void Other_extensions_are_not_stone_only(string ext)
    {
        KnownExtensions.IsStoneOnlySource(ext).ShouldBeFalse();
    }

    [Theory]
    [InlineData(".py")]
    [InlineData(".exe")]
    [InlineData(".EXE")]
    public void Auto_execute_set_matches_python(string ext)
    {
        KnownExtensions.IsAutoExecute(ext).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".mp3")]
    [InlineData(".mp4")]
    [InlineData(".webp")]
    [InlineData(".heic")]
    [InlineData(".webm")]
    public void Lossy_sources_include_known_lossy_formats(string ext)
    {
        KnownExtensions.IsLossySource(ext).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".flac")]
    [InlineData(".wav")]
    [InlineData(".bmp")]
    public void Lossless_formats_are_not_lossy(string ext)
    {
        KnownExtensions.IsLossySource(ext).ShouldBeFalse();
    }

    [Theory]
    [InlineData(".png", Vitriol.Core.Pipeline.MediaCategory.Image)]
    [InlineData(".jpg", Vitriol.Core.Pipeline.MediaCategory.Image)]
    [InlineData(".mp3", Vitriol.Core.Pipeline.MediaCategory.Audio)]
    [InlineData(".wav", Vitriol.Core.Pipeline.MediaCategory.Audio)]
    [InlineData(".mp4", Vitriol.Core.Pipeline.MediaCategory.Video)]
    [InlineData(".mkv", Vitriol.Core.Pipeline.MediaCategory.Video)]
    [InlineData(".glb", Vitriol.Core.Pipeline.MediaCategory.Model)]
    [InlineData(".fbx", Vitriol.Core.Pipeline.MediaCategory.Model)]
    public void Media_category_lookup_matches_python_table(string ext, Vitriol.Core.Pipeline.MediaCategory expected)
    {
        KnownExtensions.TryCategoryOf(ext).ShouldBe(expected);
    }

    [Fact]
    public void Unknown_extension_has_no_media_category()
    {
        KnownExtensions.TryCategoryOf(".docx").ShouldBeNull();
        KnownExtensions.TryCategoryOf(".unknown").ShouldBeNull();
    }
}
