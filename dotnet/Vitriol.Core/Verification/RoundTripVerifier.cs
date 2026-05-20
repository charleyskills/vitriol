using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Verification;

/// <summary>
/// Optional options for <see cref="RoundTripVerifier"/>. Controls whether the
/// structural-equivalence fallback runs after byte comparison fails.
/// </summary>
public sealed record RoundTripVerifierOptions
{
    /// <summary>When byte equality fails, run the structural-equivalence check too.</summary>
    public bool AlsoCheckStructural { get; init; } = true;
}

/// <summary>
/// Forward-then-reverse conversion with endpoint SHA-256 comparison. Mirrors
/// <c>_run_with_verify</c> in <c>app/core/conversion_queue.py:156-230</c>.
///
/// <para>For the Stone path and the trailer-envelope path, bytes are
/// preserved by construction and the verdict is <see cref="RoundTripResult.ByteEqual"/>.
/// For the IR path the byte hashes inevitably drift; when
/// <see cref="RoundTripVerifierOptions.AlsoCheckStructural"/> is true the
/// verifier falls back to <see cref="IStructuralEquivalence"/>, returning
/// <see cref="RoundTripResult.StructurallyEqual"/> or
/// <see cref="RoundTripResult.StructurallyDiffers"/>.</para>
/// </summary>
public sealed class RoundTripVerifier : IRoundTripVerifier
{
    private readonly IConversionRouter _router;
    private readonly IStructuralEquivalence? _structural;
    private readonly RoundTripVerifierOptions _options;
    private readonly ILogger<RoundTripVerifier> _logger;

    public RoundTripVerifier(
        IConversionRouter router,
        IStructuralEquivalence? structural = null,
        RoundTripVerifierOptions? options = null,
        ILogger<RoundTripVerifier>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
        _structural = structural;
        _options = options ?? new RoundTripVerifierOptions();
        _logger = logger ?? NullLogger<RoundTripVerifier>.Instance;
    }

    public async ValueTask<RoundTripResult> VerifyAsync(
        ConversionJob job,
        IProgress<ConversionEvent>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using TempScope scope = new("vitriol-verify-");
        string forwardPath = scope.PathFor("forward" + job.DestinationExtension);
        string reversePath = scope.PathFor("reverse" + job.SourceExtension);

        try
        {
            // Forward: src -> forwardPath.
            ConversionJob forward = job with { Destination = forwardPath };
            await _router.ConvertAsync(forward, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ConversionEvent.Progress(0.55));

            // Reverse: forwardPath -> reversePath, with extensions swapped.
            ConversionJob reverse = forward with
            {
                Source = forwardPath,
                Destination = reversePath,
                SourceExtension = forward.DestinationExtension,
                DestinationExtension = forward.SourceExtension,
            };
            await _router.ConvertAsync(reverse, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ConversionEvent.Progress(0.95));

            string sourceHash;
            string reverseHash;
            try
            {
                sourceHash = await Sha256.HashFileAsync(job.Source, cancellationToken).ConfigureAwait(false);
                reverseHash = await Sha256.HashFileAsync(reversePath, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                return new RoundTripResult.IoError(
                    $"Verification could not complete (I/O error during hash compare): {e.Message}. "
                    + "The conversion itself may still be valid — try again or run without "
                    + "Verify Round-Trip to keep the output.");
            }

            if (sourceHash == reverseHash)
            {
                //_logger.LogInformation(
                //    "Round-trip verified by byte equality. Source/reverse SHA-256 = {Hash}",
                //    sourceHash);
                progress?.Report(new ConversionEvent.Progress(1.0));
                return new RoundTripResult.ByteEqual(sourceHash);
            }

            // Bytes differ — try the structural fallback if requested and available.
            if (_options.AlsoCheckStructural && _structural is not null)
            {
                StructuralComparisonResult structuralResult = await _structural.CompareAsync(
                    job.Source,
                    reversePath,
                    job.SourceExtension,
                    cancellationToken).ConfigureAwait(false);

                switch (structuralResult)
                {
                    case StructuralComparisonResult.Equivalent equiv:
                        //_logger.LogInformation(
                        //    "Round-trip verified by structural equivalence: {Desc}", equiv.Description);
                        return new RoundTripResult.StructurallyEqual(equiv.Description);
                    case StructuralComparisonResult.Different diff:
                        return new RoundTripResult.StructurallyDiffers(diff.Description);
                    case StructuralComparisonResult.NotApplicable na:
                        return new RoundTripResult.BytesDiffer(sourceHash, reverseHash,
                            $"Bytes differ and structural comparison unavailable: {na.Reason}");
                    case StructuralComparisonResult.Error err:
                        return new RoundTripResult.BytesDiffer(sourceHash, reverseHash,
                            $"Bytes differ; structural check errored: {err.Message}");
                }
            }

            return new RoundTripResult.BytesDiffer(sourceHash, reverseHash,
                "The forward+reverse round-trip did not reproduce the original file. "
                + "The conversion is not byte-lossless for this source/host combination.");
        }
        catch (UnsupportedConversionException e)
        {
            return new RoundTripResult.IoError(
                $"Verification could not complete (conversion refused): {e.Message}");
        }
    }
}
