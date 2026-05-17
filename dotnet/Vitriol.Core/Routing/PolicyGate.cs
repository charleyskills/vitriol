using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;

namespace Vitriol.Core.Routing;

/// <summary>
/// Source-type policy gate. Refuses any conversion where BOTH source and
/// target are auto-execute (.py↔.exe) to prevent the tool from being used as
/// a malware-wrapping pipeline. Also promotes <c>Masquerade=true</c> for
/// Stone-only sources (.zip, .exe) so the dropdown populates without the
/// user toggling Stone explicitly. Mirrors <c>router.py:70-88</c>.
///
/// This gate never <see cref="RoutingDecision.Handled"/>s — it either refuses
/// or returns <see cref="RoutingDecision.NotApplicable"/> after mutating the
/// job's <see cref="ConversionJob.Masquerade"/> flag in the context.
/// </summary>
public sealed class PolicyGate : IRoutingGate
{
    public int Order => 0;

    public string Name => "Policy";

    public ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string srcExt = context.Job.SourceExtension;
        string dstExt = context.Job.DestinationExtension;

        // Block .py↔.exe both-ends.
        if (KnownExtensions.IsAutoExecute(srcExt) && KnownExtensions.IsAutoExecute(dstExt))
        {
            return ValueTask.FromResult<RoutingDecision>(new RoutingDecision.Refused(
                Name,
                $"Vitriol does not allow {srcExt} -> {dstExt} conversions. "
                + ".py and .exe cannot be both the source AND target of the same conversion -- "
                + "this would let the tool be used as a malware-wrapping pipeline. "
                + "Convert your source to a non-executable Stone host first "
                + "(.zip / .png / .wav / etc.), then convert that to your final target if needed."));
        }

        // Stone-only sources get masquerade auto-engaged.
        if (KnownExtensions.IsStoneOnlySource(srcExt) && !context.Job.Masquerade)
        {
            context.PromoteMasquerade();
        }

        return ValueTask.FromResult<RoutingDecision>(RoutingDecision.NotApplicable.Instance);
    }
}
