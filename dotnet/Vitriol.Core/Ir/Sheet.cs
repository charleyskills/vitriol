using Vitriol.Core.Collections;

namespace Vitriol.Core.Ir;

/// <summary>
/// A named sheet of cells. Mirrors the Python <c>Sheet</c> dataclass in
/// <c>app/core/intermediate.py:92–95</c>.
/// </summary>
public sealed record Sheet(string Name, EquatableArray<EquatableArray<Cell>> Rows)
{
    public Sheet(string name) : this(name, EquatableArray<EquatableArray<Cell>>.Empty) { }
}
