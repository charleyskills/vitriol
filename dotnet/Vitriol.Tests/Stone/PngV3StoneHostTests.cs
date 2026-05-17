using Vitriol.Core.Pipeline;
using Vitriol.Stone;
using Vitriol.Stone.Carriers;
using Vitriol.Stone.Hosts;

namespace Vitriol.Tests.Stone;

/// <summary>
/// PNG v3 (Mandelbrot LSB scatter pack) host tests. The v3 path is triggered
/// when <see cref="StoneOptions.CrossCategory"/> is true or a password is
/// supplied — otherwise the host falls back to the v1 ucMs-chunk path
/// (covered by <see cref="PngStoneHostTests"/>).
/// </summary>
public sealed class PngV3StoneHostTests
{
    private readonly PngStoneHost _host = new();

    [Fact]
    public async Task V3_round_trip_with_password_recovers_payload()
    {
        byte[] payload = "Encrypted v3 carrier — Sprint 10."u8.ToArray();
        StoneOptions options = new()
        {
            Password = "chopin"u8.ToArray(),
            CrossCategory = true,
        };

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".bin", dst, options, default);

        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".png", options, default);

        result.RecoveredExtension.ShouldBe(".bin");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task V3_round_trip_passwordless_still_succeeds_when_cross_category()
    {
        byte[] payload = "no password, just cross-category"u8.ToArray();
        StoneOptions options = new() { CrossCategory = true };

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".txt", dst, options, default);

        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".png", options, default);

        result.RecoveredExtension.ShouldBe(".txt");
        result.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task Same_source_and_password_produce_identical_png_bytes()
    {
        // Determinism property: the v3 carrier is fully deterministic from
        // (source, password). Two embeds of the same input must yield byte-
        // identical PNG output (so SHA-256 of output is reproducible).
        byte[] payload = "deterministic"u8.ToArray();
        StoneOptions options = new()
        {
            Password = "stable"u8.ToArray(),
            CrossCategory = true,
        };

        await using MemoryStream a = new();
        await _host.EmbedAsync(payload, ".bin", a, options, default);

        await using MemoryStream b = new();
        await _host.EmbedAsync(payload, ".bin", b, options, default);

        a.ToArray().ShouldBe(b.ToArray());
    }

    [Fact]
    public async Task Wrong_password_v3_extract_yields_garbage_no_error()
    {
        byte[] payload = "secret"u8.ToArray();
        StoneOptions right = new()
        {
            Password = "rightpass"u8.ToArray(),
            CrossCategory = true,
        };
        StoneOptions wrong = new()
        {
            Password = "wrongpass"u8.ToArray(),
            CrossCategory = true,
        };

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".bin", dst, right, default);

        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".png", wrong, default);

        // No-oracle property: extract returns *something* rather than throwing.
        // The recovered bytes do NOT match the original payload.
        result.Payload.ToArray().ShouldNotBe(payload);
    }

    [Fact]
    public async Task Has_envelope_returns_true_for_v3_carrier()
    {
        byte[] payload = "stone"u8.ToArray();
        StoneOptions options = new() { CrossCategory = true };

        await using MemoryStream stream = new();
        await _host.EmbedAsync(payload, ".bin", stream, options, default);

        stream.Position = 0;
        (await _host.HasEnvelopeAsync(stream, ".png", default)).ShouldBeTrue();
    }

    [Fact]
    public async Task V3_carrier_output_is_a_valid_png_with_expected_dimensions()
    {
        byte[] payload = "valid png check"u8.ToArray();
        StoneOptions options = new()
        {
            Password = "x"u8.ToArray(),
            CrossCategory = true,
        };

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".bin", dst, options, default);

        // Compute expected dimensions: tiny payload → 1080×1080 tier.
        (int expectedW, int expectedH) = MandelbrotDims.ForEnvelope(payload.Length + 50 /* envelope overhead */);
        expectedW.ShouldBe(1080);
        expectedH.ShouldBe(1080);

        dst.Position = 0;
        MandelbrotPngCodec.Decoded decoded = await MandelbrotPngCodec.ReadRgbAsync(dst, default);
        decoded.Width.ShouldBe(expectedW);
        decoded.Height.ShouldBe(expectedH);
    }

    [Fact]
    public async Task V1_fallback_when_no_options_set()
    {
        // No password, no cross-category → v1 ucMs-chunk path. The carrier
        // is a 1×1 PNG and the extract still recovers the payload.
        byte[] payload = "v1 path"u8.ToArray();
        StoneOptions options = new();

        await using MemoryStream dst = new();
        await _host.EmbedAsync(payload, ".bin", dst, options, default);

        // 1×1 PNG is much smaller than a v3 carrier.
        dst.Length.ShouldBeLessThan(1024);

        dst.Position = 0;
        StoneExtractionResult result = await _host.ExtractAsync(dst, ".png", options, default);
        result.Payload.ToArray().ShouldBe(payload);
    }
}
