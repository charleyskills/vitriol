using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;

namespace Vitriol.Core.Routing;

/// <summary>
/// Compiler mode — when the target is <c>.py</c> and the job sets
/// <see cref="ConversionJob.Compiler"/>, embed the source via Stone into a
/// self-extracting Python script. Lossy sources are refused. Mirrors
/// <c>router.py:144-156</c>.
///
/// Depends on <see cref="IStoneEngine"/>. Registered by Sprint 3's
/// <c>AddVitriolStone()</c>.
/// </summary>
public sealed class CompilerGate : IRoutingGate
{
    private readonly IStoneEngine _stone;

    public CompilerGate(IStoneEngine stone)
    {
        ArgumentNullException.ThrowIfNull(stone);
        _stone = stone;
    }

    public int Order => 20;

    public string Name => "Compiler";

    public async ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Job.Compiler || context.Job.DestinationExtension != ".py")
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        if (KnownExtensions.IsLossySource(context.Job.SourceExtension))
        {
            return new RoutingDecision.Refused(
                Name,
                $"Compiler mode requires a lossless source. {context.Job.SourceExtension} "
                + "is a lossy format (the round-trip would not reconstruct the original). "
                + "Untick Compiler to convert as plain text, or convert your source to a "
                + "lossless format first.");
        }

        await using FileStream src = File.OpenRead(context.Job.Source);
        await using FileStream dst = File.Create(context.Job.Destination);
        await _stone.EmbedAsync(
            src,
            context.Job.SourceExtension,
            dst,
            context.Job.DestinationExtension,
            BuildStoneOptions(context),
            cancellationToken).ConfigureAwait(false);

        return new RoutingDecision.Handled(Name);
    }

    private static StoneOptions BuildStoneOptions(RoutingContext context) => new()
    {
        Password = context.Job.Password,
        CrossCategory = IsCrossCategory(context),
        Progress = context.Progress,
    };

    private static bool IsCrossCategory(RoutingContext context)
    {
        MediaCategory? src = context.Registry.CategoryOf(context.Job.SourceExtension);
        MediaCategory? dst = context.Registry.CategoryOf(context.Job.DestinationExtension);
        return src != dst;
    }
}
