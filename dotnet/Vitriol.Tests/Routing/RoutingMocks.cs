namespace Vitriol.Tests.Routing;

/// <summary>
/// Lightweight hand-rolled mocks for routing gate tests. Avoids pulling in Moq
/// for what is essentially a small fixed surface.
/// </summary>
internal static class RoutingMocks
{
    public sealed class FakeFormatReader : IFormatReader
    {
        public FakeFormatReader(DocKind kind, params string[] extensions)
        {
            Kind = kind;
            SupportedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        }

        public DocKind Kind { get; }
        public IReadOnlySet<string> SupportedExtensions { get; }
        public int ReadCalls { get; private set; }

        public ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken)
        {
            ReadCalls++;
            // Return an empty TextDoc — gate tests only care that read+write were called.
            return new(TextDoc.Empty);
        }
    }

    public sealed class FakeFormatWriter : IFormatWriter
    {
        public FakeFormatWriter(DocKind kind, params string[] extensions)
        {
            Kind = kind;
            SupportedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        }

        public DocKind Kind { get; }
        public IReadOnlySet<string> SupportedExtensions { get; }
        public int WriteCalls { get; private set; }
        public IDocument? LastDocument { get; private set; }

        public ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken)
        {
            WriteCalls++;
            LastDocument = document;
            return ValueTask.CompletedTask;
        }
    }

    public sealed class FakeMediaHandler : IMediaHandler
    {
        public FakeMediaHandler(MediaCategory category, params string[] extensions)
        {
            Category = category;
            SupportedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        }

        public MediaCategory Category { get; }
        public IReadOnlySet<string> SupportedExtensions { get; }
        public int ConvertCalls { get; private set; }
        public string? LastSrcExt { get; private set; }
        public string? LastDstExt { get; private set; }

        public ValueTask ConvertAsync(string srcExt, Stream src, string dstExt, Stream dst,
            MediaContext context, CancellationToken cancellationToken)
        {
            ConvertCalls++;
            LastSrcExt = srcExt;
            LastDstExt = dstExt;
            return ValueTask.CompletedTask;
        }
    }

    public sealed class FakeStoneEngine : IStoneEngine
    {
        public HashSet<string> EmbedTargets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExtractSources { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> EnvelopeBearers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LossyOverride { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int EmbedCalls { get; private set; }
        public int ExtractCalls { get; private set; }

        public bool CanEmbedInto(string destinationExtension) => EmbedTargets.Contains(destinationExtension);
        public bool CanExtractFrom(string sourceExtension) => ExtractSources.Contains(sourceExtension);
        public bool IsLossySource(string ext) =>
            LossyOverride.Contains(ext) || Vitriol.Core.Registry.KnownExtensions.IsLossySource(ext);

        public ValueTask<bool> HasEnvelopeAsync(Stream source, string sourceExtension, CancellationToken cancellationToken)
            => new(EnvelopeBearers.Contains(sourceExtension));

        public ValueTask EmbedAsync(Stream source, string srcExt, Stream destination, string dstExt,
            StoneOptions options, CancellationToken cancellationToken)
        {
            EmbedCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ExtractAsync(Stream source, string srcExt, Stream destination, string dstExt,
            StoneOptions options, CancellationToken cancellationToken)
        {
            ExtractCalls++;
            return ValueTask.CompletedTask;
        }
    }

    public sealed class FakeTrailerReader : ITrailerEnvelopeReader
    {
        public FakeTrailerReader(string extension, TrailerEnvelopeResult? result)
        {
            _extension = extension;
            _result = result;
        }

        private readonly string _extension;
        private readonly TrailerEnvelopeResult? _result;

        public bool CanRead(string sourceExtension) =>
            string.Equals(sourceExtension, _extension, StringComparison.OrdinalIgnoreCase);

        public ValueTask<TrailerEnvelopeResult?> TryReadAsync(Stream source, string ext, CancellationToken ct)
            => new(_result);
    }

    public sealed class FakeProgress<T> : IProgress<T>
    {
        public List<T> Events { get; } = new();
        public void Report(T value) => Events.Add(value);
    }
}
