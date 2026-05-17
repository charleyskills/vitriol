using System.IO.Compression;
using System.Text;
using Vitriol.Core.Detection.Sniffers;

namespace Vitriol.Tests.Detection;

public sealed class ZipSubtypeSnifferTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public void Plain_zip_returns_dot_zip()
    {
        string path = MakeZip(entries: new[]
        {
            ("hello.txt", "hi"u8.ToArray()),
        });
        ZipSubtypeSniffer.Probe(path).ShouldBe(".zip");
    }

    [Fact]
    public void Docx_detected_by_word_subfolder_and_content_types()
    {
        string path = MakeZip(entries: new[]
        {
            ("[Content_Types].xml", "<?xml version=\"1.0\"?>"u8.ToArray()),
            ("word/document.xml", "<?xml?>"u8.ToArray()),
        });
        ZipSubtypeSniffer.Probe(path).ShouldBe(".docx");
    }

    [Fact]
    public void Xlsx_detected_by_xl_subfolder()
    {
        string path = MakeZip(entries: new[]
        {
            ("[Content_Types].xml", "<?xml version=\"1.0\"?>"u8.ToArray()),
            ("xl/workbook.xml", "<?xml?>"u8.ToArray()),
        });
        ZipSubtypeSniffer.Probe(path).ShouldBe(".xlsx");
    }

    [Fact]
    public void Pptx_detected_by_ppt_subfolder()
    {
        string path = MakeZip(entries: new[]
        {
            ("[Content_Types].xml", "<?xml version=\"1.0\"?>"u8.ToArray()),
            ("ppt/presentation.xml", "<?xml?>"u8.ToArray()),
        });
        ZipSubtypeSniffer.Probe(path).ShouldBe(".pptx");
    }

    [Fact]
    public void Epub_detected_by_mimetype_entry()
    {
        string path = MakeZip(entries: new[]
        {
            ("mimetype", Encoding.ASCII.GetBytes("application/epub+zip")),
            ("META-INF/container.xml", "<?xml?>"u8.ToArray()),
        });
        ZipSubtypeSniffer.Probe(path).ShouldBe(".epub");
    }

    [Fact]
    public void Odt_detected_by_odf_mimetype()
    {
        string path = MakeZip(entries: new[]
        {
            ("mimetype", Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.text")),
            ("content.xml", "<?xml?>"u8.ToArray()),
        });
        ZipSubtypeSniffer.Probe(path).ShouldBe(".odt");
    }

    [Fact]
    public void Falls_back_to_zip_for_missing_file()
    {
        ZipSubtypeSniffer.Probe("/tmp/this/path/does/not/exist-vitriol-test.zip").ShouldBe(".zip");
    }

    private string MakeZip((string Name, byte[] Bytes)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"vitriol-zipsubtype-{Guid.NewGuid():N}.zip");
        _tempFiles.Add(path);

        using FileStream fs = File.Create(path);
        using ZipArchive zip = new(fs, ZipArchiveMode.Create);
        foreach ((string name, byte[] bytes) in entries)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using Stream es = entry.Open();
            es.Write(bytes, 0, bytes.Length);
        }
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
