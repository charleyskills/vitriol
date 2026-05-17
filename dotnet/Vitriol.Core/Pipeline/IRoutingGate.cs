namespace Vitriol.Core.Pipeline;

/// <summary>
/// One branch of the router's dispatch table. Mirrors one of the nine gates in
/// <c>app/core/router.py:43–312</c>. The router walks gates in priority order
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

/// <summary>State and services carried into each gate's <c>TryHandleAsync</c>.</summary>
public sealed record RoutingContext(
    ConversionJob Job,
    IFormatRegistry Registry,
    IProgress<ConversionEvent>? Progress,
    List<string> Warnings);

/// <summary>
/// Result of a gate's attempt to claim a job.
/// </summary>
public abstract record RoutingDecision
{
    protected RoutingDecision() { }

    /// <summary>This gate does not apply; continue to the next gate.</summary>
    public sealed record NotApplicable : RoutingDecision
    {
        public static readonly NotApplicable Instance = new();
    }

    /// <summary>This gate handled the job successfully.</summary>
    public sealed record Handled(string GateName) : RoutingDecision;

    /// <summary>This gate refuses the job (terminal — no further gates tried).</summary>
    public sealed record Refused(string GateName, string Reason) : RoutingDecision;
}
