namespace Vitriol.Core.Pipeline;

/// <summary>
/// Outcome of <see cref="IRoundTripVerifier.VerifyAsync"/>. Mirrors the three
/// branches of <c>_run_with_verify</c> in
/// <c>app/core/conversion_queue.py:156–230</c>: success (hashes match), bytes
/// differ (hashes don't match), and I/O error during the hash compare.
/// </summary>
public abstract record RoundTripResult
{
    protected RoundTripResult() { }

    public sealed record ByteEqual(string Sha256) : RoundTripResult;

    public sealed record StructurallyEqual(string Description) : RoundTripResult;

    public sealed record BytesDiffer(string SourceSha256, string ReverseSha256, string Description)
        : RoundTripResult;

    public sealed record StructurallyDiffers(string Description) : RoundTripResult;

    public sealed record IoError(string Message) : RoundTripResult;
}
