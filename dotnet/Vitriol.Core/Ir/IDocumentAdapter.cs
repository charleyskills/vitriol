namespace Vitriol.Core.Ir;

/// <summary>
/// Translates between IR kinds. Mirrors the <c>ADAPTERS</c> dict in
/// <c>app/core/intermediate.py:213–231</c>. Implementations should emit
/// <see cref="Vitriol.Core.Pipeline.ConversionEvent.Warning"/> events when the
/// translation is known-lossy (e.g. <c>TextDocToTabular</c> drops prose).
/// </summary>
public interface IDocumentAdapter<in TFrom, out TTo>
    where TFrom : IDocument
    where TTo : IDocument
{
    TTo Adapt(TFrom source, IProgress<Pipeline.ConversionEvent>? progress = null);
}
