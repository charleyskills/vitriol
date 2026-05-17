using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Crypto;

/// <summary>
/// DI registration for <see cref="CryptoHandler"/>. Single singleton registered
/// as both <see cref="IFormatReader"/> and <see cref="IFormatWriter"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVitriolCrypto(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<CryptoHandler>();
        services.AddSingleton<IFormatReader>(sp => sp.GetRequiredService<CryptoHandler>());
        services.AddSingleton<IFormatWriter>(sp => sp.GetRequiredService<CryptoHandler>());

        return services;
    }
}
