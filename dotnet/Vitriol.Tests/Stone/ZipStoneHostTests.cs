using System.IO.Compression;
using Vitriol.Stone;
using Vitriol.Stone.Hosts;

namespace Vitriol.Tests.Stone;

public sealed class ZipStoneHostTests
{
    private readonly ZipStoneHost _host = new();

    [Fact]
    public async Task Round_trip_recovers_payload_and_extension()
    {
        byte[] payload = "music payload"u8.ToArray();

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".m4a", dst, new StoneOptions(), default);

        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".zip", new StoneOptions(), default);

        result.RecoveredExtension.ShouldBe(".m4a");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Emits_real_zip_with_single_member_named_original_dot_ext()
    {
        await using MemoryStream dst = new();
        await _host.EmbedAsync("payload"u8.ToArray(), ".pdf", dst, new StoneOptions(), default);

        dst.Position = 0;
        using ZipArchive zip = new(dst, ZipArchiveMode.Read);
        zip.Entries.Count.ShouldBe(1);
        zip.Entries[0].FullName.ShouldBe("original.pdf");
    }

    [Fact]
    public async Task Has_envelope_true_for_single_member_original_archive()
    {
        await using MemoryStream stream = new();
        await _host.EmbedAsync("p"u8.ToArray(), ".bin", stream, new StoneOptions(), default);

        stream.Position = 0;
        bool detected = await _host.HasEnvelopeAsync(stream, ".zip", default);
        detected.ShouldBeTrue();
    }

    [Fact]
    public async Task Has_envelope_false_for_multi_member_archive()
    {
        await using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("a.txt").Open().Dispose();
            zip.CreateEntry("b.txt").Open().Dispose();
        }
        stream.Position = 0;
        bool detected = await _host.HasEnvelopeAsync(stream, ".zip", default);
        detected.ShouldBeFalse();
    }

    [Fact]
    public async Task Has_envelope_false_for_wrong_member_name()
    {
        await using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("notoriginal.bin").Open().Dispose();
        }
        stream.Position = 0;
        bool detected = await _host.HasEnvelopeAsync(stream, ".zip", default);
        detected.ShouldBeFalse();
    }

    [Fact]
    public async Task Extract_rejects_zip_with_more_than_one_member()
    {
        await using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("original.txt").Open().Dispose();
            zip.CreateEntry("extra.txt").Open().Dispose();
        }
        stream.Position = 0;

        StoneEnvelopeException ex = await Should.ThrowAsync<StoneEnvelopeException>(async () =>
            await _host.ExtractAsync(stream, ".zip", new StoneOptions(), default));

        ex.Message.ShouldContain("expected one member");
    }

    [Fact]
    public async Task Extract_rejects_member_with_parent_directory_traversal()
    {
        // Zip-Slip: attacker crafts a ZIP whose only entry is named
        // "../../../etc/passwd". The Python implementation doesn't guard
        // against this; the .NET port does.
        await using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("../etc/passwd").Open().Dispose();
        }
        stream.Position = 0;

        StoneEnvelopeException ex = await Should.ThrowAsync<StoneEnvelopeException>(async () =>
            await _host.ExtractAsync(stream, ".zip", new StoneOptions(), default));

        ex.Message.ShouldContain("parent-directory");
    }

    [Fact]
    public async Task Extract_rejects_absolute_member_name()
    {
        await using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("/absolute/path.bin").Open().Dispose();
        }
        stream.Position = 0;

        StoneEnvelopeException ex = await Should.ThrowAsync<StoneEnvelopeException>(async () =>
            await _host.ExtractAsync(stream, ".zip", new StoneOptions(), default));

        ex.Message.ShouldContain("absolute");
    }
}
