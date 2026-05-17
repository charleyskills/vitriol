namespace Vitriol.Core.Detection;

/// <summary>
/// One probe in the detection pipeline. Mirrors a single magic-byte clause in
/// <c>app/core/file_detector.py:_sniff</c> (lines 66-119).
/// </summary>
public interface IFileSniffer
{
    int Order { get; }

    string? TrySniff(ReadOnlySpan<byte> head, string path);
}
