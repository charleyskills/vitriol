using System.Text;
using Vitriol.Stone;
using Vitriol.Stone.Hosts;

namespace Vitriol.Tests.Stone;

public sealed class TxtStoneHostTests
{
    private readonly TxtStoneHost _host = new();

    [Fact]
    public async Task Round_trip_recovers_payload_and_extension()
    {
        byte[] payload = "secret bytes hidden in a base64 dump"u8.ToArray();

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".pdf", dst, new StoneOptions(), default);

        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".txt", new StoneOptions(), default);

        result.RecoveredExtension.ShouldBe(".pdf");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Emits_base64_wrapped_to_76_columns_with_trailing_newline()
    {
        byte[] payload = new byte[200];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 13);
        }

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".bin", dst, new StoneOptions(), default);

        string text = Encoding.UTF8.GetString(dst.ToArray());
        string[] lines = text.Split('\n');

        // Last "line" after split is empty because of trailing \n.
        lines[^1].ShouldBeEmpty();
        // All non-final lines are exactly 76 chars except possibly the last
        // line of content which can be shorter.
        for (int i = 0; i < lines.Length - 2; i++)
        {
            lines[i].Length.ShouldBe(76);
        }
        lines[^2].Length.ShouldBeLessThanOrEqualTo(76);
    }

    [Fact]
    public async Task Has_envelope_returns_true_for_stone_txt()
    {
        await using MemoryStream stream = new();
        await _host.EmbedAsync("payload"u8.ToArray(), ".txt", stream, new StoneOptions(), default);

        stream.Position = 0;
        bool detected = await _host.HasEnvelopeAsync(stream, ".txt", default);
        detected.ShouldBeTrue();
    }

    [Fact]
    public async Task Has_envelope_returns_false_for_plain_text()
    {
        await using MemoryStream stream = new(Encoding.UTF8.GetBytes("Hello world,\nthis is just text.\n"));
        bool detected = await _host.HasEnvelopeAsync(stream, ".txt", default);
        detected.ShouldBeFalse();
    }

    [Fact]
    public async Task Has_envelope_returns_false_for_random_base64_dump()
    {
        // Valid base64 that doesn't begin with our magic — should not detect.
        byte[] random = new byte[200];
        new Random(7).NextBytes(random);
        string text = Convert.ToBase64String(random);
        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(text));
        bool detected = await _host.HasEnvelopeAsync(stream, ".txt", default);
        detected.ShouldBeFalse();
    }

    [Fact]
    public async Task Extracts_when_envelope_has_comment_lines()
    {
        // User pastes the base64 dump under a header comment.
        byte[] payload = "payload"u8.ToArray();
        await using MemoryStream tmp = new();
        await _host.EmbedAsync(payload, ".bin", tmp, new StoneOptions(), default);
        string body = Encoding.UTF8.GetString(tmp.ToArray());

        string withComment = "# This is a Stone TXT file\n" + body;
        await using MemoryStream input = new(Encoding.UTF8.GetBytes(withComment));

        StoneExtractionResult result = await _host.ExtractAsync(input, ".txt", new StoneOptions(), default);
        result.Payload.ToArray().ShouldBe(payload);
    }
}
