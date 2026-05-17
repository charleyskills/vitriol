using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Registry;

/// <summary>
/// Default <see cref="IFormatRegistry"/>. Builds extension-keyed lookup tables
/// from the DI-registered handler collections. Mirrors how
/// <c>app/format_handlers/__init__.py::load_all</c> assembles its module-level
/// dicts at startup.
/// </summary>
public sealed class FormatRegistry : IFormatRegistry
{
    private readonly Dictionary<string, IFormatReader> _readers;
    private readonly Dictionary<string, IFormatWriter> _writers;
    private readonly Dictionary<string, IMediaHandler> _mediaHandlers;
    private readonly Dictionary<string, MediaCategory> _mediaCategoryOf;

    public FormatRegistry(
        IEnumerable<IFormatReader> readers,
        IEnumerable<IFormatWriter> writers,
        IEnumerable<IMediaHandler> mediaHandlers)
    {
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(mediaHandlers);

        _readers = new(StringComparer.OrdinalIgnoreCase);
        _writers = new(StringComparer.OrdinalIgnoreCase);
        _mediaHandlers = new(StringComparer.OrdinalIgnoreCase);
        _mediaCategoryOf = new(StringComparer.OrdinalIgnoreCase);

        foreach (IFormatReader reader in readers)
        {
            foreach (string ext in reader.SupportedExtensions)
            {
                _readers.TryAdd(ext, reader);
            }
        }
        foreach (IFormatWriter writer in writers)
        {
            foreach (string ext in writer.SupportedExtensions)
            {
                _writers.TryAdd(ext, writer);
            }
        }
        foreach (IMediaHandler handler in mediaHandlers)
        {
            foreach (string ext in handler.SupportedExtensions)
            {
                _mediaHandlers.TryAdd(ext, handler);
                _mediaCategoryOf[ext] = handler.Category;
            }
        }

        // Fold the hardcoded category table in too, so the router can answer
        // category questions even for media types that don't have a registered
        // handler yet (Sprint 6+).
        foreach (KeyValuePair<string, MediaCategory> kv in KnownExtensions.MediaCategoryOf)
        {
            _mediaCategoryOf.TryAdd(kv.Key, kv.Value);
        }
    }

    public IFormatReader? GetReader(string extension) =>
        _readers.GetValueOrDefault(extension);

    public IFormatWriter? GetWriter(string extension) =>
        _writers.GetValueOrDefault(extension);

    public IMediaHandler? GetMediaHandler(string extension) =>
        _mediaHandlers.GetValueOrDefault(extension);

    public MediaCategory? CategoryOf(string extension) =>
        _mediaCategoryOf.TryGetValue(extension, out MediaCategory cat) ? cat : null;

    public bool IsStoneOnlySource(string extension) =>
        KnownExtensions.IsStoneOnlySource(extension);

    public bool IsAutoExecute(string extension) =>
        KnownExtensions.IsAutoExecute(extension);

    public bool IsLossySource(string extension) =>
        KnownExtensions.IsLossySource(extension);
}
