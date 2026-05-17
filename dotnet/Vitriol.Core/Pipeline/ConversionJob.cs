namespace Vitriol.Core.Pipeline;

/// <summary>
/// One unit of work for the router. Mirrors the Python <c>Job</c> dataclass at
/// <c>app/core/conversion_queue.py:42–55</c>. Immutable — use <c>with</c>
/// expressions to derive variants.
/// </summary>
public sealed record ConversionJob(
    string Source,
    string Destination,
    string SourceExtension,
    string DestinationExtension)
{
    public bool SaveOverOriginal { get; init; }
    public bool Masquerade { get; init; }
    public bool VerifyRoundTrip { get; init; }
    public bool Compiler { get; init; }
    public ReadOnlyMemory<byte> Password { get; init; }
    public bool PreserveAnimations { get; init; }
    public long TotalBytes { get; init; }
}
