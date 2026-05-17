using System.Buffers.Binary;
using System.Text;
using Vitriol.Core.Pipeline;
using Vitriol.Stone.Envelope;

namespace Vitriol.Stone.Hosts;

/// <summary>
/// WAV host (UCMSv1 plain envelope inside the RIFF data chunk). Builds a real
/// 8 kHz mono 16-bit PCM WAV file whose <c>data</c> chunk holds the envelope
/// verbatim. Mirrors <c>_wav_embed</c> / <c>_wav_extract</c> in
/// <c>app/format_handlers/masquerade.py:499-515, 880-925</c>.
///
/// <para>The v3 music-mode variant (44.1 kHz stereo with payload in the
/// bottom 4 bits of each sample) requires the music synth module and is
/// deferred to a follow-up sprint. v1 same-category WAV → WAV round-trip
/// is byte-identical through this host.</para>
/// </summary>
public sealed class WavStoneHost : IStoneHost
{
    private const int SampleRate = 8000;
    private const short NumChannels = 1;
    private const short BitsPerSample = 16;

    private static ReadOnlySpan<byte> Riff => "RIFF"u8;
    private static ReadOnlySpan<byte> Wave => "WAVE"u8;
    private static ReadOnlySpan<byte> Fmt => "fmt "u8;
    private static ReadOnlySpan<byte> Data => "data"u8;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav" };

    public bool CanEmbed => true;

    public bool CanExtract => true;

    public async ValueTask<bool> HasEnvelopeAsync(Stream source, string extension, CancellationToken cancellationToken)
    {
        if (!source.CanSeek)
        {
            return false;
        }
        source.Position = 0;
        byte[] head = new byte[12];
        int read = await ReadExactAsync(source, head, cancellationToken).ConfigureAwait(false);
        if (read < 12 || !head.AsSpan(0, 4).SequenceEqual(Riff) || !head.AsSpan(8, 4).SequenceEqual(Wave))
        {
            return false;
        }

        await foreach ((string tag, byte[] body) in EnumerateChunksAsync(source, cancellationToken).ConfigureAwait(false))
        {
            if (tag == "data")
            {
                return body.AsSpan().IndexOf(UcmsMagic.V1) >= 0;
            }
        }
        return false;
    }

    public ValueTask EmbedAsync(
        ReadOnlyMemory<byte> sourceBytes,
        string sourceExtension,
        Stream destination,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        UcmsEnvelope envelope = new(sourceExtension, sourceBytes);
        byte[] env = envelope.Build();

        // 16-bit sample alignment: pad data chunk to even bytes.
        bool padded = (env.Length & 1) == 1;
        int dataSize = padded ? env.Length + 1 : env.Length;

        const int FmtChunkBodySize = 16;
        int riffSize = 4 /* "WAVE" */
            + (8 + FmtChunkBodySize) /* fmt chunk */
            + (8 + dataSize); /* data chunk */

        int totalSize = 8 + riffSize;
        byte[] buffer = new byte[totalSize];
        Span<byte> span = buffer;
        int pos = 0;

        // RIFF header.
        Riff.CopyTo(span);
        pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], (uint)riffSize);
        pos += 4;
        Wave.CopyTo(span[pos..]);
        pos += 4;

        // fmt chunk.
        Fmt.CopyTo(span[pos..]);
        pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], FmtChunkBodySize);
        pos += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], 1); // PCM format
        pos += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)NumChannels);
        pos += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], SampleRate);
        pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span[pos..],
            (uint)(SampleRate * NumChannels * BitsPerSample / 8));
        pos += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(span[pos..],
            (ushort)(NumChannels * BitsPerSample / 8));
        pos += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)BitsPerSample);
        pos += 2;

        // data chunk.
        Data.CopyTo(span[pos..]);
        pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], (uint)dataSize);
        pos += 4;
        env.CopyTo(span[pos..]);
        pos += env.Length;
        if (padded)
        {
            span[pos] = 0;
            pos++;
        }

        return destination.WriteAsync(buffer.AsMemory(0, pos), cancellationToken);
    }

    public async ValueTask<StoneExtractionResult> ExtractAsync(
        Stream source,
        string sourceExtension,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        source.Position = 0;
        byte[] head = new byte[12];
        if (await ReadExactAsync(source, head, cancellationToken).ConfigureAwait(false) < 12
            || !head.AsSpan(0, 4).SequenceEqual(Riff)
            || !head.AsSpan(8, 4).SequenceEqual(Wave))
        {
            throw new StoneEnvelopeException("Not a WAV file.");
        }

        byte[]? dataBlob = null;
        await foreach ((string tag, byte[] body) in EnumerateChunksAsync(source, cancellationToken).ConfigureAwait(false))
        {
            if (tag == "data")
            {
                dataBlob = body;
                break;
            }
        }
        if (dataBlob is null)
        {
            throw new StoneEnvelopeException("WAV: no data chunk found.");
        }

        UcmsEnvelope envelope = UcmsEnvelope.Parse(dataBlob);
        return new StoneExtractionResult(envelope.Payload, envelope.Extension);
    }

    /// <summary>
    /// Yields each RIFF sub-chunk after the initial 12-byte header. Each
    /// chunk is &lt;4-byte ASCII tag&gt; + &lt;4-byte little-endian size&gt; + body.
    /// Bodies pad to even byte boundaries (a 1-byte pad after odd sizes).
    /// </summary>
    private static async IAsyncEnumerable<(string Tag, byte[] Body)> EnumerateChunksAsync(
        Stream source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        byte[] header = new byte[8];
        while (true)
        {
            int read = await ReadExactAsync(source, header, cancellationToken).ConfigureAwait(false);
            if (read < 8)
            {
                yield break;
            }
            string tag = Encoding.ASCII.GetString(header, 0, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));

            byte[] body = new byte[size];
            int bodyRead = await ReadExactAsync(source, body, cancellationToken).ConfigureAwait(false);
            if (bodyRead < (int)size)
            {
                yield break;
            }
            yield return (tag, body);

            if ((size & 1) == 1)
            {
                // Skip 1-byte pad.
                byte[] pad = new byte[1];
                _ = await source.ReadAsync(pad.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<int> ReadExactAsync(Stream source, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await source.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }
            total += n;
        }
        return total;
    }
}
