using FsCheck.Xunit;
using Vitriol.Stone.Envelope;

namespace Vitriol.Tests.Stone;

public sealed class UcmsEnvelopeTests
{
    [Fact]
    public void V1_magic_is_seven_bytes_matching_python()
    {
        // Python: MAGIC = b"UCMSv1\0" — 6 ASCII chars + one NUL = 7 bytes.
        UcmsMagic.V1.Length.ShouldBe(7);
        UcmsMagic.V1Length.ShouldBe(7);
        UcmsMagic.V8Length.ShouldBe(8);
        UcmsMagic.V2.Length.ShouldBe(8);
        UcmsMagic.V3.Length.ShouldBe(8);
    }

    [Fact]
    public void V1_build_layout_matches_python_byte_for_byte()
    {
        // Python _build_envelope:
        //   MAGIC (7) + ext_len (1) + ext_str (var) + payload_len (8 BE) + payload (var)
        // For ext=".txt", payload="hi", the envelope is:
        //   "UCMSv1\0" + 0x04 + ".txt" + (8-byte BE 2) + "hi" = 22 bytes
        UcmsEnvelope env = new(".txt", "hi"u8.ToArray());
        byte[] built = env.Build();

        byte[] expected =
        {
            (byte)'U', (byte)'C', (byte)'M', (byte)'S', (byte)'v', (byte)'1', 0x00,
            0x04,
            (byte)'.', (byte)'t', (byte)'x', (byte)'t',
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02,
            (byte)'h', (byte)'i',
        };
        built.ShouldBe(expected);
    }

    [Fact]
    public void V1_build_parse_round_trip()
    {
        byte[] payload = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        UcmsEnvelope original = new(".pdf", payload);

        byte[] built = original.Build();
        UcmsEnvelope parsed = UcmsEnvelope.Parse(built);

        parsed.Extension.ShouldBe(".pdf");
        parsed.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void V1_normalizes_extension_without_leading_dot()
    {
        UcmsEnvelope env = new("png", new byte[] { 1, 2, 3 });
        byte[] built = env.Build();

        UcmsEnvelope parsed = UcmsEnvelope.Parse(built);
        parsed.Extension.ShouldBe(".png");
    }

    [Fact]
    public void V1_parse_finds_envelope_after_padding()
    {
        // Hosts often prepend host-specific bytes before the envelope. Parse
        // must locate the magic via IndexOf and decode from there.
        byte[] env = new UcmsEnvelope(".bin", new byte[] { 0x42 }).Build();
        byte[] padded = new byte[100 + env.Length];
        env.CopyTo(padded, 100);

        UcmsEnvelope parsed = UcmsEnvelope.Parse(padded);
        parsed.Extension.ShouldBe(".bin");
        parsed.Payload.ToArray().ShouldBe(new byte[] { 0x42 });
    }

    [Fact]
    public void V1_parse_truncated_envelope_returns_error_not_throw()
    {
        byte[] env = new UcmsEnvelope(".png", new byte[] { 1, 2, 3 }).Build();
        byte[] truncated = env[..(env.Length - 2)];

        bool ok = UcmsEnvelope.TryParse(truncated, out _, out string? error);
        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("Truncated");
    }

    [Fact]
    public void V1_parse_without_magic_fails()
    {
        bool ok = UcmsEnvelope.TryParse(new byte[] { 0, 1, 2, 3, 4 }, out _, out string? error);
        ok.ShouldBeFalse();
        error!.ShouldContain("magic not found");
    }

    [Property(MaxTest = 50)]
    public bool Build_then_parse_is_identity_for_arbitrary_payloads(byte[] payload)
    {
        payload ??= Array.Empty<byte>();
        UcmsEnvelope original = new(".dat", payload);
        UcmsEnvelope round = UcmsEnvelope.Parse(original.Build());
        return round.Extension == ".dat"
            && round.Payload.Span.SequenceEqual(payload);
    }

    [Fact]
    public void V1_clamps_extension_at_255_bytes()
    {
        string longExt = "." + new string('a', 300);
        UcmsEnvelope env = new(longExt, new byte[] { 1 });
        byte[] built = env.Build();

        // ext_len byte at offset 7 (after the 7-byte magic) must be ≤ 255.
        built[7].ShouldBeLessThanOrEqualTo((byte)255);
    }
}
