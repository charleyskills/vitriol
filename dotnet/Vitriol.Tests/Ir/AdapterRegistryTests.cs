using System.Text;

namespace Vitriol.Tests.Ir;

public sealed class AdapterRegistryTests
{
    [Fact]
    public void Adapts_text_to_binary_via_utf8()
    {
        TextDoc doc = new(EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(new Run("hello")))));

        AdapterRegistry registry = new();
        registry.TryAdapt(doc, DocKind.Binary, progress: null, out IDocument? result).ShouldBeTrue();
        BinaryDoc binary = result.ShouldBeOfType<BinaryDoc>();
        Encoding.UTF8.GetString(binary.Bytes.Span).ShouldBe("hello");
        binary.Mime.ShouldStartWith("text/plain");
    }

    [Fact]
    public void Adapts_binary_to_text_with_utf8_decode()
    {
        byte[] payload = Encoding.UTF8.GetBytes("first\n\nsecond");
        BinaryDoc binary = new(payload, "text/plain");

        AdapterRegistry registry = new();
        registry.TryAdapt(binary, DocKind.Text, progress: null, out IDocument? result).ShouldBeTrue();
        TextDoc doc = result.ShouldBeOfType<TextDoc>();
        doc.Blocks.Count.ShouldBe(2);
        ((Paragraph)doc.Blocks[0]).Runs[0].Text.ShouldBe("first");
        ((Paragraph)doc.Blocks[1]).Runs[0].Text.ShouldBe("second");
    }

    [Fact]
    public void Binary_to_text_emits_warning_on_invalid_utf8()
    {
        // 0xFF is invalid as a start byte in UTF-8.
        byte[] bad = { 0xFF, 0xFE, 0x41 };
        BinaryDoc binary = new(bad, "application/octet-stream");

        List<ConversionEvent> events = new();
        SyncProgress<ConversionEvent> progress = new(events.Add);

        AdapterRegistry registry = new();
        registry.TryAdapt(binary, DocKind.Text, progress, out _).ShouldBeTrue();

        events.OfType<ConversionEvent.Warning>().ShouldNotBeEmpty();
        events.OfType<ConversionEvent.Warning>().First().Message.ShouldContain("UTF-8");
    }

    [Fact]
    public void Returns_false_for_unsupported_kind_pair()
    {
        TextDoc doc = TextDoc.Empty;
        AdapterRegistry registry = new();

        // text → text is the identity, not a registered adapter.
        registry.TryAdapt(doc, DocKind.Text, progress: null, out _).ShouldBeFalse();
    }
}
