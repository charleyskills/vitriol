namespace Vitriol.Core.Ir;

[Flags]
public enum RunStyle
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Underline = 1 << 2,
    Code = 1 << 3,
}
