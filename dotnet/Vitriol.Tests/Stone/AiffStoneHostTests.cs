using System.Buffers.Binary;
using System.Text;
using Vitriol.Stone;
using Vitriol.Stone.Hosts;

namespace Vitriol.Tests.Stone;

public sealed class AiffStoneHostTests
{
    private readonly AiffStoneHost _host = new();

    [Fact]
    public async Task Round_trip_recovers_payload_and_extension()
    {
        byte[] payload = "audio carrier hidden in aiff ssnd"u8.ToArray();

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".pdf", dst, new StoneOptions(), default);

        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".aiff", new StoneOptions(), default);

        result.RecoveredExtension.ShouldBe(".pdf");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Emits_real_form_aiff_with_comm_and_ssnd_chunks()
    {
        await using MemoryStream dst = new();
        await _host.EmbedAsync("p"u8.ToArray(), ".bin", dst, new StoneOptions(), default);

        byte[] bytes = dst.ToArray();
        Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("FORM");
        Encoding.ASCII.GetString(bytes, 8, 4).ShouldBe("AIFF");
        Encoding.ASCII.GetString(bytes, 12, 4).ShouldBe("COMM");

        // COMM body size big-endian 4 bytes; AIFF v1 is exactly 18.
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)).ShouldBe((uint)18);

        // COMM body: channels(2 BE) frames(4 BE) sampleSize(2 BE) sampleRate(10)
        BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(20, 2)).ShouldBe((short)1); // mono
        BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(26, 2)).ShouldBe((short)16); // 16-bit

        // SSND chunk header at offset 38 (after FORM header 12 + COMM header 8 + COMM body 18).
        Encoding.ASCII.GetString(bytes, 38, 4).ShouldBe("SSND");
    }

    [Theory]
    [InlineData(8000u)]
    [InlineData(44100u)]
    [InlineData(48000u)]
    [InlineData(96000u)]
    public void Extended_float_round_trip_for_sample_rates(uint rate)
    {
        byte[] encoded = AiffStoneHost.EncodeExtendedFloat(rate);
        encoded.Length.ShouldBe(10);
        AiffStoneHost.DecodeExtendedFloat(encoded).ShouldBe(rate);
    }

    [Fact]
    public void Extended_float_zero_is_ten_zero_bytes()
    {
        AiffStoneHost.EncodeExtendedFloat(0).ShouldBe(new byte[10]);
        AiffStoneHost.DecodeExtendedFloat(new byte[10]).ShouldBe(0u);
    }

    [Fact]
    public async Task Has_envelope_true_for_stone_aiff()
    {
        await using MemoryStream stream = new();
        await _host.EmbedAsync("payload"u8.ToArray(), ".bin", stream, new StoneOptions(), default);

        stream.Position = 0;
        bool detected = await _host.HasEnvelopeAsync(stream, ".aiff", default);
        detected.ShouldBeTrue();
    }

    [Fact]
    public async Task Extract_fails_on_non_aiff()
    {
        await using MemoryStream stream = new(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        await Should.ThrowAsync<StoneEnvelopeException>(async () =>
            await _host.ExtractAsync(stream, ".aiff", new StoneOptions(), default));
    }

    [Fact]
    public void Supports_aif_alias()
    {
        _host.SupportedExtensions.ShouldContain(".aif");
        _host.SupportedExtensions.ShouldContain(".aiff");
    }
}
