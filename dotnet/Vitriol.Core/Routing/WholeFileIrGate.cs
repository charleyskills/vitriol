using Vitriol.Core.Ir;
using Vitriol.Core.Ir.Adapters;
using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Routing;

/// <summary>
/// Whole-file IR path: <c>reader.read → adapter → writer.write</c>. The
/// terminal gate for non-media, non-Stone conversions. Mirrors
/// <c>router.py:289-311</c>.
/// </summary>
public sealed class WholeFileIrGate : IRoutingGate
{
    private readonly AdapterRegistry _adapterRegistry;

    public WholeFileIrGate(AdapterRegistry adapterRegistry)
    {
        ArgumentNullException.ThrowIfNull(adapterRegistry);
        _adapterRegistry = adapterRegistry;
    }

    public int Order => 90;

    public string Name => "WholeFileIr";

    public async ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IFormatReader? reader = context.Registry.GetReader(context.Job.SourceExtension);
        IFormatWriter? writer = context.Registry.GetWriter(context.Job.DestinationExtension);

        if (reader is null)
        {
            return new RoutingDecision.Refused(
                Name, $"No reader for {context.Job.SourceExtension}.");
        }
        if (writer is null)
        {
            return new RoutingDecision.Refused(
                Name, $"No writer for {context.Job.DestinationExtension}.");
        }

        // Same-handler plain-text pass-through (e.g. .txt → .py / .log). The
        // IR path would mangle whitespace; stream-convert preserves bytes
        // exactly. Vitriol's router.py:250-257 calls this out specifically.
        if (ReferenceEquals(reader, writer) && reader is IStreamConverter streamConv
            && streamConv.CanStream(context.Job.SourceExtension, context.Job.DestinationExtension))
        {
            await using FileStream src = File.OpenRead(context.Job.Source);
            await using FileStream dst = File.Create(context.Job.Destination);
            await streamConv.StreamConvertAsync(
                src, dst,
                context.Job.SourceExtension,
                context.Job.DestinationExtension,
                context.Progress,
                cancellationToken).ConfigureAwait(false);
            return new RoutingDecision.Handled(Name);
        }

        cancellationToken.ThrowIfCancellationRequested();
        context.Progress?.Report(new ConversionEvent.Progress(0.05));

        IDocument doc;
        ReadContext readContext = new(context.Job.SourceExtension)
        {
            Password = context.Job.Password,
            Progress = context.Progress,
        };
        await using (FileStream src = File.OpenRead(context.Job.Source))
        {
            doc = await reader.ReadAsync(src, readContext, cancellationToken).ConfigureAwait(false);
        }

        // Forward handler-stashed warnings (metadata["warnings"] in Vitriol).
        if (doc.Metadata.Extras.TryGetValue("warnings", out object warningsObj)
            && warningsObj is IEnumerable<string> handlerWarnings)
        {
            foreach (string w in handlerWarnings)
            {
                context.AddWarning(w);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        context.Progress?.Report(new ConversionEvent.Progress(0.5));

        DocKind srcKind = reader.Kind;
        DocKind dstKind = writer.Kind;
        if (srcKind != dstKind)
        {
            if (!_adapterRegistry.TryAdapt(doc, dstKind, context.Progress, out IDocument? adapted)
                || adapted is null)
            {
                return new RoutingDecision.Refused(
                    Name,
                    $"No adapter from {srcKind} to {dstKind} for "
                    + $"{context.Job.SourceExtension} → {context.Job.DestinationExtension}.");
            }
            doc = adapted;
        }

        cancellationToken.ThrowIfCancellationRequested();
        context.Progress?.Report(new ConversionEvent.Progress(0.7));

        WriteContext writeContext = new(context.Job.DestinationExtension)
        {
            Progress = context.Progress,
        };
        await using (FileStream dst = File.Create(context.Job.Destination))
        {
            await writer.WriteAsync(doc, dst, writeContext, cancellationToken).ConfigureAwait(false);
        }

        context.Progress?.Report(new ConversionEvent.Progress(1.0));
        return new RoutingDecision.Handled(Name);
    }
}
