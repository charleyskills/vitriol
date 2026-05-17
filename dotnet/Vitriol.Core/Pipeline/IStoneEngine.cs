namespace Vitriol.Core.Pipeline;

/// <summary>
/// Philosopher's Stone — byte-lossless envelope embed/extract. Mirrors the
/// public surface of <c>app/format_handlers/masquerade.py</c>: <c>convert</c>,
/// <c>can_embed_into</c>, <c>can_extract_from</c>, <c>has_envelope</c>,
/// <c>is_lossy</c>.
/// </summary>
public interface IStoneEngine
{
    bool CanEmbedInto(string destinationExtension);

    bool CanExtractFrom(string sourceExtension);

    bool IsLossySource(string sourceExtension);

    ValueTask<bool> HasEnvelopeAsync(Stream source, string sourceExtension, CancellationToken cancellationToken);

    ValueTask EmbedAsync(
        Stream source,
        string sourceExtension,
        Stream destination,
        string destinationExtension,
        StoneOptions options,
        CancellationToken cancellationToken);

    ValueTask ExtractAsync(
        Stream source,
        string sourceExtension,
        Stream destination,
        string destinationExtension,
        StoneOptions options,
        CancellationToken cancellationToken);
}
