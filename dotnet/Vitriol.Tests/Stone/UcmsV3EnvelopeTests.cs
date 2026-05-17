using FsCheck.Xunit;
using Vitriol.Stone.Envelope;

namespace Vitriol.Tests.Stone;

public sealed class UcmsV3EnvelopeTests
{
    [Fact]
    public void V3_build_parse_round_trip()
    {
        byte[] payload = "round-trip payload"u8.ToArray();
        ReadOnlyMemory<byte> password = "secret"u8.ToArray();

        UcmsV3Envelope original = new(Width: 1024, Height: 768, Extension: ".pdf", Payload: payload);
        byte[] built = original.Build(password);

        UcmsV3Envelope parsed = UcmsV3Envelope.Parse(built, password);

        parsed.Width.ShouldBe(1024);
        parsed.Height.ShouldBe(768);
        parsed.Extension.ShouldBe(".pdf");
        parsed.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void V3_same_source_and_password_produce_identical_envelope()
    {
        byte[] payload = "deterministic"u8.ToArray();
        ReadOnlyMemory<byte> password = "chopin"u8.ToArray();

        UcmsV3Envelope env = new(1024, 1024, ".m4a", payload);
        byte[] a = env.Build(password);
        byte[] b = env.Build(password);

        a.ShouldBe(b);
    }

    [Fact]
    public void V3_wrong_password_yields_no_error_returns_garbage()
    {
        byte[] payload = "real payload"u8.ToArray();
        ReadOnlyMemory<byte> right = "rightpass"u8.ToArray();
        ReadOnlyMemory<byte> wrong = "wrongpass"u8.ToArray();

        UcmsV3Envelope original = new(800, 600, ".png", payload);
        byte[] built = original.Build(right);

        UcmsV3Envelope garbage = UcmsV3Envelope.Parse(built, wrong);

        garbage.Width.ShouldBe(800);
        garbage.Height.ShouldBe(600);
        // Payload bytes are garbled; only the header (W/H) was unencrypted.
        garbage.Payload.ToArray().ShouldNotBe(payload);
    }

    [Fact]
    public void V3_header_layout_matches_python_spec()
    {
        // magic(8) + W(4 BE) + H(4 BE) + IV(16) + salt(4) = 36 bytes header.
        UcmsV3Envelope env = new(1, 2, ".x", Array.Empty<byte>());
        byte[] built = env.Build(ReadOnlyMemory<byte>.Empty);

        UcmsV3Envelope.HeaderSize.ShouldBe(36);
        built.AsSpan(0, 8).SequenceEqual(UcmsMagic.V3).ShouldBeTrue();
        // W
        built[8].ShouldBe((byte)0);
        built[9].ShouldBe((byte)0);
        built[10].ShouldBe((byte)0);
        built[11].ShouldBe((byte)1);
        // H
        built[12].ShouldBe((byte)0);
        built[13].ShouldBe((byte)0);
        built[14].ShouldBe((byte)0);
        built[15].ShouldBe((byte)2);
        // salt field is at offset 32..36 and reserved zeros.
        built[32].ShouldBe((byte)0);
        built[33].ShouldBe((byte)0);
        built[34].ShouldBe((byte)0);
        built[35].ShouldBe((byte)0);
    }

    [Fact]
    public void V3_parse_too_short_envelope_fails_gracefully()
    {
        bool ok = UcmsV3Envelope.TryParse(new byte[10], ReadOnlyMemory<byte>.Empty,
            out _, out string? error);
        ok.ShouldBeFalse();
        error.ShouldContain("too short");
    }

    [Property(MaxTest = 30)]
    public bool V3_round_trip_arbitrary_payloads(byte[] payload)
    {
        payload ??= Array.Empty<byte>();
        ReadOnlyMemory<byte> password = "p"u8.ToArray();
        UcmsV3Envelope env = new(64, 64, ".bin", payload);
        byte[] built = env.Build(password);
        UcmsV3Envelope parsed = UcmsV3Envelope.Parse(built, password);
        return parsed.Extension == ".bin"
            && parsed.Payload.Span.SequenceEqual(payload);
    }
}
