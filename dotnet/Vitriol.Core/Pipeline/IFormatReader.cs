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

    ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken);
}
