using System.IO.Compression;
using Vitriol.Core.Pipeline;

namespace Vitriol.Stone.Hosts;

/// <summary>
/// ZIP host: a real STORED-method archive whose only member is
/// <c>original.{ext}</c> containing the source bytes verbatim. No envelope,
/// no encryption — the file opens in any unzip tool. Mirrors
/// <c>_zip_embed</c> / <c>_zip_extract</c> in
/// <c>app/format_handlers/masquerade.py:5315-5356</c>.
///
/// <para>The .NET port adds path-traversal validation that Vitriol's Python
/// version lacks: rejects member names containing <c>..</c> segments or
/// absolute paths during extract.</para>
/// </summary>
public sealed class ZipStoneHost : IStoneHost
{
    public const string MemberPrefix = "original";

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip" };

    public bool CanEmbed => true;

    public bool CanExtract => true;

    public ValueTask<bool> HasEnvelopeAsync(Stream source, string extension, CancellationToken cancellationToken)
    {
        try
        {
            using ZipArchive zip = new(source, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count != 1)
            {
                return ValueTask.FromResult(false);
            }
            string name = zip.Entries[0].FullName;
            return ValueTask.FromResult(
                name.StartsWith(MemberPrefix + ".", StringComparison.Ordinal));
        }
        catch (InvalidDataException)
        {
            return ValueTask.FromResult(false);
        }
    }

    public async ValueTask EmbedAsync(
        ReadOnlyMemory<byte> sourceBytes,
        string sourceExtension,
        Stream destination,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        string ext = NormalizeExtension(sourceExtension);
        string memberName = MemberPrefix + ext;

        using ZipArchive zip = new(destination, ZipArchiveMode.Create, leaveOpen: true);
        ZipArchiveEntry entry = zip.CreateEntry(memberName, CompressionLevel.NoCompression);
        await using Stream es = entry.Open();
        await es.WriteAsync(sourceBytes, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<StoneExtractionResult> ExtractAsync(
        Stream source,
        string sourceExtension,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        using ZipArchive zip = new(source, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count != 1)
        {
            throw new StoneEnvelopeException(
                $"ZIP host: expected one member, got {zip.Entries.Count}; "
                + "treating as opaque bytes.");
        }

        ZipArchiveEntry entry = zip.Entries[0];
        string name = entry.FullName;

        ValidateMemberName(name);

        if (!name.StartsWith(MemberPrefix + ".", StringComparison.Ordinal))
        {
            throw new StoneEnvelopeException(
                $"ZIP host: member name '{name}' doesn't match Stone-built "
                + "'original.*' pattern.");
        }

        await using Stream es = entry.Open();
        using MemoryStream buffer = new();
        await es.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        string recoveredExt = name[MemberPrefix.Length..];
        if (recoveredExt.Length == 0)
        {
            recoveredExt = ".bin";
        }
        if (!recoveredExt.StartsWith('.'))
        {
            recoveredExt = "." + recoveredExt;
        }

        return new StoneExtractionResult(buffer.ToArray(), recoveredExt);
    }

    /// <summary>
    /// Rejects entry names that would attempt path traversal or write outside
    /// the destination. Vitriol's Python version doesn't enforce this; the
    /// port does because Zip-Slip is a well-known archive vulnerability.
    /// </summary>
    private static void ValidateMemberName(string name)
    {
        if (name.Length == 0)
        {
            throw new StoneEnvelopeException("ZIP host: empty member name.");
        }
        if (Path.IsPathRooted(name) || name.StartsWith('/') || name.StartsWith('\\'))
        {
            throw new StoneEnvelopeException(
                $"ZIP host: refusing absolute member name '{name}'.");
        }
        foreach (string segment in name.Split('/', '\\'))
        {
            if (segment == "..")
            {
                throw new StoneEnvelopeException(
                    $"ZIP host: refusing member name with parent-directory traversal '{name}'.");
            }
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }
        return extension.StartsWith('.') ? extension : "." + extension;
    }
}
