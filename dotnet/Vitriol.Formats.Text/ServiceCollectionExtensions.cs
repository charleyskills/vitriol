using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Text;

/// <summary>
/// DI registration for <see cref="PlainTextHandler"/>. The single instance is
/// registered as both <see cref="IFormatReader"/> and <see cref="IFormatWriter"/>
/// so the router resolves the same object for both — that satisfies the
/// <c>ReferenceEquals(reader, writer)</c> check in the IR gate's same-handler
/// pass-through branch.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVitriolText(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<PlainTextHandler>();
        services.AddSingleton<IFormatReader>(sp => sp.GetRequiredService<PlainTextHandler>());
        services.AddSingleton<IFormatWriter>(sp => sp.GetRequiredService<PlainTextHandler>());

        return services;
    }
}
