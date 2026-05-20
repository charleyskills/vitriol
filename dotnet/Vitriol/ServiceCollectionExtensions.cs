using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Registry;
using Vitriol.Stone;
using Vitriol.Formats.Text;
using Vitriol.Formats.Image;
using Vitriol.Formats.Tabular;
using Vitriol.Formats.Archive;
using Vitriol.Formats.Crypto;
using Vitriol.Formats.Doc;

namespace Vitriol;

/// <summary>
/// Registers the complete Vitriol engine — all format handlers and routing infrastructure.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the full Vitriol stack: Core router + Stone engine + all eight format libraries
    /// (Text, Image, Tabular, Archive, Crypto, Doc).
    /// </summary>
    /// <remarks>
    /// To register only a subset of format handlers, call the individual
    /// <c>AddVitriolCore()</c>, <c>AddVitriolStone()</c>, <c>AddVitriolText()</c>, etc.
    /// extension methods from the respective library packages instead.
    /// </remarks>
    public static IServiceCollection AddVitriol(this IServiceCollection services) =>
        services
            .AddVitriolCore()
            .AddVitriolStone()
            .AddVitriolText()
            .AddVitriolImage()
            .AddVitriolTabular()
            .AddVitriolArchive()
            .AddVitriolCrypto()
            .AddVitriolDoc();
}
