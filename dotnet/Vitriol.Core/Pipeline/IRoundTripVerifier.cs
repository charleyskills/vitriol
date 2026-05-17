namespace Vitriol.Core.Pipeline;

/// <summary>
/// Forward-then-reverse conversion with SHA-256 endpoint comparison. Mirrors
/// <c>_run_with_verify</c> in <c>app/core/conversion_queue.py:156–230</c>.
/// </summary>
public interface IRoundTripVerifier
{
    ValueTask<RoundTripResult> VerifyAsync(
        ConversionJob job,
        IProgress<ConversionEvent>? progress,
        CancellationToken cancellationToken);
}
