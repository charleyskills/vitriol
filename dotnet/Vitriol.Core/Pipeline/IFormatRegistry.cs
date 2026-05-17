namespace Vitriol.Core.Pipeline;

/// <summary>
/// Read-only handler lookup. Mirrors the module-level dicts in
/// <c>app/format_handlers/__init__.py</c>: <c>READERS</c>, <c>WRITERS</c>,
/// <c>MEDIA_HANDLERS</c>, <c>MEDIA_CATEGORY_OF</c>, <c>STONE_ONLY_SOURCES</c>,
/// <c>AUTO_EXECUTE_EXTS</c>.
/// </summary>
public interface IFormatRegistry
{
    IFormatReader? GetReader(string extension);

    IFormatWriter? GetWriter(string extension);

    IMediaHandler? GetMediaHandler(string extension);

    MediaCategory? CategoryOf(string extension);

    bool IsStoneOnlySource(string extension);

    bool IsAutoExecute(string extension);

    bool IsLossySource(string extension);
}
