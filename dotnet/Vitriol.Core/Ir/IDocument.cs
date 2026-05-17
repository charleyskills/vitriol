namespace Vitriol.Core.Ir;

/// <summary>
/// Marker for the three concrete IR shapes the router moves through the pipeline.
/// Mirrors the Python <c>DOC_KIND</c> categories ("text", "tabular", "binary",
/// "archive", "pandoc") in <c>app/format_handlers/__init__.py</c>.
/// </summary>
public interface IDocument
{
    DocumentMetadata Metadata { get; }
}
