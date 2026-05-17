using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Image;

/// <summary>
/// DI registration for <see cref="ImageMediaHandler"/>. Registers one
/// singleton instance as <see cref="IMediaHandler"/> so the router's
/// <see cref="Vitriol.Core.Routing.SameMediaHandlerGate"/> can resolve it
/// for every supported image extension.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVitriolImage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ImageMediaHandler>();
        services.AddSingleton<IMediaHandler>(sp => sp.GetRequiredService<ImageMediaHandler>());

        return services;
    }
}
