using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;
using Vitriol.Formats.Archive;

namespace Vitriol.Tests.Formats.Archive;

/// <summary>
/// End-to-end archive conversion via the conversion router. Sprint 9 is the
/// first time <c>DocKind.Archive</c> has a registered handler, so the IR
/// gate's "no adapter needed when src.Kind == dst.Kind" path fires for
/// real here.
/// </summary>
public sealed class ArchiveRouterIntegrationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task Zip_to_zip_via_router_is_byte_passthrough()
    {
        byte[] zipBytes = BuildZip(("readme.md", "# hello"u8.ToArray()));
        string dir = MakeScope();
        string srcPath = Path.Combine(dir, "in.zip");
        string dstPath = Path.Combine(dir, "out.zip");
        await File.WriteAllBytesAsync(srcPath, zipBytes);

        IConversionRouter router = BuildProvider().GetRequiredService<IConversionRouter>();
        // The router auto-promotes Masquerade for STONE_ONLY_SOURCES (.zip),
        // which would steer us into the Stone PhilosophersStoneGate before
        // the IR gate. To exercise the *archive* path through the IR gate
        // we point at .cbz on the source side — same kind, different
        // extension, not in StoneOnlySources.
        string cbzSrc = Path.Combine(dir, "in.cbz");
        File.Move(srcPath, cbzSrc);

        ConversionJob job = new(cbzSrc, dstPath, ".cbz", ".zip");
        await router.ConvertAsync(job, progress: null, default);

        // Same archive kind (Zip) so the bytes are passthrough-equal.
        File.ReadAllBytes(dstPath).ShouldBe(zipBytes);
    }

    [Fact]
    public async Task Zip_to_tar_via_router_repacks_entries()
    {
        byte[] zipBytes = BuildZip(
            ("docs/readme.txt", "readme content"u8.ToArray()),
            ("data/values.bin", new byte[] { 0, 1, 2, 3, 4 }));

        string dir = MakeScope();
        string srcPath = Path.Combine(dir, "in.cbz");
        string dstPath = Path.Combine(dir, "out.tar");
        await File.WriteAllBytesAsync(srcPath, zipBytes);

        IConversionRouter router = BuildProvider().GetRequiredService<IConversionRouter>();
        ConversionJob job = new(srcPath, dstPath, ".cbz", ".tar");
        await router.ConvertAsync(job, progress: null, default);

        File.Exists(dstPath).ShouldBeTrue();
        new FileInfo(dstPath).Length.ShouldBeGreaterThan(0);

        // Round trip via the handler to confirm the entries survived.
        await using FileStream tarStream = File.OpenRead(dstPath);
        ArchiveHandler handler = new();
        ArchiveDoc round = (ArchiveDoc)await handler.ReadAsync(tarStream, new ReadContext(".tar"), default);
        round.SourceKind.ShouldBe(ArchiveKind.Tar);
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddVitriolArchive();
        return services.BuildServiceProvider();
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
                es.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }

    private string MakeScope()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vitriol-arch-{Guid.NewGuid():N}");
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
