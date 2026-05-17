using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Detection;

/// <summary>
/// Default <see cref="IFormatDetector"/>. Extension first, then magic-byte sniff
/// of the first 4 KB. Sniffers are injected via DI and run in
/// <see cref="IFileSniffer.Order"/> order; first non-null result wins. Mirrors
/// <c>app/core/file_detector.py:detect</c> (line 54).
/// </summary>
public sealed class FormatDetector : IFormatDetector
{
    private const int HeadSize = 4096;

    private readonly IFileSniffer[] _sniffers;

    public FormatDetector(IEnumerable<IFileSniffer> sniffers)
    {
        ArgumentNullException.ThrowIfNull(sniffers);
        _sniffers = sniffers.OrderBy(s => s.Order).ToArray();
    }

    public async ValueTask<string> DetectAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        string ext = ExtensionNormalizer.FromPath(path);

        byte[] buffer = new byte[HeadSize];
        int read;
        try
        {
            await using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: HeadSize, useAsync: true);
            read = await fs.ReadAsync(buffer.AsMemory(0, HeadSize), cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return ext;
        }
        catch (IOException)
        {
            return ext;
        }
        catch (UnauthorizedAccessException)
        {
            return ext;
        }

        ReadOnlySpan<byte> head = buffer.AsSpan(0, read);
        foreach (IFileSniffer sniffer in _sniffers)
        {
            string? hit = sniffer.TrySniff(head, path);
            if (hit is not null)
            {
                return hit;
            }
        }
        return ext;
    }

    public string DetectFromHead(string path, ReadOnlySpan<byte> head)
    {
        ArgumentNullException.ThrowIfNull(path);
        string ext = ExtensionNormalizer.FromPath(path);

        foreach (IFileSniffer sniffer in _sniffers)
        {
            string? hit = sniffer.TrySniff(head, path);
            if (hit is not null)
            {
                return hit;
            }
        }
        return ext;
    }
}
