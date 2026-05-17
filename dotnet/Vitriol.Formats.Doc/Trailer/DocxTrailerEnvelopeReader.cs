using System.IO.Compression;
using Vitriol.Core.Pipeline;
using Vitriol.Stone.Envelope;

namespace Vitriol.Formats.Doc.Trailer;

/// <summary>
/// Reads the <c>_vitriol/original.bin</c> ZIP entry from a Vitriol-written
/// DOCX. Mirrors the Python check in <c>app/core/router.py:33-39</c>:
///
/// <code>
/// with zipfile.ZipFile(src) as z:
///     if "_vitriol/original.bin" in z.namelist():
///         return _parse_envelope(z.read("_vitriol/original.bin"))
/// </code>
/// </summary>
public sealed class DocxTrailerEnvelopeReader : ITrailerEnvelopeReader
{
    public const string EntryName = "_vitriol/original.bin";

    public bool CanRead(string sourceExtension) =>
        string.Equals(sourceExtension, ".docx", StringComparison.OrdinalIgnoreCase);

    public async ValueTask<TrailerEnvelopeResult?> TryReadAsync(
        Stream source,
        string sourceExtension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanSeek)
        {
            return null;
        }
        source.Position = 0;

        try
        {
            using ZipArchive zip = new(source, ZipArchiveMode.Read, leaveOpen: true);
            ZipArchiveEntry? entry = zip.GetEntry(EntryName);
            if (entry is null)
            {
                return null;
            }

            await using Stream es = entry.Open();
            using MemoryStream buf = new();
            await es.CopyToAsync(buf, cancellationToken).ConfigureAwait(false);

            if (!UcmsEnvelope.TryParse(buf.GetBuffer().AsSpan(0, (int)buf.Length),
                out UcmsEnvelope? envelope, out _))
            {
                return null;
            }

            return new TrailerEnvelopeResult(envelope!.Payload, envelope.Extension);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
