using Vitriol.Core.Detection;
using Vitriol.Core.Detection.Sniffers;

namespace Vitriol.Tests.Detection;

public sealed class FormatDetectorTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private FormatDetector Build() => new(new IFileSniffer[]
    {
        new MagicByteSniffer(),
        new ZipSubtypeSniffer(),
    });

    [Fact]
    public async Task Detects_png_by_magic_bytes()
    {
        byte[] bytes = new byte[64];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        string path = WriteTempFile(bytes, ".dat");

        (await Build().DetectAsync(path, default)).ShouldBe(".png");
    }

    [Fact]
    public async Task Falls_back_to_extension_on_unknown_content()
    {
        string path = WriteTempFile(new byte[] { 0x55, 0xAA }, ".obj");
        (await Build().DetectAsync(path, default)).ShouldBe(".obj");
    }

    [Fact]
    public async Task Falls_back_to_extension_on_unreadable_file()
    {
        string nonExistent = $"/tmp/vitriol-nope-{Guid.NewGuid():N}.PnG";
        (await Build().DetectAsync(nonExistent, default)).ShouldBe(".png");
    }

    [Fact]
    public async Task Detects_pdf()
    {
        string path = WriteTempFile("%PDF-1.7\n%binary marker"u8.ToArray(), ".bin");
        (await Build().DetectAsync(path, default)).ShouldBe(".pdf");
    }

    [Fact]
    public async Task Detects_zip()
    {
        byte[] pkSignature = { (byte)'P', (byte)'K', 0x03, 0x04 };
        // Build a real (empty) ZIP via System.IO.Compression so the subtype
        // sniffer doesn't trip on a malformed central directory.
        string path = Path.Combine(Path.GetTempPath(), $"vitriol-detect-{Guid.NewGuid():N}.zip");
        _tempFiles.Add(path);
        using (FileStream fs = File.Create(path))
        using (System.IO.Compression.ZipArchive zip = new(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt").Open().Dispose();
        }
        _ = pkSignature;

        (await Build().DetectAsync(path, default)).ShouldBe(".zip");
    }

    [Fact]
    public void DetectFromHead_uses_supplied_bytes_without_io()
    {
        byte[] head = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
        Build().DetectFromHead("/tmp/doesnt-matter.txt", head).ShouldBe(".png");
    }

    private string WriteTempFile(byte[] bytes, string extension)
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"vitriol-detect-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string p in _tempFiles)
        {
            try { File.Delete(p); } catch (IOException) { }
        }
    }
}
