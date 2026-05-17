namespace Vitriol.Core.Pipeline;

/// <summary>
/// The single conversion dispatcher. Mirrors <c>convert_file</c> in
/// <c>app/core/router.py:43–312</c>. Implementations walk an ordered list of
/// <see cref="IRoutingGate"/> and invoke the first one that accepts the job.
/// </summary>
public interface IConversionRouter
{
    ValueTask ConvertAsync(
        ConversionJob job,
        IProgress<ConversionEvent>? progress,
        CancellationToken cancellationToken);
}
