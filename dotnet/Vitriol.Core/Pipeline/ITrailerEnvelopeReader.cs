namespace Vitriol.Core.Pipeline;

/// <summary>
/// Probes a source container (PDF, DOCX, EPUB) for a Vitriol-written trailer
/// envelope. Mirrors <c>_try_trailer_envelope</c> in <c>app/core/router.py:25-40</c>:
/// PDF carries the envelope in a trailer extension dictionary; DOCX and EPUB
/// carry it as a ZIP entry at <c>_vitriol/original.bin</c>. When recovery
/// succeeds the router short-circuits the IR path and writes payload bytes
/// directly (<c>router.py:103-140</c>).
/// </summary>
public interface ITrailerEnvelopeReader
{
    bool CanRead(string sourceExtension);

    ValueTask<TrailerEnvelopeResult?> TryReadAsync(
        Stream source,
        string sourceExtension,
        CancellationToken cancellationToken);
}

/// <summary>Recovered payload plus the original extension hint baked into the envelope.</summary>
public sealed record TrailerEnvelopeResult(ReadOnlyMemory<byte> Payload, string RecoveredExtension);
