namespace Vitriol.Core.Pipeline;

/// <summary>
/// Streaming pass-through for a specific <c>(srcExt, dstExt)</c> pair. Mirrors
/// the <c>can_stream</c> / <c>stream_convert</c> hooks on the Python handler
/// modules. A handler that implements this interface signals that, for the
/// given pair, it can emit the destination without materializing the full
/// document in memory. The streaming gate (<c>router.py:259-286</c>) routes
/// large files through this path.
/// </summary>
public interface IStreamConverter
{
    bool CanStream(string sourceExtension, string destinationExtension);

    ValueTask StreamConvertAsync(
        Stream source,
        Stream destination,
        string sourceExtension,
        string destinationExtension,
        IProgress<ConversionEvent>? progress,
        CancellationToken cancellationToken);
}
