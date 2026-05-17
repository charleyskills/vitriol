using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Routing;

/// <summary>
/// When both extensions belong to the same media handler (image↔image,
/// audio↔audio, video↔video, model↔model), or to the audio/video twin
/// handler (video→audio extraction), dispatch directly to the handler's
/// <see cref="IMediaHandler.ConvertAsync"/>. Mirrors <c>router.py:185-201</c>.
/// </summary>
public sealed class SameMediaHandlerGate : IRoutingGate
{
    public int Order => 40;

    public string Name => "SameMediaHandler";

    public async ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IMediaHandler? srcHandler = context.Registry.GetMediaHandler(context.Job.SourceExtension);
        IMediaHandler? dstHandler = context.Registry.GetMediaHandler(context.Job.DestinationExtension);

        if (srcHandler is null || dstHandler is null)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        // Same handler instance — direct dispatch.
        bool sameHandler = ReferenceEquals(srcHandler, dstHandler);

        // Audio/video extraction lives in the same handler module in Vitriol
        // (the same FFmpeg subprocess wrapper). Allow the cross-category
        // call when both categories are audio/video.
        bool audioVideoExtraction = srcHandler.Category is MediaCategory.Audio or MediaCategory.Video
            && dstHandler.Category is MediaCategory.Audio or MediaCategory.Video
            && sameHandler;

        if (!sameHandler && !audioVideoExtraction)
        {
            return RoutingDecision.NotApplicable.Instance;
        }

        MediaContext mediaContext = new()
        {
            PreserveAnimations = context.Job.PreserveAnimations,
            Progress = context.Progress,
        };

        await using FileStream src = File.OpenRead(context.Job.Source);
        await using FileStream dst = File.Create(context.Job.Destination);
        await srcHandler.ConvertAsync(
            context.Job.SourceExtension,
            src,
            context.Job.DestinationExtension,
            dst,
            mediaContext,
            cancellationToken).ConfigureAwait(false);

        return new RoutingDecision.Handled(Name);
    }
}
