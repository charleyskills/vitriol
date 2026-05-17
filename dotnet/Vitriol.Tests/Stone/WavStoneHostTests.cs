using System.Buffers.Binary;
using System.Text;
using Vitriol.Stone;
using Vitriol.Stone.Hosts;

namespace Vitriol.Tests.Stone;

public sealed class WavStoneHostTests
{
    private readonly WavStoneHost _host = new();

    [Fact]
    public async Task Round_trip_recovers_payload_and_extension()
    {
        byte[] payload = "audio carrier hidden bytes"u8.ToArray();

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".pdf", dst, new StoneOptions(), default);

        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".wav", new StoneOptions(), default);

        result.RecoveredExtension.ShouldBe(".pdf");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Emits_real_riff_wave_pcm_8khz_mono_16bit()
    {
        await using MemoryStream dst = new();
        await _host.EmbedAsync("p"u8.ToArray(), ".bin", dst, new StoneOptions(), default);

        byte[] bytes = dst.ToArray();

        Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("RIFF");
        Encoding.ASCII.GetString(bytes, 8, 4).ShouldBe("WAVE");

        // First chunk is fmt with 16-byte body.
        Encoding.ASCII.GetString(bytes, 12, 4).ShouldBe("fmt ");
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4)).ShouldBe((uint)16);

        // fmt body: format(2) channels(2) rate(4) byterate(4) align(2) bits(2)
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20, 2)).ShouldBe((ushort)1); // PCM
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(22, 2)).ShouldBe((ushort)1); // mono
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24, 4)).ShouldBe((uint)8000); // 8 kHz
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(34, 2)).ShouldBe((ushort)16); // 16-bit

        // data chunk follows immediately.
        Encoding.ASCII.GetString(bytes, 36, 4).ShouldBe("data");
    }

    [Fact]
    public async Task Has_envelope_true_for_stone_wav()
    {
        await using MemoryStream stream = new();
        await _host.EmbedAsync("payload"u8.ToArray(), ".bin", stream, new StoneOptions(), default);

        stream.Position = 0;
        bool detected = await _host.HasEnvelopeAsync(stream, ".wav", default);
        detected.ShouldBeTrue();
    }

    [Fact]
    public async Task Has_envelope_false_for_plain_wav()
    {
        // Build a tiny plain WAV manually: RIFF + WAVE + fmt + data with a
        // few PCM samples that don't contain UCMSv1 magic.
        byte[] plain = BuildPlainWav();
        await using MemoryStream stream = new(plain);
        bool detected = await _host.HasEnvelopeAsync(stream, ".wav", default);
        detected.ShouldBeFalse();
    }

    [Fact]
    public async Task Extract_fails_on_non_wav()
    {
        await using MemoryStream stream = new(new byte[] { 0x00, 0x01, 0x02, 0x03 });
        await Should.ThrowAsync<StoneEnvelopeException>(async () =>
            await _host.ExtractAsync(stream, ".wav", new StoneOptions(), default));
    }

    [Fact]
    public async Task Round_trip_with_odd_length_payload_padding()
    {
        // Test the 16-bit alignment branch: envelope length odd → pad with one
        // trailing zero. Extraction must still recover the original payload.
        byte[] payload = "odd"u8.ToArray(); // 3 bytes; envelope adds 7+1+ext+8+3 — make sure parity works
        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".x", dst, new StoneOptions(), default);

        // RIFF chunks always have even-byte bodies; sanity-check by parsing.
        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".wav", new StoneOptions(), default);
        result.Payload.ToArray().ShouldBe(payload);
        result.RecoveredExtension.ShouldBe(".x");
    }

    private static byte[] BuildPlainWav()
    {
        // 8 kHz mono 16-bit, 4 zero samples.
        byte[] data = new byte[8];
        byte[] result = new byte[44 + data.Length];
        Span<byte> span = result;

        Encoding.ASCII.GetBytes("RIFF").CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)(36 + data.Length));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(span[8..]);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(span[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], 1);  // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], 1);  // mono
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], 8000);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], 16000);
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], 16);
        Encoding.ASCII.GetBytes("data").CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)data.Length);
        return result;
    }
}
