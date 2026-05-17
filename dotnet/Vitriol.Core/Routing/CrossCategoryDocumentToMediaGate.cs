using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Routing;

/// <summary>
/// When the source is a document and the destination is a media format with
/// no semantic adapter, auto-engage Stone so the conversion is round-trippable.
/// Adds a warning explaining the user must convert back through Vitriol to
/// recover the original. Mirrors <c>router.py:224-241</c>.
/// </summary>
public sealed class CrossCategoryDocumentToMediaGate : IRoutingGate
{
    private readonly IStoneEngine _stone;

    public CrossCategoryDocumentToMediaGate(IStoneEngine stone)
    {
        ArgumentNullException.ThrowIfNull(stone);
        _stone = stone;
    }

    public int Order => 60;

    public string Name => "CrossCategoryDocumentToMedia";

    public async ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IMediaHandler? srcHandler = context.Registry.GetMediaHandler(context.Job.SourceExtension);
        IMediaHandler? dstHandler = context.Registry.GetMediaHandler(context.Job.DestinationExtension);

        if (srcHandler is not null || dstHandler is null)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        if (!_stone.CanEmbedInto(context.Job.DestinationExtension) ||
            _stone.IsLossySource(context.Job.SourceExtension))
        {
            return new RoutingDecision.Refused(
                Name,
                $"Cannot convert {context.Job.SourceExtension} → {context.Job.DestinationExtension}: "
                + $"no semantic path and {context.Job.DestinationExtension} is not a "
                + "Philosopher's Stone host.");
        }

        context.AddWarning(
            $"{context.Job.SourceExtension} → {context.Job.DestinationExtension} has no "
            + "semantic conversion path; embedded the source via Philosopher's Stone. "
            + "Convert the output back through Vitriol to recover the original.");

        await using FileStream src = File.OpenRead(context.Job.Source);
        await using FileStream dst = File.Create(context.Job.Destination);
        StoneOptions options = new()
        {
            Password = context.Job.Password,
            CrossCategory = true,
            Progress = context.Progress,
        };
        await _stone.EmbedAsync(
            src,
            context.Job.SourceExtension,
            dst,
            context.Job.DestinationExtension,
            options,
            cancellationToken).ConfigureAwait(false);

        return new RoutingDecision.Handled(Name);
    }
}
