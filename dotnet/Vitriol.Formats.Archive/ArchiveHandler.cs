using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using Vitriol.Core.Ir;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Archive;

/// <summary>
/// Archive reader+writer for ZIP / 7Z / TAR family via SharpCompress.
/// Mirrors <c>app/format_handlers/archive_handler.py</c>. Same-kind
/// conversions are byte-perfect (copy the source bytes verbatim); cross-kind
/// conversions extract each entry from the source archive and stream it
/// into a fresh writer for the destination kind.
///
/// <para><b>Write coverage</b>: ZIP / .cbz / TAR / TAR.GZ / TAR.BZ2 /
/// TAR.XZ / TAR.ZST. 7Z / .cb7 are <b>read-only</b> because no MIT-licensed
/// pure-managed 7z writer exists in .NET today (Python's <c>py7zr</c> has no
/// direct counterpart). RAR / .cbr are also read-only (closed format).</para>
///
/// <para>The router resolves a single singleton instance as both
/// <see cref="IFormatReader"/> and <see cref="IFormatWriter"/>, so the
/// <c>ReferenceEquals</c> check in <c>WholeFileIrGate</c> sees one handler
/// and the IR pass-through is short.</para>
/// </summary>
public sealed class ArchiveHandler : IFormatReader, IFormatWriter
{
    public DocKind Kind => DocKind.Archive;

    /// <summary>
    /// All extensions the handler claims. The router calls
    /// <see cref="IFormatRegistry.GetReader"/> and <see cref="IFormatRegistry.GetWriter"/>
    /// per extension; we surface both read-only and write-capable extensions
    /// here and refuse RAR / 7Z at <see cref="WriteAsync"/> time with a
    /// clear message.
    /// </summary>
    public IReadOnlySet<string> SupportedExtensions { get; } =
        ArchiveKindMap.ReadableExtensions;

    public async ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        ArchiveKind kind = ArchiveKindMap.FromExtension(context.Extension);

        using MemoryStream buffer = new();
        await input.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return new ArchiveDoc(buffer.ToArray(), kind);
    }

    public async ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        ArchiveDoc archive = document switch
        {
            ArchiveDoc a => a,
            _ => throw new InvalidOperationException(
                $"ArchiveHandler.Write expects an ArchiveDoc, got {document.GetType().Name}."),
        };

        ArchiveKind dstKind = ArchiveKindMap.FromExtension(context.Extension);
        if (!ArchiveKindMap.WritableExtensions.Contains(context.Extension))
        {
            throw new UnsupportedConversionException(dstKind switch
            {
                ArchiveKind.Rar =>
                    "Writing RAR is not supported (the RAR format is closed-source).",
                ArchiveKind.SevenZip =>
                    "Writing 7Z is not supported in the .NET port yet "
                    + "(SharpCompress is 7z-read-only; deferred to a future sprint).",
                ArchiveKind.TarZst =>
                    "Writing TAR.ZST is not supported in this sprint (SharpCompress's "
                    + "tar+zstd writer pipeline needs verification; reading is fine).",
                _ => $"Writing {dstKind} is not supported.",
            });
        }

        // Same-kind byte passthrough preserves every byte (and therefore every
        // bit of compression metadata that wouldn't survive a re-pack). Matches
        // Python's shutil.copyfile fast path at archive_handler.py:89-91.
        if (dstKind == archive.SourceKind)
        {
            await output.WriteAsync(archive.SourceBytes, cancellationToken).ConfigureAwait(false);
            context.Progress?.Report(new ConversionEvent.Progress(1.0));
            return;
        }

        await Repack(archive, output, dstKind, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task Repack(
        ArchiveDoc source,
        Stream output,
        ArchiveKind dstKind,
        WriteContext context,
        CancellationToken cancellationToken)
    {
        // Buffer the source to a MemoryStream so SharpCompress can re-read
        // (some readers seek; some don't, but ReaderFactory wraps either way).
        using MemoryStream srcStream = new(source.SourceBytes.ToArray(), writable: false);

        WriterOptions writerOptions = BuildWriterOptions(dstKind);
        ArchiveType writerType = ToWriterArchiveType(dstKind);

        using IWriter writer = WriterFactory.Open(output, writerType, writerOptions);
        using IReader reader = ReaderFactory.Open(srcStream);

        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.Entry.IsDirectory)
            {
                continue;
            }

            string key = reader.Entry.Key ?? string.Empty;
            ValidateEntryName(key);

            // Buffer the entry into memory so we can pass a seekable stream
            // to IWriter.Write (some writers need to know the length).
            using MemoryStream entryBuf = new();
            using (Stream entryStream = reader.OpenEntryStream())
            {
                await entryStream.CopyToAsync(entryBuf, cancellationToken).ConfigureAwait(false);
            }
            entryBuf.Position = 0;

            writer.Write(key, entryBuf, reader.Entry.LastModifiedTime);
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.Progress?.Report(new ConversionEvent.Progress(1.0));
    }

    private static ArchiveType ToWriterArchiveType(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Zip => ArchiveType.Zip,
        ArchiveKind.Tar => ArchiveType.Tar,
        ArchiveKind.TarGz => ArchiveType.Tar,
        ArchiveKind.TarBz2 => ArchiveType.Tar,
        ArchiveKind.TarXz => ArchiveType.Tar,
        _ => throw new InvalidOperationException($"No writer mapping for {kind}"),
    };

    private static WriterOptions BuildWriterOptions(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Zip => new WriterOptions(CompressionType.Deflate),
        ArchiveKind.Tar => new WriterOptions(CompressionType.None),
        ArchiveKind.TarGz => new WriterOptions(CompressionType.GZip),
        ArchiveKind.TarBz2 => new WriterOptions(CompressionType.BZip2),
        ArchiveKind.TarXz => new WriterOptions(CompressionType.Xz),
        _ => throw new InvalidOperationException($"No writer options for {kind}"),
    };

    /// <summary>
    /// Guards against Zip-Slip-style archive entry names that would resolve
    /// outside the destination root. The .NET archive port enforces this
    /// where the Python implementation doesn't — same fix the
    /// <see cref="Vitriol.Stone.Hosts.ZipStoneHost"/> applies in Sprint 3.
    /// </summary>
    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidDataException("Archive entry has an empty name.");
        }
        if (Path.IsPathRooted(name) || name.StartsWith('/') || name.StartsWith('\\'))
        {
            throw new InvalidDataException(
                $"Archive: refusing absolute entry name '{name}'.");
        }
        foreach (string segment in name.Split('/', '\\'))
        {
            if (segment == "..")
            {
                throw new InvalidDataException(
                    $"Archive: refusing entry name with parent-directory traversal '{name}'.");
            }
        }
    }
}
