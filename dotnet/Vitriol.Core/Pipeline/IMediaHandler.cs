namespace Vitriol.Core.Pipeline;

/// <summary>
/// Media transcoder (image / audio / video / model). Mirrors the duck-typed
/// <c>convert(src, dst, src_ext, dst_ext, cancel, progress, ...)</c> contract
/// of <c>image_handler.py</c>, <c>audio_video.py</c>, <c>model_handler.py</c>.
/// </summary>
public interface IMediaHandler
{
    MediaCategory Category { get; }

    IReadOnlySet<string> SupportedExtensions { get; }

    ValueTask ConvertAsync(
        string sourceExtension,
        Stream source,
        string destinationExtension,
        Stream destination,
        MediaContext context,
        CancellationToken cancellationToken);
}
