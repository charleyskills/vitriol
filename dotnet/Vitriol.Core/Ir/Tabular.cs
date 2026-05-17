using Vitriol.Core.Collections;

namespace Vitriol.Core.Ir;

/// <summary>
/// Tabular IR: a list of named sheets. Mirrors <c>Tabular</c> in
/// <c>app/core/intermediate.py:98–100</c>.
/// </summary>
public sealed record Tabular(EquatableArray<Sheet> Sheets, DocumentMetadata Metadata) : IDocument
{
    public static Tabular Empty { get; } = new(EquatableArray<Sheet>.Empty, DocumentMetadata.Empty);

    public Tabular(EquatableArray<Sheet> sheets) : this(sheets, DocumentMetadata.Empty) { }
}
