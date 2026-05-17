using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Registry;
using Vitriol.Core.Routing;

namespace Vitriol.Tests.Routing;

public sealed class PolicyGateTests
{
    private static IFormatRegistry EmptyRegistry()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        ServiceProvider sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IFormatRegistry>();
    }

    [Theory]
    [InlineData(".py", ".exe")]
    [InlineData(".exe", ".py")]
    [InlineData(".py", ".py")]
    [InlineData(".exe", ".exe")]
    public async Task Refuses_py_and_exe_both_ends(string srcExt, string dstExt)
    {
        PolicyGate gate = new();
        RoutingContext context = new(
            new ConversionJob("/tmp/in", "/tmp/out", srcExt, dstExt),
            EmptyRegistry(),
            progress: null);

        RoutingDecision decision = await gate.TryHandleAsync(context, default);

        decision.ShouldBeOfType<RoutingDecision.Refused>().Reason.ShouldContain("malware-wrapping");
    }

    [Fact]
    public async Task Promotes_masquerade_for_zip_source()
    {
        PolicyGate gate = new();
        RoutingContext context = new(
            new ConversionJob("/tmp/in.zip", "/tmp/out.png", ".zip", ".png"),
            EmptyRegistry(),
            progress: null);

        RoutingDecision decision = await gate.TryHandleAsync(context, default);

        decision.ShouldBeOfType<RoutingDecision.NotApplicable>();
        context.Job.Masquerade.ShouldBeTrue();
    }

    [Fact]
    public async Task Leaves_masquerade_alone_for_non_stone_source()
    {
        PolicyGate gate = new();
        RoutingContext context = new(
            new ConversionJob("/tmp/in.png", "/tmp/out.jpg", ".png", ".jpg"),
            EmptyRegistry(),
            progress: null);

        RoutingDecision decision = await gate.TryHandleAsync(context, default);

        decision.ShouldBeOfType<RoutingDecision.NotApplicable>();
        context.Job.Masquerade.ShouldBeFalse();
    }
}
