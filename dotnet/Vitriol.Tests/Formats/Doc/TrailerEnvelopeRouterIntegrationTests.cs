using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;
using Vitriol.Formats.Doc;
using Vitriol.Formats.Image;

namespace Vitriol.Tests.Formats.Doc;

/// <summary>
/// End-to-end: PNG → DOCX → PNG byte-perfect via the router, exercising the
/// Sprint 2 <c>TrailerEnvelopeGate</c> and Sprint 1's <c>_vitriol_origin</c>
/// sidecar plumbing for the first time. This is the headline new capability
/// Sprint 12 unlocks.
/// </summary>
public sealed class TrailerEnvelopeRouterIntegrationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task Png_to_docx_to_png_round_trips_byte_perfect()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddVitriolImage();
        services.AddVitriolDoc();
        ServiceProvider sp = services.BuildServiceProvider();

        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();

        // Build a small valid PNG.
        string dir = MakeScope();
        string srcPng = Path.Combine(dir, "in.png");
        await File.WriteAllBytesAsync(srcPng, BuildTinyRgbaPng());
        byte[] originalBytes = await File.ReadAllBytesAsync(srcPng);

        // Forward: PNG → DOCX. The cross-category image→doc gate wraps the
        // PNG bytes as a TextDoc with the origin sidecar; the DOCX writer
        // stashes the sidecar at _vitriol/original.bin.
        string docxPath = Path.Combine(dir, "out.docx");
        ConversionJob forward = new(srcPng, docxPath, ".png", ".docx");
        await router.ConvertAsync(forward, progress: null, default);

        File.Exists(docxPath).ShouldBeTrue();

        // Reverse: DOCX → PNG. The trailer-envelope gate runs first, finds
        // _vitriol/original.bin, parses the envelope, and writes payload
        // bytes directly. No PDF re-rendering happens.
        string recoveredPng = Path.Combine(dir, "recovered.png");
        ConversionJob reverse = new(docxPath, recoveredPng, ".docx", ".png");
        await router.ConvertAsync(reverse, progress: null, default);

        // The recovered PNG should be byte-identical to the original.
        byte[] recoveredBytes = await File.ReadAllBytesAsync(recoveredPng);
        recoveredBytes.ShouldBe(originalBytes);
    }

    [Fact]
    public async Task Wav_to_pdf_to_wav_round_trips_byte_perfect()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddVitriolDoc();
        ServiceProvider sp = services.BuildServiceProvider();

        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();

        // Fabricate a small "WAV" file (the contents are opaque to the
        // trailer-envelope round trip).
        string dir = MakeScope();
        string srcWav = Path.Combine(dir, "in.wav");
        byte[] originalBytes = new byte[1024];
        new Random(31).NextBytes(originalBytes);
        // Make sure it starts with the RIFF/WAVE signature so format detection
        // classifies it as a media file even without a handler registered.
        "RIFF"u8.CopyTo(originalBytes);
        "WAVE"u8.CopyTo(originalBytes.AsSpan(8));
        await File.WriteAllBytesAsync(srcWav, originalBytes);

        // .wav → .pdf cannot route through the image-to-document gate (the
        // source isn't an image). Instead the cross-category audio→doc path
        // would normally fail. To exercise the trailer-envelope sidecar on
        // the PDF side, we construct the conversion as if a Vitriol-produced
        // PDF were the source and recover.
        //
        // Simulate it by manually writing a PDF with origin via the handler,
        // then converting back through the router.
        PdfHandler handler = new();
        TextDoc docWithOrigin = new TextDoc(EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(new Run("placeholder")))),
            Vitriol.Core.Ir.DocumentMetadata.Empty
                .WithOrigin(new Vitriol.Core.Ir.VitriolOrigin(originalBytes, ".wav")));

        string pdfPath = Path.Combine(dir, "carrier.pdf");
        await using (FileStream fs = File.Create(pdfPath))
        {
            await handler.WriteAsync(docWithOrigin, fs, new WriteContext(".pdf"), default);
        }

        // Reverse via the router: PDF → WAV. Trailer-envelope gate recovers.
        string recoveredWav = Path.Combine(dir, "recovered.wav");
        ConversionJob reverse = new(pdfPath, recoveredWav, ".pdf", ".wav");
        await router.ConvertAsync(reverse, progress: null, default);

        byte[] recoveredBytes = await File.ReadAllBytesAsync(recoveredWav);
        recoveredBytes.ShouldBe(originalBytes);
    }

    private string MakeScope()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vitriol-trailer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static byte[] BuildTinyRgbaPng()
    {
        // 1×1 fully-transparent RGBA PNG, hand-rolled to avoid pulling
        // ImageSharp into this test.
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        byte[] ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 1);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // RGBA
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;

        byte[] pixel = new byte[] { 0, 0, 0, 0, 0 };
        byte[] compressed;
        using (MemoryStream ms = new())
        {
            using (System.IO.Compression.ZLibStream z = new(ms,
                System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(pixel);
            }
            compressed = ms.ToArray();
        }

        using MemoryStream output = new();
        output.Write(signature);
        WriteChunk(output, "IHDR"u8.ToArray(), ihdr);
        WriteChunk(output, "IDAT"u8.ToArray(), compressed);
        WriteChunk(output, "IEND"u8.ToArray(), Array.Empty<byte>());
        return output.ToArray();

        static void WriteChunk(Stream s, byte[] tag, byte[] data)
        {
            byte[] length = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
            s.Write(length);
            s.Write(tag);
            s.Write(data);
            System.IO.Hashing.Crc32 crc = new();
            crc.Append(tag);
            crc.Append(data);
            byte[] crcBytes = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc.GetCurrentHashAsUInt32());
            s.Write(crcBytes);
        }
    }

    public void Dispose()
    {
        foreach (string d in _tempDirs)
        {
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
        }
    }
}
