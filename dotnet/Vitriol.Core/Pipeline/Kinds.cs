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
