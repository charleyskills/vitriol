using System.IO.Compression;
using System.Text;
using Vitriol.Core.Pipeline;
using Vitriol.Formats.Archive;

namespace Vitriol.Tests.Formats.Archive;

public sealed class ArchiveHandlerTests
{
    private readonly ArchiveHandler _handler = new();

    [Fact]
    public void Reports_archive_kind_and_extension_set()
    {
        _handler.Kind.ShouldBe(DocKind.Archive);
        _handler.SupportedExtensions.ShouldContain(".zip");
        _handler.SupportedExtensions.ShouldContain(".cbz");
        _handler.SupportedExtensions.ShouldContain(".tar");
        _handler.SupportedExtensions.ShouldContain(".tar.gz");
        _handler.SupportedExtensions.ShouldContain(".7z");
        _handler.SupportedExtensions.ShouldContain(".rar");
    }

    [Fact]
    public async Task Read_returns_archive_doc_with_kind_from_extension()
    {
        byte[] zipBytes = BuildZip(("hello.txt", "hi"u8.ToArray()));
        await using MemoryStream src = new(zipBytes);

        IDocument doc = await _handler.ReadAsync(src, new ReadContext(".zip"), default);
        ArchiveDoc archive = doc.ShouldBeOfType<ArchiveDoc>();
        archive.SourceKind.ShouldBe(ArchiveKind.Zip);
        archive.SourceBytes.Length.ShouldBe(zipBytes.Length);
    }

    [Fact]
    public async Task Cbz_read_aliases_to_zip_kind()
    {
        byte[] zipBytes = BuildZip(("page1.png", new byte[] { 1, 2, 3 }));
        await using MemoryStream src = new(zipBytes);

        ArchiveDoc archive = (ArchiveDoc)await _handler.ReadAsync(src, new ReadContext(".cbz"), default);
        archive.SourceKind.ShouldBe(ArchiveKind.Zip);
    }

    [Fact]
    public async Task Same_kind_write_is_byte_passthrough()
    {
        byte[] zipBytes = BuildZip(("a.txt", "alpha"u8.ToArray()), ("b.txt", "beta"u8.ToArray()));
        ArchiveDoc archive = new(zipBytes, ArchiveKind.Zip);

        await using MemoryStream dst = new();
        await _handler.WriteAsync(archive, dst, new WriteContext(".zip"), default);

        dst.ToArray().ShouldBe(zipBytes);
    }

    [Fact]
    public async Task Cross_kind_zip_to_tar_repacks_all_entries()
    {
        byte[] zipBytes = BuildZip(
            ("docs/readme.txt", "readme"u8.ToArray()),
            ("data/values.csv", "a,b\n1,2\n"u8.ToArray()));
        ArchiveDoc archive = new(zipBytes, ArchiveKind.Zip);

        await using MemoryStream dst = new();
        await _handler.WriteAsync(archive, dst, new WriteContext(".tar"), default);

        dst.Position = 0;
        ArchiveDoc round = (ArchiveDoc)await _handler.ReadAsync(dst, new ReadContext(".tar"), default);
        round.SourceKind.ShouldBe(ArchiveKind.Tar);

        Dictionary<string, byte[]> entries = await ReadAllEntriesAsync(round);
        entries.Count.ShouldBe(2);
        entries.ShouldContainKey("docs/readme.txt");
        entries["docs/readme.txt"].ShouldBe("readme"u8.ToArray());
        entries.ShouldContainKey("data/values.csv");
        entries["data/values.csv"].ShouldBe("a,b\n1,2\n"u8.ToArray());
    }

    [Theory]
    [InlineData(".tar.gz")]
    [InlineData(".tar.bz2")]
    public async Task Cross_kind_zip_to_compressed_tar_round_trips(string dstExt)
    {
        byte[] zipBytes = BuildZip(("file.txt", "compressed content"u8.ToArray()));
        ArchiveDoc archive = new(zipBytes, ArchiveKind.Zip);

        await using MemoryStream dst = new();
        await _handler.WriteAsync(archive, dst, new WriteContext(dstExt), default);

        dst.Position = 0;
        ArchiveDoc round = (ArchiveDoc)await _handler.ReadAsync(dst, new ReadContext(dstExt), default);

        Dictionary<string, byte[]> entries = await ReadAllEntriesAsync(round);
        entries.ShouldContainKey("file.txt");
        entries["file.txt"].ShouldBe("compressed content"u8.ToArray());
    }

