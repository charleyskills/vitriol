namespace Vitriol.Core.Pipeline;

/// <summary>Context for an <see cref="IFormatReader"/> call.</summary>
public sealed record ReadContext(string Extension)
{
    public ReadOnlyMemory<byte> Password { get; init; }

    public IProgress<ConversionEvent>? Progress { get; init; }
}

/// <summary>Context for an <see cref="IFormatWriter"/> call.</summary>
public sealed record WriteContext(string Extension)
{
    public IProgress<ConversionEvent>? Progress { get; init; }
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
