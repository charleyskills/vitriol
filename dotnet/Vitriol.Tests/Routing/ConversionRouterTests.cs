using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;
using Vitriol.Core.Routing;
using static Vitriol.Tests.Routing.RoutingMocks;

namespace Vitriol.Tests.Routing;

public sealed class ConversionRouterTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task Same_handler_media_pair_dispatches_to_handler()
    {
        FakeMediaHandler imageHandler = new(MediaCategory.Image, ".png", ".jpg");
        ServiceProvider sp = BuildProvider(s =>
        {
            s.AddSingleton<IMediaHandler>(imageHandler);
        });
        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();

        ConversionJob job = MakeJob(".png", ".jpg");
        await router.ConvertAsync(job, null, default);

        imageHandler.ConvertCalls.ShouldBe(1);
        imageHandler.LastSrcExt.ShouldBe(".png");
        imageHandler.LastDstExt.ShouldBe(".jpg");
    }

    [Fact]
    public async Task Cross_category_image_to_document_wraps_bytes_in_textdoc()
    {
        FakeMediaHandler imageHandler = new(MediaCategory.Image, ".png");
        FakeFormatWriter pdfWriter = new(DocKind.Text, ".pdf");

        ServiceProvider sp = BuildProvider(s =>
        {
            s.AddSingleton<IMediaHandler>(imageHandler);
            s.AddSingleton<IFormatWriter>(pdfWriter);
        });

        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();
        ConversionJob job = MakeJob(".png", ".pdf");
        await router.ConvertAsync(job, null, default);

        pdfWriter.WriteCalls.ShouldBe(1);
        pdfWriter.LastDocument.ShouldBeOfType<TextDoc>();
        TextDoc tdoc = (TextDoc)pdfWriter.LastDocument!;
        tdoc.Blocks.Count.ShouldBe(1);
        tdoc.Blocks[0].ShouldBeOfType<ImageBlock>();
        tdoc.Metadata.Origin.ShouldNotBeNull();
        tdoc.Metadata.Origin!.Extension.ShouldBe(".png");
    }

    [Fact]
    public async Task Whole_file_ir_path_dispatches_reader_then_writer()
    {
        FakeFormatReader txtReader = new(DocKind.Text, ".txt");
        FakeFormatWriter mdWriter = new(DocKind.Text, ".md");

        ServiceProvider sp = BuildProvider(s =>
        {
            s.AddSingleton<IFormatReader>(txtReader);
            s.AddSingleton<IFormatWriter>(mdWriter);
        });

        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();
        ConversionJob job = MakeJob(".txt", ".md");
        await router.ConvertAsync(job, null, default);

        txtReader.ReadCalls.ShouldBe(1);
        mdWriter.WriteCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Policy_block_for_py_to_exe_throws_UnsupportedConversion()
    {
        ServiceProvider sp = BuildProvider();
        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();
        ConversionJob job = MakeJob(".py", ".exe");

        await Should.ThrowAsync<UnsupportedConversionException>(async () =>
            await router.ConvertAsync(job, null, default));
    }

    [Fact]
    public async Task Stone_only_source_promotes_masquerade_then_dispatches_via_stone_gate()
    {
        FakeStoneEngine stone = new();
        stone.EmbedTargets.Add(".png");

        ServiceProvider sp = BuildProvider(s =>
        {
            s.AddSingleton<IStoneEngine>(stone);
            // Register the Stone-aware gate manually (Sprint 3 will add a DI helper).
            s.AddSingleton<IRoutingGate, PhilosophersStoneGate>();
        });

        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();
        ConversionJob job = MakeJob(".zip", ".png");
        await router.ConvertAsync(job, null, default);

        stone.EmbedCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Trailer_envelope_recovery_writes_payload_directly()
    {
        // .pdf source carries a recovered .png payload; destination is .png.
        // The trailer gate's direct-write branch fires because the recovered
        // extension matches the destination AND .png is in the hardcoded
        // MediaCategoryOf table, so the gate considers the destination "media".
        byte[] payload = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
        FakeTrailerReader trailer = new(".pdf", new TrailerEnvelopeResult(payload, ".png"));

        ServiceProvider sp = BuildProvider(s =>
        {
            s.AddSingleton<ITrailerEnvelopeReader>(trailer);
        });
        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();

        string srcPath = WriteTempFile(new byte[1], ".pdf");
        string dstPath = TempPath(".png");
        ConversionJob job = new(srcPath, dstPath, ".pdf", ".png");
        await router.ConvertAsync(job, null, default);

        File.ReadAllBytes(dstPath).ShouldBe(payload);
    }

    [Fact]
    public async Task Unsupported_pair_with_no_handlers_throws()
    {
        ServiceProvider sp = BuildProvider();
        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();
        ConversionJob job = MakeJob(".txt", ".md");

        await Should.ThrowAsync<UnsupportedConversionException>(async () =>
            await router.ConvertAsync(job, null, default));
    }

    [Fact]
    public async Task Cross_category_document_to_media_auto_engages_stone()
    {
        FakeStoneEngine stone = new();
        stone.EmbedTargets.Add(".png");
        FakeMediaHandler imageHandler = new(MediaCategory.Image, ".png");

        ServiceProvider sp = BuildProvider(s =>
        {
            s.AddSingleton<IStoneEngine>(stone);
            s.AddSingleton<IMediaHandler>(imageHandler);
            s.AddSingleton<IRoutingGate, CrossCategoryDocumentToMediaGate>();
        });

        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();
        ConversionJob job = MakeJob(".txt", ".png");
        await router.ConvertAsync(job, null, default);

        stone.EmbedCalls.ShouldBe(1);
    }

    private ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private ConversionJob MakeJob(string srcExt, string dstExt)
    {
        string srcPath = WriteTempFile(new byte[] { 0 }, srcExt);
        string dstPath = TempPath(dstExt);
        return new ConversionJob(srcPath, dstPath, srcExt, dstExt);
    }

    private string WriteTempFile(byte[] bytes, string extension)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vitriol-router-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        string path = Path.Combine(dir, $"src{extension}");
        File.WriteAllBytes(path, bytes);
        _tempFiles.Add(path);
        return path;
    }

    private string TempPath(string extension)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vitriol-router-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        string path = Path.Combine(dir, $"dst{extension}");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string f in _tempFiles)
        {
            try { File.Delete(f); } catch (IOException) { }
        }
        foreach (string d in _tempDirs)
        {
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
        }
    }
}
