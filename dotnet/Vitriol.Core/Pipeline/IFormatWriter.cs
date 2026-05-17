using Vitriol.Core.Ir;

namespace Vitriol.Core.Pipeline;

/// <summary>
/// Encodes an <see cref="IDocument"/> to the destination stream. Mirrors the
/// duck-typed <c>write(doc, path, ext, cancel)</c> contract of the Python
/// handler modules in <c>app/format_handlers/</c>.
/// </summary>
public interface IFormatWriter
{
    DocKind Kind { get; }

    IReadOnlySet<string> SupportedExtensions { get; }

    ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken);
}
