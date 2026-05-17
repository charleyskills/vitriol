using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Routing;
using Vitriol.Stone.Hosts;

namespace Vitriol.Stone;

/// <summary>
/// DI registration for Vitriol.Stone. Registers the Stone engine, all carrier
/// hosts, and the three Stone-dependent routing gates
/// (<see cref="CompilerGate"/>, <see cref="PhilosophersStoneGate"/>,
/// <see cref="CrossCategoryDocumentToMediaGate"/>) that
/// <c>AddVitriolCore()</c> deliberately left out.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVitriolStone(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Hosts.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStoneHost, TxtStoneHost>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStoneHost, ZipStoneHost>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStoneHost, PngStoneHost>());

        // Engine.
        services.TryAddSingleton<IStoneEngine, StoneEngine>();

        // Stone-aware routing gates (paired with AddVitriolCore's non-Stone gates).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, CompilerGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, PhilosophersStoneGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoutingGate, CrossCategoryDocumentToMediaGate>());

        return services;
    }
}
