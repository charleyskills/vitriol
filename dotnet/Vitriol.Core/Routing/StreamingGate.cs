using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Routing;

/// <summary>
/// When the source is bigger than the per-format streaming threshold and the
/// reader/writer pair supports streaming, dispatch through
/// <see cref="IStreamConverter.StreamConvertAsync"/>. If the handlers don't
/// stream but the destination is a Stone host, fall back to byte-masquerade
/// with a warning ("byte-perfect but only round-trips through Vitriol").
/// Mirrors <c>router.py:259-286</c>.
/// </summary>
public sealed class StreamingGate : IRoutingGate
{
    private readonly IEnumerable<IStoneEngine> _stoneEngines;

    public StreamingGate(IEnumerable<IStoneEngine> stoneEngines)
    {
        ArgumentNullException.ThrowIfNull(stoneEngines);
        _stoneEngines = stoneEngines;
    }

    public int Order => 70;

    public string Name => "Streaming";

    public async ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        long srcSize;
        try
        {
            srcSize = new FileInfo(context.Job.Source).Length;
        }
        catch (IOException)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        long threshold = Math.Min(
            StreamingThresholds.For(context.Job.SourceExtension),
            StreamingThresholds.For(context.Job.DestinationExtension));

        if (srcSize <= threshold)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        IFormatReader? reader = context.Registry.GetReader(context.Job.SourceExtension);
        IFormatWriter? writer = context.Registry.GetWriter(context.Job.DestinationExtension);

        if (reader is IStreamConverter streamConverter
            && writer is not null
            && ReferenceEquals(reader, writer)
            && streamConverter.CanStream(context.Job.SourceExtension, context.Job.DestinationExtension))
        {
            await using FileStream src = File.OpenRead(context.Job.Source);
            await using FileStream dst = File.Create(context.Job.Destination);
            await streamConverter.StreamConvertAsync(
                src, dst,
                context.Job.SourceExtension,
                context.Job.DestinationExtension,
                context.Progress,
                cancellationToken).ConfigureAwait(false);
            return new RoutingDecision.Handled(Name);
        }

        // Fallback to byte-masquerade when a Stone engine is registered and
        // the destination is a Stone host.
        IStoneEngine? stone = _stoneEngines.FirstOrDefault();
        if (stone is not null
            && stone.CanEmbedInto(context.Job.DestinationExtension)
            && !stone.IsLossySource(context.Job.SourceExtension))
        {
            context.AddWarning(
                "File too large for direct conversion and handler cannot stream — "
                + "used byte-masquerade as fallback. Output is byte-perfect but only "
                + "round-trips through Vitriol.");

            await using FileStream src = File.OpenRead(context.Job.Source);
            await using FileStream dst = File.Create(context.Job.Destination);
            StoneOptions options = new()
            {
                Password = context.Job.Password,
                CrossCategory = context.Registry.CategoryOf(context.Job.SourceExtension)
                    != context.Registry.CategoryOf(context.Job.DestinationExtension),
                Progress = context.Progress,
            };
            await stone.EmbedAsync(
                src,
                context.Job.SourceExtension,
                dst,
                context.Job.DestinationExtension,
                options,
                cancellationToken).ConfigureAwait(false);
            return new RoutingDecision.Handled(Name);
        }

        // No streaming path and no Stone fallback — fall through to the
        // whole-file IR gate, which will try its luck.
        return RoutingDecision.NotApplicable.Instance;
    }
}
