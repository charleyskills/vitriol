using Vitriol.Core.Pipeline;

namespace Vitriol.Stone;

/// <summary>
/// One carrier format for the Philosopher's Stone. Embeds a Stone envelope
/// into a file that is also a valid instance of the carrier format (a real
/// PNG, a real ZIP archive, a real text file, etc.). Mirrors one per-host
/// section of <c>app/format_handlers/masquerade.py</c>.
/// </summary>
public interface IStoneHost
{
    /// <summary>Canonical destination extensions this host handles. Always lowercase, with leading dot.</summary>
    IReadOnlySet<string> SupportedExtensions { get; }

    bool CanEmbed { get; }

    bool CanExtract { get; }

    /// <summary>Quick probe: does <paramref name="source"/> already carry an envelope?</summary>
    ValueTask<bool> HasEnvelopeAsync(Stream source, string extension, CancellationToken cancellationToken);

    /// <summary>
    /// Embed <paramref name="sourceBytes"/> (whose original extension is
    /// <paramref name="sourceExtension"/>) into a new carrier of this host's
    /// format, writing the carrier bytes to <paramref name="destination"/>.
    /// </summary>
    ValueTask EmbedAsync(
        ReadOnlyMemory<byte> sourceBytes,
        string sourceExtension,
        Stream destination,
        StoneOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extract the original bytes + extension from a Stone carrier.
    /// </summary>
    ValueTask<StoneExtractionResult> ExtractAsync(
        Stream source,
        string sourceExtension,
        StoneOptions options,
        CancellationToken cancellationToken);
}

public sealed record StoneExtractionResult(ReadOnlyMemory<byte> Payload, string RecoveredExtension);
