namespace Vitriol.Core.Pipeline;

/// <summary>Context for an <see cref="IFormatReader"/> call.</summary>
public sealed record ReadContext(string Extension)
{
    public ReadOnlyMemory<byte> Password { get; init; }

    public IProgress<ConversionEvent>? Progress { get; init; }

    /// <summary>
    /// Original source path, when available. Handlers that key on the
    /// filename (e.g. <c>CsvTextHandler</c> deriving the sheet name from
    /// <c>Path.GetFileNameWithoutExtension</c>) read this. Optional;
    /// <c>null</c> when the source is a non-file stream.
    /// </summary>
    public string? SourceHint { get; init; }
}

/// <summary>Context for an <see cref="IFormatWriter"/> call.</summary>
public sealed record WriteContext(string Extension)
{
    public IProgress<ConversionEvent>? Progress { get; init; }

    /// <summary>
    /// Final destination path, when available. Writers that emit a sidecar
    /// folder layout (e.g. <c>MarkdownHandler</c> bundle output with
    /// <c>&lt;dir&gt;/&lt;stem&gt;/&lt;stem&gt;.md + images/</c>) need the
    /// path beyond just the extension. Optional; <c>null</c> when the
    /// destination is a non-file stream.
    /// </summary>
    public string? DestinationHint { get; init; }
}

/// <summary>Context for an <see cref="IMediaHandler"/> call.</summary>
public sealed record MediaContext
{
    public bool PreserveAnimations { get; init; }

    public IProgress<ConversionEvent>? Progress { get; init; }
}

/// <summary>Options carried into the Stone engine.</summary>
public sealed record StoneOptions
{
    public ReadOnlyMemory<byte> Password { get; init; }

    public bool CrossCategory { get; init; }

    public IProgress<ConversionEvent>? Progress { get; init; }
}
