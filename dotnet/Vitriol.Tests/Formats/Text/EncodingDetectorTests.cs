using System.Text;
using Vitriol.Core.Pipeline;
using Vitriol.Formats.Text;

namespace Vitriol.Tests.Formats.Text;

public sealed class EncodingDetectorTests
{
    private sealed class CaptureProgress : IProgress<ConversionEvent>
    {
        public List<ConversionEvent> Events { get; } = new();
        public void Report(ConversionEvent value) => Events.Add(value);
    }

    [Fact]
    public void Detects_utf8_bom_and_strips_it()
    {
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("hello")).ToArray();
        CaptureProgress progress = new();

        var result = EncodingDetector.Decode(bytes, progress);

        result.HadBom.ShouldBeTrue();
        result.Text.ShouldBe("hello");
        result.HadReplacements.ShouldBeFalse();
        progress.Events.OfType<ConversionEvent.Warning>().ShouldBeEmpty();
    }

    [Fact]
    public void Strict_utf8_succeeds_without_bom()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("plain ascii then unicode: αβγ");
        CaptureProgress progress = new();

        var result = EncodingDetector.Decode(bytes, progress);

        result.HadBom.ShouldBeFalse();
        result.HadReplacements.ShouldBeFalse();
        result.Text.ShouldBe("plain ascii then unicode: αβγ");
        progress.Events.OfType<ConversionEvent.Warning>().ShouldBeEmpty();
    }

    [Fact]
    public void Non_utf8_bytes_decode_with_replacement_AND_emit_warning()
    {
        // 0xFF is invalid as a UTF-8 start byte; strict decode throws.
        byte[] bytes = { (byte)'h', (byte)'i', 0xFF, (byte)'!' };
        CaptureProgress progress = new();

        var result = EncodingDetector.Decode(bytes, progress);

        result.HadBom.ShouldBeFalse();
        result.HadReplacements.ShouldBeTrue();
        result.Text.ShouldContain("�");
        progress.Events.OfType<ConversionEvent.Warning>().ShouldNotBeEmpty();
        progress.Events.OfType<ConversionEvent.Warning>().First().Message
            .ShouldContain("not valid UTF-8");
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, (byte)'a', 0x00 }, "a")]                 // UTF-16 LE
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, (byte)'a' }, "a")]                 // UTF-16 BE
    public void Detects_utf16_bom(byte[] bytes, string expected)
    {
        var result = EncodingDetector.Decode(bytes);
        result.HadBom.ShouldBeTrue();
        result.Text.ShouldBe(expected);
    }
}
