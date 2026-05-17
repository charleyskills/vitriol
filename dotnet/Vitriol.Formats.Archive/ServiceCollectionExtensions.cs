using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Archive;

/// <summary>
/// DI registration for <see cref="ArchiveHandler"/>. One singleton, two
/// interface registrations so the IR gate's same-handler check resolves.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVitriolArchive(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ArchiveHandler>();
        services.AddSingleton<IFormatReader>(sp => sp.GetRequiredService<ArchiveHandler>());
        services.AddSingleton<IFormatWriter>(sp => sp.GetRequiredService<ArchiveHandler>());

        return services;
    }
}
