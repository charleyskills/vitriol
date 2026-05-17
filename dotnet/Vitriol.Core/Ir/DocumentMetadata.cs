using Vitriol.Core.Collections;

namespace Vitriol.Core.Ir;

/// <summary>
/// Document-level metadata: a small typed surface (Title/Author/Language) plus
/// an untyped <see cref="Extras"/> bag for handler-specific metadata and an
/// optional <see cref="Origin"/> sidecar carrying the original source bytes for
/// byte-perfect round-trip. Mirrors the Python <c>metadata: dict</c> on
/// <c>TextDoc</c> (<c>app/core/intermediate.py:80</c>) but is typed at the
/// surface — handler-stashed keys go in <c>Extras</c> instead of being
/// stringly-typed top-level entries.
/// </summary>
public sealed record DocumentMetadata(
    string? Title = null,
    string? Author = null,
    string? Language = null,
    EquatableDictionary<string, object> Extras = default,
    VitriolOrigin? Origin = null)
{
    public static DocumentMetadata Empty { get; } = new();

    public DocumentMetadata WithOrigin(VitriolOrigin origin) => this with { Origin = origin };

    public DocumentMetadata WithExtras(EquatableDictionary<string, object> extras) =>
        this with { Extras = extras };
}
