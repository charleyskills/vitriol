using Vitriol.Core.Ir;
using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Routing;

/// <summary>
/// When the source is an image and the destination is a document format
/// (PDF/DOCX/MD/HTML/etc.), wrap the image bytes as a single-block
/// <see cref="TextDoc"/> via <see cref="ImageBytesToTextDoc.Wrap"/> and hand
/// off to the destination writer. Reversibility-aware writers honor the
/// stashed <c>_vitriol_origin</c>. Mirrors <c>router.py:203-222</c>.
/// </summary>
public sealed class CrossCategoryImageToDocumentGate : IRoutingGate
{
    public int Order => 50;

    public string Name => "CrossCategoryImageToDocument";

    public async ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IMediaHandler? srcHandler = context.Registry.GetMediaHandler(context.Job.SourceExtension);
        IMediaHandler? dstHandler = context.Registry.GetMediaHandler(context.Job.DestinationExtension);

        if (srcHandler is null || dstHandler is not null)
        {
            // Either the source isn't media, or the destination IS media —
            // not our case.
            return RoutingDecision.NotApplicable.Instance;
        }

        if (srcHandler.Category != MediaCategory.Image)
        {
            // Other cross-category routes (audio/video/model → doc) don't have
            // a single-block wrapping path in Vitriol; let later gates refuse.
            return RoutingDecision.NotApplicable.Instance;
        }

        IFormatWriter? writer = context.Registry.GetWriter(context.Job.DestinationExtension);
        if (writer is null)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        context.Progress?.Report(new ConversionEvent.Progress(0.05));

        byte[] sourceBytes;
        await using (FileStream fs = File.OpenRead(context.Job.Source))
        {
            sourceBytes = new byte[fs.Length];
            int total = 0;
            while (total < sourceBytes.Length)
            {
                int read = await fs.ReadAsync(sourceBytes.AsMemory(total), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                total += read;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        context.Progress?.Report(new ConversionEvent.Progress(0.5));

        TextDoc doc = ImageBytesToTextDoc.Wrap(
            sourceBytes,
            context.Job.SourceExtension,
            alt: Path.GetFileNameWithoutExtension(context.Job.Source));

        WriteContext writeContext = new(context.Job.DestinationExtension)
        {
            Progress = context.Progress,
            DestinationHint = context.Job.Destination,
        };

        await using FileStream dst = File.Create(context.Job.Destination);
        await writer.WriteAsync(doc, dst, writeContext, cancellationToken).ConfigureAwait(false);

        context.Progress?.Report(new ConversionEvent.Progress(1.0));
        return new RoutingDecision.Handled(Name);
    }
}
