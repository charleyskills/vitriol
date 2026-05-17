using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Tabular;

/// <summary>
/// DI registration for <see cref="CsvTextHandler"/> and <see cref="XlsxHandler"/>.
/// Each handler is a singleton registered against both <see cref="IFormatReader"/>
/// and <see cref="IFormatWriter"/> so the router resolves the same instance and
/// the same-handler pass-through check passes.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVitriolTabular(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<CsvTextHandler>();
        services.AddSingleton<IFormatReader>(sp => sp.GetRequiredService<CsvTextHandler>());
        services.AddSingleton<IFormatWriter>(sp => sp.GetRequiredService<CsvTextHandler>());

        services.TryAddSingleton<XlsxHandler>();
        services.AddSingleton<IFormatReader>(sp => sp.GetRequiredService<XlsxHandler>());
        services.AddSingleton<IFormatWriter>(sp => sp.GetRequiredService<XlsxHandler>());

        return services;
    }
}
