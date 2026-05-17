using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Registry;
using Vitriol.Stone;
using Vitriol.Stone.Hosts;

namespace Vitriol.Tests.Stone;

public sealed class StoneEngineTests
{
    private static IStoneEngine BuildEngine()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddVitriolStone();
        return services.BuildServiceProvider().GetRequiredService<IStoneEngine>();
    }

    [Fact]
    public void Registered_extensions_include_all_v1_hosts()
    {
        IStoneEngine engine = BuildEngine();
        engine.CanEmbedInto(".txt").ShouldBeTrue();
        engine.CanEmbedInto(".zip").ShouldBeTrue();
        engine.CanEmbedInto(".png").ShouldBeTrue();
        engine.CanEmbedInto(".wav").ShouldBeTrue();
        engine.CanEmbedInto(".aiff").ShouldBeTrue();
        engine.CanEmbedInto(".aif").ShouldBeTrue();
    }

    [Fact]
    public void Unregistered_extensions_return_false()
    {
        IStoneEngine engine = BuildEngine();
        engine.CanEmbedInto(".wav").ShouldBeFalse();
        engine.CanEmbedInto(".pdf").ShouldBeFalse();
    }

    [Fact]
    public void Lossy_source_detection_matches_known_extensions_table()
    {
        IStoneEngine engine = BuildEngine();
        engine.IsLossySource(".jpg").ShouldBeTrue();
        engine.IsLossySource(".mp3").ShouldBeTrue();
        engine.IsLossySource(".png").ShouldBeFalse();
        engine.IsLossySource(".wav").ShouldBeFalse();
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".zip")]
    [InlineData(".png")]
    [InlineData(".wav")]
    [InlineData(".aiff")]
    public async Task End_to_end_embed_then_extract_recovers_bytes(string hostExt)
    {
        IStoneEngine engine = BuildEngine();
        byte[] payload = new byte[1024];
        new Random(13).NextBytes(payload);

        await using MemoryStream src = new(payload);
        await using MemoryStream carrier = new();
        await engine.EmbedAsync(src, ".bin", carrier, hostExt, new(), default);

        carrier.Position = 0;
        bool detected = await engine.HasEnvelopeAsync(carrier, hostExt, default);
        detected.ShouldBeTrue();

        carrier.Position = 0;
        await using MemoryStream recovered = new();
        await engine.ExtractAsync(carrier, hostExt, recovered, ".bin", new(), default);

        recovered.ToArray().ShouldBe(payload);
    }
}
