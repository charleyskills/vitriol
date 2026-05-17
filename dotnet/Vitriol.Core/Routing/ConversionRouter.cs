using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vitriol.Core.Detection;
using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Routing;

/// <summary>
/// Default <see cref="IConversionRouter"/>. Walks gates in <see cref="IRoutingGate.Order"/>
/// order and stops at the first non-<see cref="RoutingDecision.NotApplicable"/>.
/// Mirrors <c>convert_file</c> in <c>app/core/router.py:43-312</c>.
/// </summary>
public sealed class ConversionRouter : IConversionRouter
{
    private readonly IFormatRegistry _registry;
    private readonly IRoutingGate[] _gates;
    private readonly ILogger<ConversionRouter> _logger;

    public ConversionRouter(
        IFormatRegistry registry,
        IEnumerable<IRoutingGate> gates,
        ILogger<ConversionRouter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(gates);

        _registry = registry;
        _gates = gates.OrderBy(g => g.Order).ToArray();
        _logger = logger ?? NullLogger<ConversionRouter>.Instance;
    }

    public IReadOnlyList<IRoutingGate> Gates => _gates;

    public async ValueTask ConvertAsync(
        ConversionJob job,
        IProgress<ConversionEvent>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        ConversionJob normalized = job with
        {
            SourceExtension = ExtensionNormalizer.Normalize(job.SourceExtension),
            DestinationExtension = ExtensionNormalizer.Normalize(job.DestinationExtension),
        };

        RoutingContext context = new(normalized, _registry, progress);

        foreach (IRoutingGate gate in _gates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Routing gate {Gate} (order {Order}) probing {Src} -> {Dst}",
                gate.Name, gate.Order, context.Job.SourceExtension, context.Job.DestinationExtension);

            RoutingDecision decision = await gate.TryHandleAsync(context, cancellationToken)
                .ConfigureAwait(false);

            switch (decision)
            {
                case RoutingDecision.Handled handled:
                    _logger.LogInformation("Gate {Gate} handled {Src} -> {Dst}",
                        handled.GateName, context.Job.SourceExtension, context.Job.DestinationExtension);
                    return;
                case RoutingDecision.Refused refused:
                    _logger.LogWarning("Gate {Gate} refused {Src} -> {Dst}: {Reason}",
                        refused.GateName, context.Job.SourceExtension, context.Job.DestinationExtension, refused.Reason);
                    throw new UnsupportedConversionException(refused.Reason);
                case RoutingDecision.NotApplicable:
                    continue;
                default:
                    throw new InvalidOperationException(
                        $"Gate {gate.Name} returned an unknown RoutingDecision: {decision.GetType()}");
            }
        }

        throw new UnsupportedConversionException(
            $"No routing gate handled {context.Job.SourceExtension} -> {context.Job.DestinationExtension}.");
    }
}
