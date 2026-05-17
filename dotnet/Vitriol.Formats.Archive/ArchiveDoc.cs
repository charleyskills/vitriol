using Vitriol.Core.Ir;

namespace Vitriol.Formats.Archive;

/// <summary>
/// Container format the reader detected. Mirrors the Python
/// <c>_KIND_FOR_EXT</c> table in <c>app/format_handlers/archive_handler.py:35-48</c>.
/// </summary>
public enum ArchiveKind
{
    Zip = 0,
    SevenZip,
    Tar,
    TarGz,
    TarBz2,
    TarXz,
    TarZst,
    /// <summary>Read-only — the RAR format spec is closed and writers don't exist.</summary>
    Rar,
}

/// <summary>
/// Archive IR. Carries the source bytes plus the detected
/// <see cref="ArchiveKind"/>. Mirrors Python's <c>ArchiveDoc</c> dataclass at
/// <c>archive_handler.py:57-67</c>, except the .NET port keeps bytes in
/// memory (capped by the streaming-threshold gate) rather than a path on disk.
/// </summary>
public sealed record ArchiveDoc(
    ReadOnlyMemory<byte> SourceBytes,
    ArchiveKind SourceKind,
    DocumentMetadata Metadata) : IDocument
{
    public ArchiveDoc(ReadOnlyMemory<byte> bytes, ArchiveKind kind)
        : this(bytes, kind, DocumentMetadata.Empty) { }

    public virtual bool Equals(ArchiveDoc? other)
    {
        if (other is null)
        {
            return false;
        }
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        return SourceKind == other.SourceKind
            && Metadata == other.Metadata
            && SourceBytes.Span.SequenceEqual(other.SourceBytes.Span);
    }

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(SourceKind);
        hash.Add(Metadata);
        hash.AddBytes(SourceBytes.Span);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Map between file extensions and <see cref="ArchiveKind"/>. The set of
/// read-supported extensions includes <c>.rar</c> / <c>.cbr</c>; the
/// write-supported set explicitly excludes them.
/// </summary>
public static class ArchiveKindMap
{
    public static readonly IReadOnlyDictionary<string, ArchiveKind> ByExtension =
        new Dictionary<string, ArchiveKind>(StringComparer.OrdinalIgnoreCase)
        {
            [".zip"] = ArchiveKind.Zip,
            [".cbz"] = ArchiveKind.Zip,
            [".7z"] = ArchiveKind.SevenZip,
            [".cb7"] = ArchiveKind.SevenZip,
            [".tar"] = ArchiveKind.Tar,
            [".tar.gz"] = ArchiveKind.TarGz,
            [".tar.bz2"] = ArchiveKind.TarBz2,
            [".tar.xz"] = ArchiveKind.TarXz,
            [".tar.zst"] = ArchiveKind.TarZst,
            [".rar"] = ArchiveKind.Rar,
            [".cbr"] = ArchiveKind.Rar,
        };

    public static readonly IReadOnlySet<string> ReadableExtensions =
        new HashSet<string>(ByExtension.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Extensions we can WRITE. Excludes:
    /// <list type="bullet">
    /// <item><c>.rar</c>/<c>.cbr</c> — closed format, no writer exists.</item>
    /// <item><c>.7z</c>/<c>.cb7</c> — SharpCompress is 7z-read-only;
    /// Python's <c>py7zr</c> has no MIT-licensed pure-managed .NET
    /// counterpart today.</item>
    /// <item><c>.tar.zst</c> — SharpCompress's tar+zstd writer pipeline
    /// is not stable across versions; deferred until verified.</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlySet<string> WritableExtensions =
        new HashSet<string>(
            ByExtension
                .Where(kv => kv.Value != ArchiveKind.Rar
                    && kv.Value != ArchiveKind.SevenZip
                    && kv.Value != ArchiveKind.TarZst)
                .Select(kv => kv.Key),
            StringComparer.OrdinalIgnoreCase);

    public static ArchiveKind FromExtension(string extension) =>
        ByExtension.TryGetValue(extension, out ArchiveKind k)
            ? k
            : throw new ArgumentOutOfRangeException(nameof(extension),
                $"Not a recognized archive extension: {extension}");
}
