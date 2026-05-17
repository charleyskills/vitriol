namespace Vitriol.Core.Ir;

/// <summary>
/// A styled span of inline text. Mirrors the Python <c>Run</c> dataclass in
/// <c>app/core/intermediate.py</c> (lines 16–24).
/// </summary>
public sealed record Run(string Text, RunStyle Style = RunStyle.None, string? Href = null)
{
    public bool Bold => (Style & RunStyle.Bold) != 0;
    public bool Italic => (Style & RunStyle.Italic) != 0;
    public bool Underline => (Style & RunStyle.Underline) != 0;
    public bool Code => (Style & RunStyle.Code) != 0;
}
