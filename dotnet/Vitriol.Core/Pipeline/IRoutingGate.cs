namespace Vitriol.Core.Pipeline;

/// <summary>
/// One branch of the router's dispatch table. Mirrors one of the gates in
/// <c>app/core/router.py:43-312</c>. The router walks gates in priority order
/// and stops at the first non-<see cref="RoutingDecision.NotApplicable"/>.
/// </summary>
public interface IRoutingGate
{
    /// <summary>Priority order. Lower values run first. Stable.</summary>
    int Order { get; }

    /// <summary>Human-readable identifier for logging and diagnostics.</summary>
    string Name { get; }

    ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// State and services carried into each gate's <c>TryHandleAsync</c>. Mutable
/// so the policy gate can promote <c>Masquerade=true</c> for Stone-only
/// sources (mirroring the local-variable mutation in <c>router.py:87</c>).
/// </summary>
public sealed class RoutingContext
{
    public RoutingContext(
        ConversionJob job,
        IFormatRegistry registry,
        IProgress<ConversionEvent>? progress,
        List<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(registry);

        Job = job;
        Registry = registry;
        Progress = progress;
        Warnings = warnings ?? new List<string>();
    }

    public ConversionJob Job { get; private set; }

    public IFormatRegistry Registry { get; }

    public IProgress<ConversionEvent>? Progress { get; }

    public List<string> Warnings { get; }

    public void PromoteMasquerade()
    {
        if (!Job.Masquerade)
        {
            Job = Job with { Masquerade = true };
        }
    }

    public void AddWarning(string message)
    {
        Warnings.Add(message);
        Progress?.Report(new ConversionEvent.Warning(message));
    }
}

/// <summary>Result of a gate's attempt to claim a job.</summary>
public abstract record RoutingDecision
{
    protected RoutingDecision() { }

    public sealed record NotApplicable : RoutingDecision
    {
        public static readonly NotApplicable Instance = new();
    }

    public sealed record Handled(string GateName) : RoutingDecision;

    public sealed record Refused(string GateName, string Reason) : RoutingDecision;
}
