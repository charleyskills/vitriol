using System.Text;
using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Ir.Adapters;

/// <summary>
/// Bridges between IR kinds. Mirrors the <c>ADAPTERS</c> dict in
/// <c>app/core/intermediate.py:213–231</c>:
/// <list type="bullet">
/// <item>tabular ↔ text</item>
/// <item>text ↔ binary (via UTF-8)</item>
/// <item>tabular ↔ binary (via plain text)</item>
/// <item>pandoc ↔ text (deferred — needs the Pandoc subprocess wrapper)</item>
/// </list>
/// </summary>
public sealed class AdapterRegistry
{
    private readonly TabularToTextDocAdapter _tabularToText = new();
    private readonly TextDocToTabularAdapter _textToTabular = new();

    public bool TryAdapt(
        IDocument source,
        DocKind targetKind,
        IProgress<ConversionEvent>? progress,
        out IDocument? result)
    {
        result = (source, targetKind) switch
        {
            (Tabular t, DocKind.Text) => _tabularToText.Adapt(t, progress),
            (TextDoc d, DocKind.Tabular) => _textToTabular.Adapt(d, progress),
            (TextDoc d, DocKind.Binary) => Utf8Encode(TextDocToPlainAdapter.Adapt(d, progress), d.Metadata),
            (BinaryDoc b, DocKind.Text) => Utf8Decode(b, progress),
            (Tabular t, DocKind.Binary) => Utf8Encode(
                TextDocToPlainAdapter.Adapt(_tabularToText.Adapt(t, progress), progress),
                t.Metadata),
            (BinaryDoc b, DocKind.Tabular) => _textToTabular.Adapt(Utf8Decode(b, progress), progress),
            _ => null,
        };

        return result is not null;
    }

    private static BinaryDoc Utf8Encode(string text, DocumentMetadata metadata)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return new BinaryDoc(bytes, "text/plain; charset=utf-8", metadata);
    }

    private static TextDoc Utf8Decode(BinaryDoc binary, IProgress<ConversionEvent>? progress)
    {
        if (!TryDecodeUtf8(binary.Bytes.Span, out string text))
        {
            progress?.Report(new ConversionEvent.Warning(
                "Binary → Text adapter: source bytes are not valid UTF-8. "
                + "Decoded with U+FFFD replacement; original bytes are lost."));
        }
        return PlainToTextDocAdapter.Adapt(text, progress) with { Metadata = binary.Metadata };
    }

    private static bool TryDecodeUtf8(ReadOnlySpan<byte> bytes, out string text)
    {
        // Strict decode first — only fall back to replacement on failure so we
        // can warn the caller. Vitriol uses errors="replace" silently
        // (intermediate.py:218, 222); the port surfaces the loss instead.
        try
        {
            Encoding strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            text = strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.UTF8.GetString(bytes);
            return false;
        }
    }
}
