using System.Text;
using Vitriol.Core.Ir;
using Vitriol.Core.Ir.Adapters;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Text;

/// <summary>
/// Plain-text reader, writer, and same-extension stream pass-through for
/// .txt, .log, .py, .xml, .html. Mirrors <c>app/format_handlers/text_plain.py</c>.
///
/// <para>One class implements three interfaces (<see cref="IFormatReader"/>,
/// <see cref="IFormatWriter"/>, <see cref="IStreamConverter"/>) so the router's
/// <see cref="Vitriol.Core.Routing.WholeFileIrGate"/> same-handler pass-through
/// check (<c>ReferenceEquals(reader, writer)</c>) succeeds. That preserves
/// whitespace exactly for .txt → .log style copies, mirroring the Python
/// "reader is writer" pattern.</para>
///
/// <para>Encoding behavior: BOM detection first; strict UTF-8 second; on
/// non-UTF-8 input falls back to replacement decoding AND emits a Warning
/// event. This is the Section 11 fix for Vitriol's silent
/// <c>errors="replace"</c> at <c>intermediate.py:218</c>.</para>
/// </summary>
public sealed class PlainTextHandler : IFormatReader, IFormatWriter, IStreamConverter
{
    public DocKind Kind => DocKind.Text;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".log", ".py", ".xml", ".html",
        };

    // -- IFormatReader -------------------------------------------------------

    public async ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await input.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        EncodingDetector.DetectionResult decoded =
            EncodingDetector.Decode(buffer.ToArray(), context.Progress);
        TextDoc doc = PlainToTextDocAdapter.Adapt(decoded.Text, context.Progress);
        return doc;
    }

    // -- IFormatWriter -------------------------------------------------------

    public async ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        TextDoc doc = document switch
        {
            TextDoc td => td,
            BinaryDoc bd => DecodeBinaryDoc(bd, context.Progress),
            _ => throw new InvalidOperationException(
                $"PlainTextHandler.Write expects a TextDoc, got {document.GetType().Name}."),
        };

        string flat = TextDocToPlainAdapter.Adapt(doc, context.Progress);
        byte[] bytes = Encoding.UTF8.GetBytes(flat);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static TextDoc DecodeBinaryDoc(BinaryDoc bd, IProgress<ConversionEvent>? progress)
    {
        EncodingDetector.DetectionResult decoded =
            EncodingDetector.Decode(bd.Bytes, progress);
        return PlainToTextDocAdapter.Adapt(decoded.Text, progress) with { Metadata = bd.Metadata };
    }

    // -- IStreamConverter ----------------------------------------------------

    public bool CanStream(string sourceExtension, string destinationExtension)
    {
        return SupportedExtensions.Contains(sourceExtension)
            && SupportedExtensions.Contains(destinationExtension);
    }

    public async ValueTask StreamConvertAsync(
        Stream source,
        Stream destination,
        string sourceExtension,
        string destinationExtension,
        IProgress<ConversionEvent>? progress,
        CancellationToken cancellationToken)
    {
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        progress?.Report(new ConversionEvent.Progress(1.0));
    }
}
