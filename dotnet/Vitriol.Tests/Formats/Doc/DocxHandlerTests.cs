using Vitriol.Core.Pipeline;
using Vitriol.Formats.Doc;

namespace Vitriol.Tests.Formats.Doc;

public sealed class DocxHandlerTests
{
    private readonly DocxHandler _handler = new();

    [Fact]
    public void Reports_text_kind_and_docx_extension()
    {
        _handler.Kind.ShouldBe(DocKind.Text);
        _handler.SupportedExtensions.ShouldContain(".docx");
    }

    [Fact]
    public async Task Write_then_read_round_trips_heading_and_paragraph()
    {
        TextDoc input = new(EquatableArray.Create<Block>(
            new Heading(1, EquatableArray.Create(new Run("Sprint 12"))),
            new Paragraph(EquatableArray.Create(new Run("Body text in a paragraph.")))));

        await using MemoryStream dst = new();
        await _handler.WriteAsync(input, dst, new WriteContext(".docx"), default);

        dst.Position = 0;
        TextDoc round = (TextDoc)await _handler.ReadAsync(dst, new ReadContext(".docx"), default);

        round.Blocks.Count.ShouldBeGreaterThanOrEqualTo(2);
        Heading h = round.Blocks[0].ShouldBeOfType<Heading>();
        h.Level.ShouldBe(1);
        h.Runs[0].Text.ShouldBe("Sprint 12");
        round.Blocks[1].ShouldBeOfType<Paragraph>().Runs[0].Text.ShouldContain("Body text");
    }

    [Fact]
    public async Task Write_then_read_preserves_inline_bold_and_italic()
    {
        TextDoc input = new(EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(
                new Run("Normal "),
                new Run("bold", RunStyle.Bold),
                new Run(" and "),
                new Run("italic", RunStyle.Italic),
                new Run(".")))));

        await using MemoryStream dst = new();
        await _handler.WriteAsync(input, dst, new WriteContext(".docx"), default);

        dst.Position = 0;
        TextDoc round = (TextDoc)await _handler.ReadAsync(dst, new ReadContext(".docx"), default);
        Paragraph p = round.Blocks[0].ShouldBeOfType<Paragraph>();

        bool sawBold = false, sawItalic = false;
        foreach (Run r in p.Runs)
        {
            if (r.Bold) { sawBold = true; }
            if (r.Italic) { sawItalic = true; }
        }
        sawBold.ShouldBeTrue();
        sawItalic.ShouldBeTrue();
    }

    [Fact]
    public async Task Trailer_envelope_round_trip_recovers_payload_byte_exact()
    {
        // Build a TextDoc with origin sidecar set. Write to DOCX; reopen as ZIP;
        // verify _vitriol/original.bin exists and contains the payload via
        // a parseable UCMSv1 envelope.
        byte[] payload = new byte[256];
        new Random(13).NextBytes(payload);

        TextDoc input = new TextDoc(EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(new Run("placeholder")))),
            Vitriol.Core.Ir.DocumentMetadata.Empty
                .WithOrigin(new Vitriol.Core.Ir.VitriolOrigin(payload, ".png")));

        await using MemoryStream dst = new();
        await _handler.WriteAsync(input, dst, new WriteContext(".docx"), default);

        // Read it back via the trailer reader.
        dst.Position = 0;
        Vitriol.Formats.Doc.Trailer.DocxTrailerEnvelopeReader reader = new();
        TrailerEnvelopeResult? result = await reader.TryReadAsync(dst, ".docx", default);

        result.ShouldNotBeNull();
        result!.RecoveredExtension.ShouldBe(".png");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Empty_body_yields_empty_textdoc()
    {
        // Build an empty DOCX (no body content) and verify read yields a
        // TextDoc with no/empty paragraphs.
        TextDoc input = new(EquatableArray<Block>.Empty);
        await using MemoryStream dst = new();
        await _handler.WriteAsync(input, dst, new WriteContext(".docx"), default);

        dst.Position = 0;
        TextDoc round = (TextDoc)await _handler.ReadAsync(dst, new ReadContext(".docx"), default);
        round.Blocks.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Rejects_non_textdoc_input()
    {
        await using MemoryStream dst = new();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(Tabular.Empty, dst, new WriteContext(".docx"), default));
    }

    [Fact]
    public async Task Table_round_trips()
    {
        TextDoc input = new(EquatableArray.Create<Block>(
            new TableBlock(EquatableArray.Create(
                EquatableArray.Create(
                    EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("a")))),
                    EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("b"))))),
                EquatableArray.Create(
                    EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("c")))),
                    EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("d")))))))));

        await using MemoryStream dst = new();
        await _handler.WriteAsync(input, dst, new WriteContext(".docx"), default);

        dst.Position = 0;
        TextDoc round = (TextDoc)await _handler.ReadAsync(dst, new ReadContext(".docx"), default);
        TableBlock t = round.Blocks[0].ShouldBeOfType<TableBlock>();
        t.Rows.Count.ShouldBe(2);
        t.Rows[0].Count.ShouldBe(2);
    }
}
