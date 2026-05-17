using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;

namespace Vitriol.Stone;

/// <summary>
/// Default <see cref="IStoneEngine"/>. Composes registered <see cref="IStoneHost"/>s
/// keyed by extension. Mirrors the dispatch portion of <c>masquerade.convert</c>
/// in <c>app/format_handlers/masquerade.py</c>.
/// </summary>
public sealed class StoneEngine : IStoneEngine
{
    private readonly Dictionary<string, IStoneHost> _hosts;

    public StoneEngine(IEnumerable<IStoneHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        _hosts = new Dictionary<string, IStoneHost>(StringComparer.OrdinalIgnoreCase);
        foreach (IStoneHost host in hosts)
        {
            foreach (string ext in host.SupportedExtensions)
            {
                _hosts.TryAdd(ext, host);
            }
        }
    }

    public IReadOnlyCollection<string> RegisteredExtensions => _hosts.Keys;

    public bool CanEmbedInto(string destinationExtension) =>
        _hosts.TryGetValue(destinationExtension, out IStoneHost? host) && host.CanEmbed;

    public bool CanExtractFrom(string sourceExtension) =>
        _hosts.TryGetValue(sourceExtension, out IStoneHost? host) && host.CanExtract;

    public bool IsLossySource(string sourceExtension) =>
        KnownExtensions.IsLossySource(sourceExtension);

    public async ValueTask<bool> HasEnvelopeAsync(
        Stream source,
        string sourceExtension,
        CancellationToken cancellationToken)
    {
        if (!_hosts.TryGetValue(sourceExtension, out IStoneHost? host))
        {
            return false;
        }
        return await host.HasEnvelopeAsync(source, sourceExtension, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask EmbedAsync(
        Stream source,
        string sourceExtension,
        Stream destination,
        string destinationExtension,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        if (!_hosts.TryGetValue(destinationExtension, out IStoneHost? host))
        {
            throw new UnsupportedConversionException(
                $"Stone has no host registered for destination {destinationExtension}.");
        }

        byte[] sourceBytes = await ReadAllAsync(source, cancellationToken).ConfigureAwait(false);
        await host.EmbedAsync(sourceBytes, sourceExtension, destination, options, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask ExtractAsync(
        Stream source,
        string sourceExtension,
        Stream destination,
        string destinationExtension,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        if (!_hosts.TryGetValue(sourceExtension, out IStoneHost? host))
        {
            throw new UnsupportedConversionException(
                $"Stone has no host registered for source {sourceExtension}.");
        }

        StoneExtractionResult result = await host.ExtractAsync(source, sourceExtension, options, cancellationToken)
            .ConfigureAwait(false);

        await destination.WriteAsync(result.Payload, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<byte[]> ReadAllAsync(Stream source, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
