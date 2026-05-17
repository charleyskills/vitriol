using System.IO.Compression;
using Vitriol.Formats.Doc.Trailer;
using Vitriol.Stone.Envelope;

namespace Vitriol.Tests.Formats.Doc;

public sealed class DocxTrailerEnvelopeReaderTests
{
    private readonly DocxTrailerEnvelopeReader _reader = new();

    [Fact]
    public void Can_read_docx_only()
    {
        _reader.CanRead(".docx").ShouldBeTrue();
        _reader.CanRead(".DOCX").ShouldBeTrue();
        _reader.CanRead(".pdf").ShouldBeFalse();
        _reader.CanRead(".epub").ShouldBeFalse();
    }

    [Fact]
    public async Task Returns_null_when_no_vitriol_entry_present()
    {
        byte[] zipBytes = BuildZip(("word/document.xml", "<?xml version=\"1.0\"?>"u8.ToArray()));
        await using MemoryStream src = new(zipBytes);

        TrailerEnvelopeResult? result = await _reader.TryReadAsync(src, ".docx", default);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Parses_envelope_from_vitriol_zip_entry()
    {
        byte[] payload = "hidden payload"u8.ToArray();
        UcmsEnvelope env = new(".bin", payload);
        byte[] envBytes = env.Build();

        byte[] zipBytes = BuildZip(
            ("word/document.xml", "<?xml version=\"1.0\"?>"u8.ToArray()),
            ("_vitriol/original.bin", envBytes));
        await using MemoryStream src = new(zipBytes);

        TrailerEnvelopeResult? result = await _reader.TryReadAsync(src, ".docx", default);
        result.ShouldNotBeNull();
        result!.RecoveredExtension.ShouldBe(".bin");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Returns_null_for_non_zip_input()
    {
        await using MemoryStream src = new("not a zip"u8.ToArray());
        TrailerEnvelopeResult? result = await _reader.TryReadAsync(src, ".docx", default);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Returns_null_for_malformed_envelope_bytes()
    {
        byte[] zipBytes = BuildZip(
            ("_vitriol/original.bin", "not a vitriol envelope"u8.ToArray()));
        await using MemoryStream src = new(zipBytes);

        TrailerEnvelopeResult? result = await _reader.TryReadAsync(src, ".docx", default);
        result.ShouldBeNull();
    }

    private static byte[] BuildZip(params (string Name, byte[] Bytes)[] entries)
    {
        using MemoryStream ms = new();
        using (ZipArchive zip = new(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] bytes) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using Stream es = entry.Open();
                es.Write(bytes);
            }
        }
        return ms.ToArray();
    }
}

public sealed class PdfTrailerEnvelopeReaderTests
{
    private readonly PdfTrailerEnvelopeReader _reader = new();

    [Fact]
    public void Can_read_pdf_only()
    {
        _reader.CanRead(".pdf").ShouldBeTrue();
        _reader.CanRead(".PDF").ShouldBeTrue();
        _reader.CanRead(".docx").ShouldBeFalse();
    }

    [Fact]
    public async Task Parses_envelope_appended_after_eof()
    {
        byte[] payload = "audio bytes"u8.ToArray();
        UcmsEnvelope env = new(".wav", payload);
        byte[] envBytes = env.Build();

        // Simulate a PDF (just a header marker + dummy bytes) with the
        // envelope appended at the end.
        byte[] fakePdfHeader = "%PDF-1.7\n... body bytes ...\n%%EOF\n"u8.ToArray();
        byte[] combined = new byte[fakePdfHeader.Length + envBytes.Length];
        fakePdfHeader.CopyTo(combined, 0);
        envBytes.CopyTo(combined, fakePdfHeader.Length);

        await using MemoryStream src = new(combined);
        TrailerEnvelopeResult? result = await _reader.TryReadAsync(src, ".pdf", default);

        result.ShouldNotBeNull();
        result!.RecoveredExtension.ShouldBe(".wav");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Returns_null_when_no_envelope_in_tail()
    {
        byte[] pdfOnly = "%PDF-1.7\n... no envelope ...\n%%EOF\n"u8.ToArray();
        await using MemoryStream src = new(pdfOnly);

        TrailerEnvelopeResult? result = await _reader.TryReadAsync(src, ".pdf", default);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Returns_null_for_tiny_file_smaller_than_magic()
    {
        await using MemoryStream src = new(new byte[3]);
        TrailerEnvelopeResult? result = await _reader.TryReadAsync(src, ".pdf", default);
        result.ShouldBeNull();
    }
}
