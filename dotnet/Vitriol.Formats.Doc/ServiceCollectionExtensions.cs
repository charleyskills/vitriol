using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuestPDF.Infrastructure;
using Vitriol.Core.Pipeline;
using Vitriol.Formats.Doc.Trailer;

namespace Vitriol.Formats.Doc;

/// <summary>
/// DI registration for <see cref="DocxHandler"/>, <see cref="PdfHandler"/>,
/// <see cref="MarkdownHandler"/>, and their trailer-envelope readers
/// (<see cref="DocxTrailerEnvelopeReader"/> + <see cref="PdfTrailerEnvelopeReader"/>).
///
/// <para>Once this runs, the Sprint 2 <c>TrailerEnvelopeGate</c> sees real
/// readers and the byte-perfect short-circuit for
/// <c>PNG → DOCX → PNG</c> / <c>WAV → PDF → WAV</c> becomes reachable.</para>
///
/// <para><b>QuestPDF license</b>: Community edition is free for individuals
/// and companies under $1M revenue. Setting it here ensures every CLI run
/// initializes the license before the first PDF write.</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVitriolDoc(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        QuestPDF.Settings.License = LicenseType.Community;

        services.TryAddSingleton<DocxHandler>();
        services.AddSingleton<IFormatReader>(sp => sp.GetRequiredService<DocxHandler>());
        services.AddSingleton<IFormatWriter>(sp => sp.GetRequiredService<DocxHandler>());

        services.TryAddSingleton<PdfHandler>();
        services.AddSingleton<IFormatReader>(sp => sp.GetRequiredService<PdfHandler>());
        services.AddSingleton<IFormatWriter>(sp => sp.GetRequiredService<PdfHandler>());

        services.TryAddSingleton<MarkdownHandler>();
        services.AddSingleton<IFormatReader>(sp => sp.GetRequiredService<MarkdownHandler>());
        services.AddSingleton<IFormatWriter>(sp => sp.GetRequiredService<MarkdownHandler>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITrailerEnvelopeReader, DocxTrailerEnvelopeReader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITrailerEnvelopeReader, PdfTrailerEnvelopeReader>());

        return services;
    }
}
