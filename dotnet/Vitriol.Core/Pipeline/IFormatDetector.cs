namespace Vitriol.Core.Pipeline;

/// <summary>
/// Format detection. Extension first, then magic-byte sniff on the file head.
/// Mirrors <c>app/core/file_detector.py:detect</c> (line 54).
/// </summary>
public interface IFormatDetector
{
    ValueTask<string> DetectAsync(string path, CancellationToken cancellationToken);

    string DetectFromHead(string path, ReadOnlySpan<byte> head);
}
