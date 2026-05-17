using Vitriol.Core.Pipeline;

namespace Vitriol.Cli;

/// <summary>
/// Pipes <see cref="ConversionEvent"/>s to stderr (so stdout stays free for
/// converted bytes if a future <c>--stdout</c> mode lands). Mirrors the Qt
/// signal fanout in <c>app/core/conversion_queue.py</c>.
///
/// <para>Watches for the IR gate's <see cref="ConversionEvent.Stage"/> event
/// and, on first sight, prints the honest-copy warning from Section 12 of
/// the design doc: <em>"This conversion uses Vitriol's intermediate document
/// model; pagination, fonts, comments, and section breaks may be lost. Run
/// with --verify to detect byte-level drift on round-trip."</em></para>
/// </summary>
public sealed class ConsoleProgressReporter : IProgress<ConversionEvent>
{
    private readonly TextWriter _stderr;
    private readonly bool _verbose;
    private readonly bool _verifyEnabled;
    private bool _irWarningEmitted;

    public ConsoleProgressReporter(TextWriter stderr, bool verbose, bool verifyEnabled)
    {
        _stderr = stderr;
        _verbose = verbose;
        _verifyEnabled = verifyEnabled;
    }

    public bool SawIrGate { get; private set; }

    public void Report(ConversionEvent value)
    {
        switch (value)
        {
            case ConversionEvent.Warning w:
                _stderr.WriteLine($"warning: {w.Message}");
                break;
            case ConversionEvent.Error e:
                _stderr.WriteLine($"error: {e.Message}");
                break;
            case ConversionEvent.Stage s when s.Description.Contains("WholeFileIr"):
                SawIrGate = true;
                EmitIrPathWarningOnce();
                if (_verbose)
                {
                    _stderr.WriteLine($"stage: {s.Description}");
                }
                break;
            case ConversionEvent.Stage s when _verbose:
                _stderr.WriteLine($"stage: {s.Description}");
                break;
            case ConversionEvent.Progress p when _verbose:
                _stderr.WriteLine($"progress: {p.Value:P0}");
                break;
            case ConversionEvent.BytesProgress bp when _verbose:
                _stderr.WriteLine($"bytes: {bp.Processed:N0}/{bp.Total:N0}");
                break;
        }
    }

    private void EmitIrPathWarningOnce()
    {
        if (_irWarningEmitted)
        {
            return;
        }
        _irWarningEmitted = true;

        if (_verifyEnabled)
        {
            _stderr.WriteLine(
                "note: this conversion uses Vitriol's intermediate document model. "
                + "--verify will catch any byte-level drift on round-trip.");
        }
        else
        {
            _stderr.WriteLine(
                "warning: this conversion uses Vitriol's intermediate document model; "
                + "pagination, fonts, comments, and section breaks may be lost. "
                + "Run with --verify to detect byte-level drift on round-trip.");
        }
    }
}
