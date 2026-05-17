using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Routing;

/// <summary>
/// Probes a PDF/DOCX/EPUB source for a Vitriol-written trailer envelope. When
/// found and the recovered extension equals the destination extension, writes
/// the payload bytes directly — byte-perfect, no re-encoding. Mirrors the
/// trailer-envelope short-circuit at <c>router.py:103-140</c>.
///
/// Depends on <see cref="ITrailerEnvelopeReader"/>s registered via DI. In
/// Sprint 2 no readers exist, so this gate always returns
/// <see cref="RoutingDecision.NotApplicable"/>. Sprint 6+ adds the PDF/DOCX/EPUB
/// readers.
/// </summary>
public sealed class TrailerEnvelopeGate : IRoutingGate
{
    private readonly IReadOnlyList<ITrailerEnvelopeReader> _readers;

    public TrailerEnvelopeGate(IEnumerable<ITrailerEnvelopeReader> readers)
    {
        ArgumentNullException.ThrowIfNull(readers);
        _readers = readers.ToArray();
    }

    public int Order => 10;

    public string Name => "TrailerEnvelope";

    public async ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_readers.Count == 0)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        string srcExt = context.Job.SourceExtension;
        string dstExt = context.Job.DestinationExtension;

        // Trailer recovery is only useful when the destination is a media
        // format (so we'd otherwise re-encode through the IR path).
        if (context.Registry.CategoryOf(dstExt) is null)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        ITrailerEnvelopeReader? reader = _readers.FirstOrDefault(r => r.CanRead(srcExt));
        if (reader is null)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        TrailerEnvelopeResult? recovered;
        await using (FileStream fs = File.OpenRead(context.Job.Source))
        {
            recovered = await reader.TryReadAsync(fs, srcExt, cancellationToken).ConfigureAwait(false);
        }

        if (recovered is null)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        // Direct write — recovered extension matches destination.
        if (string.Equals(recovered.RecoveredExtension, dstExt, StringComparison.OrdinalIgnoreCase))
        {
            await using FileStream dst = File.Create(context.Job.Destination);
            await dst.WriteAsync(recovered.Payload, cancellationToken).ConfigureAwait(false);
            context.Progress?.Report(new ConversionEvent.Progress(1.0));
            return new RoutingDecision.Handled(Name);
        }

        // Cross-media recovery via the media handler — only when both extensions
        // belong to the same handler. Defer the actual transcode to the
        // SameMediaHandlerGate via a recursive router call (Sprint 6+ wiring);
        // for now refuse with a clear message rather than silently dropping.
        return new RoutingDecision.Refused(
            Name,
            $"Cannot convert {srcExt} → {dstExt}: this {srcExt} carries a recoverable "
            + $"{recovered.RecoveredExtension} payload, but cross-media recovery is not "
            + "wired up yet. Convert to "
            + $"{recovered.RecoveredExtension} first, then to {dstExt}.");
    }
}
