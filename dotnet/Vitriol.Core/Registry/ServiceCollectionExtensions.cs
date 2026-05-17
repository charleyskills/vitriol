using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vitriol.Core.Detection;
using Vitriol.Core.Detection.Sniffers;
using Vitriol.Core.Ir.Adapters;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Routing;

namespace Vitriol.Core.Registry;

/// <summary>
/// DI registration for Vitriol.Core. Registers the IR adapter registry, the
/// format detector with its built-in sniffers, the format registry, the
/// conversion router, and every non-Stone routing gate. Stone-aware gates
/// (Compiler, PhilosophersStone, CrossCategoryDocumentToMedia) are registered
/// by <c>AddVitriolStone()</c> in the Vitriol.Stone assembly.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVitriolCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // IR adapters (Sprint 1).
        services.TryAddSingleton<AdapterRegistry>();

        // Detection.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFileSniffer, MagicByteSniffer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFileSniffer, ZipSubtypeSniffer>());
        services.TryAddSingleton<IFormatDetector, FormatDetector>();

        // Registry.
        services.TryAddSingleton<IFormatRegistry, FormatRegistry>();

        // Routing — non-Stone gates only.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, PolicyGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, DiskSpaceHintGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, TrailerEnvelopeGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, SameMediaHandlerGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, CrossCategoryImageToDocumentGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, StreamingGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, WholeFileIrGate>());

        services.TryAddSingleton<IConversionRouter, ConversionRouter>();

        return services;
    }
}
