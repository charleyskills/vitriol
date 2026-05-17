using Vitriol.Core.Pipeline;
using Vitriol.Formats.Doc;

namespace Vitriol.Tests.Formats.Doc;

public sealed class PdfHandlerTests
{
    private readonly PdfHandler _handler = new();

    [Fact]
    public void Reports_text_kind_and_pdf_extension()
    {
        _handler.Kind.ShouldBe(DocKind.Text);
        _handler.SupportedExtensions.ShouldContain(".pdf");
    }

    [Fact]
    public async Task Write_then_read_round_trips_paragraph_text()
    {
        TextDoc input = new(EquatableArray.Create<Block>(
            new Heading(1, EquatableArray.Create(new Run("Sprint 12 PDF Test"))),
            new Paragraph(EquatableArray.Create(new Run("Body text in a paragraph.")))));

        await using MemoryStream dst = new();
        await _handler.WriteAsync(input, dst, new WriteContext(".pdf"), default);

        // Verify output is a real PDF (starts with %PDF-).
        byte[] bytes = dst.ToArray();
        bytes.Length.ShouldBeGreaterThan(0);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");

        // Read it back via the handler.
        dst.Position = 0;
        TextDoc round = (TextDoc)await _handler.ReadAsync(dst, new ReadContext(".pdf"), default);
        round.Blocks.Count.ShouldBeGreaterThan(0);
        // QuestPDF lays the heading and paragraph onto the same page, so the
        // extracted text contains both phrases concatenated.
        string allText = string.Join("\n", round.Blocks.OfType<Paragraph>()
            .Select(p => string.Concat(p.Runs.AsSpan().ToArray().Select(r => r.Text))));
        allText.ShouldContain("Sprint 12");
        allText.ShouldContain("Body text");
    }

    [Fact]
    public async Task Trailer_envelope_appended_after_eof()
    {
        // Set up a TextDoc with origin sidecar and verify the trailer reader
        // recovers the payload from the written PDF.
        byte[] payload = new byte[128];
        new Random(7).NextBytes(payload);

        TextDoc input = new TextDoc(EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(new Run("placeholder")))),
            Vitriol.Core.Ir.DocumentMetadata.Empty
                .WithOrigin(new Vitriol.Core.Ir.VitriolOrigin(payload, ".wav")));

        await using MemoryStream dst = new();
        await _handler.WriteAsync(input, dst, new WriteContext(".pdf"), default);

        // PDF signature is still present.
        byte[] bytes = dst.ToArray();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");

        // Trailer reader picks up the appended envelope.
        dst.Position = 0;
        Vitriol.Formats.Doc.Trailer.PdfTrailerEnvelopeReader reader = new();
        TrailerEnvelopeResult? result = await reader.TryReadAsync(dst, ".pdf", default);

        result.ShouldNotBeNull();
        result!.RecoveredExtension.ShouldBe(".wav");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Read_emits_warning_for_suspiciously_low_extraction()
    {
        // A 100KB+ "PDF" with no text content should fire the low-extraction
        // warning. Easiest path: write a minimal but mostly-empty PDF.
        TextDoc empty = new(EquatableArray<Block>.Empty);
        await using MemoryStream dst = new();
        await _handler.WriteAsync(empty, dst, new WriteContext(".pdf"), default);

        // Pad the file to >100KB by appending null bytes after the trailer.
        // This is still a valid PDF (trailing bytes are ignored), and the
        // text-extraction heuristic should fire.
        byte[] padding = new byte[120 * 1024];
        await dst.WriteAsync(padding);

        dst.Position = 0;
        List<ConversionEvent> events = new();
        IProgress<ConversionEvent> progress = new SyncProgress(events.Add);
        ReadContext ctx = new(".pdf") { Progress = progress };
        await _handler.ReadAsync(dst, ctx, default);

        events.OfType<ConversionEvent.Warning>()
            .Any(w => w.Message.Contains("extraction"))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Rejects_non_textdoc_input()
    {
        await using MemoryStream dst = new();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(Tabular.Empty, dst, new WriteContext(".pdf"), default));
    }

    private sealed class SyncProgress : IProgress<ConversionEvent>
    {
        private readonly Action<ConversionEvent> _h;
        public SyncProgress(Action<ConversionEvent> h) => _h = h;
        public void Report(ConversionEvent value) => _h(value);
    }
}
