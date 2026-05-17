namespace Vitriol.Core.Pipeline;

/// <summary>
/// Classifies the IR shape a format handler produces or consumes. Mirrors the
/// Python <c>DOC_KIND</c> string attribute on each handler module.
/// </summary>
public enum DocKind
{
    Text = 0,
    Tabular = 1,
    Binary = 2,
    Archive = 3,
    Pandoc = 4,
    /// <summary>
    /// X.509 certificates and asymmetric key material (PEM / DER).
    /// Self-contained — no <see cref="Vitriol.Core.Ir.Adapters.AdapterRegistry"/>
    /// entries cross this kind, mirroring Python's <c>DOC_KIND = "crypto"</c>
    /// isolation in <c>app/format_handlers/crypto_handler.py</c>.
    /// </summary>
    Crypto = 5,
}

/// <summary>
/// Classifies a media handler. Mirrors the Python <c>MEDIA_CATEGORY</c>
/// attribute (<c>image / audio / video / model</c>).
/// </summary>
public enum MediaCategory
{
    Image = 0,
    Audio = 1,
    Video = 2,
    Model = 3,
}
