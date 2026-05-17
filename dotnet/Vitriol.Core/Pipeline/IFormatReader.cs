using Vitriol.Core.Ir;

namespace Vitriol.Core.Pipeline;

/// <summary>
/// Decodes a source file into an <see cref="IDocument"/>. Mirrors the duck-typed
/// <c>read(path, ext, cancel)</c> contract of the Python handler modules in
/// <c>app/format_handlers/</c>.
/// </summary>
public interface IFormatReader
{
    DocKind Kind { get; }

    IReadOnlySet<string> SupportedExtensions { get; }

    /// <summary>
    /// True if this reader can stream the given <c>(src, dst)</c> pair without
    /// materializing the full document in memory. Mirrors the
    /// <c>can_stream(src_ext, dst_ext)</c> hook in the Python handlers.
    /// </summary>
    bool CanStream(string sourceExtension, string destinationExtension) => false;

    ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken);
}