    [Fact]
    public async Task Write_rejects_rar_with_clear_error()
    {
        ArchiveDoc archive = new(BuildZip(("x.txt", "x"u8.ToArray())), ArchiveKind.Zip);
        await using MemoryStream dst = new();

        UnsupportedConversionException ex = await Should.ThrowAsync<UnsupportedConversionException>(
            async () => await _handler.WriteAsync(archive, dst, new WriteContext(".rar"), default));

        ex.Message.ShouldContain("RAR");
        ex.Message.ShouldContain("closed-source");
    }

    [Fact]
    public async Task Write_rejects_7z_with_explanation()
    {
        ArchiveDoc archive = new(BuildZip(("x.txt", "x"u8.ToArray())), ArchiveKind.Zip);
        await using MemoryStream dst = new();

        UnsupportedConversionException ex = await Should.ThrowAsync<UnsupportedConversionException>(
            async () => await _handler.WriteAsync(archive, dst, new WriteContext(".7z"), default));

        ex.Message.ShouldContain("7Z");
    }

    [Fact]
    public async Task Write_rejects_tar_zst_with_explanation()
    {
        ArchiveDoc archive = new(BuildZip(("x.txt", "x"u8.ToArray())), ArchiveKind.Zip);
        await using MemoryStream dst = new();

        UnsupportedConversionException ex = await Should.ThrowAsync<UnsupportedConversionException>(
            async () => await _handler.WriteAsync(archive, dst, new WriteContext(".tar.zst"), default));

        ex.Message.ShouldContain("TAR.ZST");
    }

    [Fact]
    public async Task Write_rejects_tar_xz_with_explanation()
    {
        // SharpCompress 0.48 does not implement an xz writer for tar archives;
        // writing .tar.xz must fail with a clear message rather than an
        // internal InvalidFormatException from inside SharpCompress.
        ArchiveDoc archive = new(BuildZip(("x.txt", "x"u8.ToArray())), ArchiveKind.Zip);
        await using MemoryStream dst = new();

        UnsupportedConversionException ex = await Should.ThrowAsync<UnsupportedConversionException>(
            async () => await _handler.WriteAsync(archive, dst, new WriteContext(".tar.xz"), default));

        ex.Message.ShouldContain("TAR.XZ");
    }

    [Fact]
    public async Task Write_rejects_non_archive_document()
    {
        await using MemoryStream dst = new();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(Vitriol.Core.Ir.TextDoc.Empty, dst, new WriteContext(".zip"), default));
    }

    [Fact]
    public void Extension_map_truth_table()
    {
        ArchiveKindMap.FromExtension(".zip").ShouldBe(ArchiveKind.Zip);
        ArchiveKindMap.FromExtension(".CBZ").ShouldBe(ArchiveKind.Zip);
        ArchiveKindMap.FromExtension(".tar.gz").ShouldBe(ArchiveKind.TarGz);
        ArchiveKindMap.FromExtension(".7z").ShouldBe(ArchiveKind.SevenZip);
        ArchiveKindMap.FromExtension(".rar").ShouldBe(ArchiveKind.Rar);

        ArchiveKindMap.WritableExtensions.ShouldNotContain(".rar");
        ArchiveKindMap.WritableExtensions.ShouldNotContain(".7z");
        ArchiveKindMap.WritableExtensions.ShouldNotContain(".tar.zst");
        ArchiveKindMap.WritableExtensions.ShouldNotContain(".tar.xz");
        ArchiveKindMap.WritableExtensions.ShouldContain(".zip");
        ArchiveKindMap.WritableExtensions.ShouldContain(".tar.gz");
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

    private static async Task<Dictionary<string, byte[]>> ReadAllEntriesAsync(ArchiveDoc archive)
    {
        Dictionary<string, byte[]> result = new(StringComparer.Ordinal);
        using MemoryStream stream = new(archive.SourceBytes.ToArray(), writable: false);
        using SharpCompress.Readers.IReader reader = SharpCompress.Readers.ReaderFactory.OpenReader(stream, new SharpCompress.Readers.ReaderOptions());
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory)
            {
                continue;
            }
            using MemoryStream buf = new();
            using (Stream es = reader.OpenEntryStream())
            {
                await es.CopyToAsync(buf);
            }
            result[reader.Entry.Key ?? string.Empty] = buf.ToArray();
        }
        return result;
    }
}
