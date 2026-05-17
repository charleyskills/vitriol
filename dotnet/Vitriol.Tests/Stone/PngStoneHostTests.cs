using System.Buffers.Binary;
using Vitriol.Stone;
using Vitriol.Stone.Hosts;

namespace Vitriol.Tests.Stone;

public sealed class PngStoneHostTests
{
    private readonly PngStoneHost _host = new();

    [Fact]
    public async Task Round_trip_recovers_payload_and_extension()
    {
        byte[] payload = "embedded inside a PNG ucMs chunk"u8.ToArray();

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".pdf", dst, new StoneOptions(), default);

        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".png", new StoneOptions(), default);

        result.RecoveredExtension.ShouldBe(".pdf");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Emits_a_real_png_with_valid_signature_and_structure()
    {
        await using MemoryStream dst = new();
        await _host.EmbedAsync("p"u8.ToArray(), ".txt", dst, new StoneOptions(), default);

        byte[] bytes = dst.ToArray();
        // PNG signature
        bytes[..8].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // First chunk is IHDR with 13 bytes of data + 4-byte CRC; tag at offset 8+4 = 12.
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)).ShouldBe((uint)13);
        System.Text.Encoding.ASCII.GetString(bytes, 12, 4).ShouldBe("IHDR");
        // 1×1 image
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)).ShouldBe((uint)1);
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)).ShouldBe((uint)1);
        bytes[24].ShouldBe((byte)8); // bit depth
        bytes[25].ShouldBe((byte)6); // RGBA
    }

    [Fact]
    public async Task Contains_ucMs_chunk_with_envelope_magic()
    {
        await using MemoryStream dst = new();
        await _host.EmbedAsync("p"u8.ToArray(), ".bin", dst, new StoneOptions(), default);

        byte[] bytes = dst.ToArray();

        // Walk chunks, find one tagged "ucMs", verify its data starts with the v1 magic.
        int p = 8;
        bool found = false;
        while (p + 12 <= bytes.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(p, 4));
            string tag = System.Text.Encoding.ASCII.GetString(bytes, p + 4, 4);
            if (tag == "ucMs")
            {
                bytes.AsSpan(p + 8, 7).SequenceEqual(
                    new byte[] { (byte)'U', (byte)'C', (byte)'M', (byte)'S',
                                 (byte)'v', (byte)'1', 0x00 }).ShouldBeTrue();
                found = true;
                break;
            }
            p += 12 + len;
        }
        found.ShouldBeTrue();
    }

    [Fact]
    public async Task Has_envelope_true_for_stone_png()
    {
        await using MemoryStream stream = new();
        await _host.EmbedAsync("p"u8.ToArray(), ".bin", stream, new StoneOptions(), default);

        stream.Position = 0;
        bool detected = await _host.HasEnvelopeAsync(stream, ".png", default);
        detected.ShouldBeTrue();
    }

    [Fact]
    public async Task Has_envelope_false_for_plain_png()
    {
        // Build a plain 1×1 PNG with no ucMs chunk via the host's own embed,
        // then strip the ucMs chunk by feeding extract a header-only PNG.
        // Simpler approach: take the embedded bytes and walk to find ucMs,
        // then construct a PNG without it.
        await using MemoryStream withChunk = new();
        await _host.EmbedAsync(new byte[] { 1 }, ".x", withChunk, new StoneOptions(), default);
        byte[] full = withChunk.ToArray();

        // Strip the ucMs chunk: find its start (length(4) + tag(4) + data + crc(4)) and remove.
        int p = 8;
        int chunkStart = -1;
        int chunkLen = 0;
        while (p + 12 <= full.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(full.AsSpan(p, 4));
            string tag = System.Text.Encoding.ASCII.GetString(full, p + 4, 4);
            if (tag == "ucMs")
            {
                chunkStart = p;
                chunkLen = 12 + len;
                break;
            }
            p += 12 + len;
        }
        chunkStart.ShouldBeGreaterThan(-1);

        byte[] stripped = new byte[full.Length - chunkLen];
        Array.Copy(full, 0, stripped, 0, chunkStart);
        Array.Copy(full, chunkStart + chunkLen, stripped, chunkStart, full.Length - chunkStart - chunkLen);

        await using MemoryStream s = new(stripped);
        bool detected = await _host.HasEnvelopeAsync(s, ".png", default);
        detected.ShouldBeFalse();
    }
}
