namespace Vitriol.Core.Pipeline;

/// <summary>
/// Thrown when the router cannot dispatch a <c>(srcExt, dstExt)</c> pair to any
/// gate. Mirrors <c>UnsupportedConversionError</c> in
/// <c>app/core/router.py:13</c>.
/// </summary>
public sealed class UnsupportedConversionException : Exception
{
    public UnsupportedConversionException(string message) : base(message) { }

    public UnsupportedConversionException(string message, Exception inner) : base(message, inner) { }

    public UnsupportedConversionException() { }
}

/// <summary>
/// Thrown when a Stone envelope is found to be truncated, malformed, or
/// otherwise non-parseable. Stone never signals "wrong password" — that
/// produces silent garbage by design (no-oracle property in
/// <c>app/format_handlers/_stone_crypto.py</c>).
/// </summary>
public sealed class StoneEnvelopeException : Exception
{
    public StoneEnvelopeException(string message) : base(message) { }

    public StoneEnvelopeException(string message, Exception inner) : base(message, inner) { }

    public StoneEnvelopeException() { }
}
