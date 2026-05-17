using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Routing;

/// <summary>
/// Philosopher's Stone embed/extract. Engages on:
/// <list type="bullet">
/// <item><b>Embed</b>: destination is a Stone host, source is non-lossy, and
/// source/destination are NOT same-handler same-category (PNG→JPG, FBX→GLB are
/// normal media conversions, not Stone).</item>
/// <item><b>Extract</b>: source is a Stone host that actually carries a
/// UCMSv* envelope (probed via <see cref="IStoneEngine.HasEnvelopeAsync"/>).</item>
/// </list>
/// Mirrors <c>router.py:158-183</c>.
/// </summary>
public sealed class PhilosophersStoneGate : IRoutingGate
{
    private readonly IStoneEngine _stone;

    public PhilosophersStoneGate(IStoneEngine stone)
    {
        ArgumentNullException.ThrowIfNull(stone);
        _stone = stone;
    }

    public int Order => 30;

    public string Name => "PhilosophersStone";

    public async ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Job.Masquerade)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        string srcExt = context.Job.SourceExtension;
        string dstExt = context.Job.DestinationExtension;

        IMediaHandler? srcHandler = context.Registry.GetMediaHandler(srcExt);
        IMediaHandler? dstHandler = context.Registry.GetMediaHandler(dstExt);
        bool sameMediaHandler = srcHandler is not null
            && dstHandler is not null
            && ReferenceEquals(srcHandler, dstHandler);

        bool embed = !sameMediaHandler
            && _stone.CanEmbedInto(dstExt)
            && !_stone.IsLossySource(srcExt);

        bool extract = false;
        if (!embed && _stone.CanExtractFrom(srcExt))
        {
            await using FileStream probe = File.OpenRead(context.Job.Source);
            extract = await _stone.HasEnvelopeAsync(probe, srcExt, cancellationToken).ConfigureAwait(false);
        }

        if (!embed && !extract)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        await using FileStream src = File.OpenRead(context.Job.Source);
        await using FileStream dst = File.Create(context.Job.Destination);
        StoneOptions options = new()
        {
            Password = context.Job.Password,
            CrossCategory = IsCrossCategory(context),
            Progress = context.Progress,
        };

        if (embed)
        {
            await _stone.EmbedAsync(src, srcExt, dst, dstExt, options, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _stone.ExtractAsync(src, srcExt, dst, dstExt, options, cancellationToken).ConfigureAwait(false);
        }

        return new RoutingDecision.Handled(Name);
    }

    private static bool IsCrossCategory(RoutingContext context)
    {
        MediaCategory? src = context.Registry.CategoryOf(context.Job.SourceExtension);
        MediaCategory? dst = context.Registry.CategoryOf(context.Job.DestinationExtension);
        return src != dst;
    }
}
