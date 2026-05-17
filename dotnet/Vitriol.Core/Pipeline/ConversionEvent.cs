namespace Vitriol.Core.Pipeline;

/// <summary>
/// A discriminated union of events emitted during a conversion. Mirrors the
/// Qt signals fanned out by the Python <c>JobSignals</c> in
/// <c>app/core/conversion_queue.py</c> (progress / warning / failed /
/// bytes_progress).
/// </summary>
public abstract record ConversionEvent
{
    protected ConversionEvent() { }

    public sealed record Progress(double Value) : ConversionEvent;

    public sealed record BytesProgress(long Processed, long Total) : ConversionEvent;

    public sealed record Stage(string Description) : ConversionEvent;

    public sealed record Warning(string Message) : ConversionEvent;

    public sealed record Error(string Message, Exception? Exception = null) : ConversionEvent;
}
