namespace Vitriol.Core.Ir;

/// <summary>
/// A single tabular cell. Mirrors the Python <c>Cell</c> dataclass in
/// <c>app/core/intermediate.py:86–89</c>. Unlike Vitriol, <see cref="Value"/>
/// stays <c>object?</c> through the pipeline rather than being cast via
/// <c>str()</c> — preserves <see cref="decimal"/> precision, <see cref="DateTime"/>
/// identity, and boolean/integer types.
/// </summary>
public sealed record Cell(object? Value = null, string? Formula = null)
{
    public static Cell Empty { get; } = new();
}
