using System.Buffers.Binary;
using System.Text;
using Vitriol.Core.Pipeline;
using Vitriol.Stone.Envelope;

namespace Vitriol.Stone.Hosts;

/// <summary>
/// AIFF host (UCMSv1 plain envelope inside the SSND chunk). Builds a real
/// 8 kHz mono 16-bit AIFF FORM container whose <c>SSND</c> chunk holds the
/// envelope verbatim after the standard 8-byte offset/blockSize prefix.
/// Mirrors <c>_aiff_embed</c> / <c>_aiff_extract</c> in
/// <c>app/format_handlers/masquerade.py:581-606, 634-678</c>.
///
/// <para>The v3 music-mode variant is deferred. v1 same-category AIFF round
/// trips byte-identically through this host.</para>
/// </summary>
public sealed class AiffStoneHost : IStoneHost
{
    private const uint SampleRate = 8000;
    private const short NumChannels = 1;
    private const short BitsPerSample = 16;

    private static ReadOnlySpan<byte> Form => "FORM"u8;
    private static ReadOnlySpan<byte> AiffTag => "AIFF"u8;
    private static ReadOnlySpan<byte> Comm => "COMM"u8;
    private static ReadOnlySpan<byte> Ssnd => "SSND"u8;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".aiff", ".aif" };

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
        if (read < 12 || !head.AsSpan(0, 4).SequenceEqual(Form) || !head.AsSpan(8, 4).SequenceEqual(AiffTag))
        {
            return false;
        }

        await foreach ((string tag, byte[] body) in EnumerateChunksAsync(source, cancellationToken).ConfigureAwait(false))
        {
            if (tag == "SSND" && body.Length > 8)
            {
                ReadOnlySpan<byte> ssndBody = body.AsSpan(8);
                return ssndBody.IndexOf(UcmsMagic.V1) >= 0;
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

        bool padded = (env.Length & 1) == 1;
        if (padded)
        {
            byte[] aligned = new byte[env.Length + 1];
            env.CopyTo(aligned, 0);
            env = aligned;
        }
        int nFrames = env.Length / (NumChannels * (BitsPerSample / 8));

        // COMM chunk body: numChannels(2 BE) numSampleFrames(4 BE)
        //                  sampleSize(2 BE) sampleRate(10 IEEE 754 80-bit BE)
        byte[] commBody = new byte[18];
        BinaryPrimitives.WriteInt16BigEndian(commBody.AsSpan(0, 2), NumChannels);
        BinaryPrimitives.WriteUInt32BigEndian(commBody.AsSpan(2, 4), (uint)nFrames);
        BinaryPrimitives.WriteInt16BigEndian(commBody.AsSpan(6, 2), BitsPerSample);
        EncodeExtendedFloat(SampleRate).CopyTo(commBody.AsSpan(8, 10));

        // SSND chunk body: offset(4 BE) blockSize(4 BE) sampleData
        byte[] ssndBody = new byte[8 + env.Length];
        // offset = 0, blockSize = 0
        env.CopyTo(ssndBody, 8);

        int commChunkSize = 8 + commBody.Length + ((commBody.Length & 1) == 1 ? 1 : 0);
        int ssndChunkSize = 8 + ssndBody.Length + ((ssndBody.Length & 1) == 1 ? 1 : 0);
        int bodySize = 4 /* "AIFF" */ + commChunkSize + ssndChunkSize;
        int totalSize = 8 + bodySize;

        byte[] buffer = new byte[totalSize];
        Span<byte> span = buffer;
        int pos = 0;

        Form.CopyTo(span);
        pos += 4;
        BinaryPrimitives.WriteUInt32BigEndian(span[pos..], (uint)bodySize);
        pos += 4;
        AiffTag.CopyTo(span[pos..]);
        pos += 4;

        // COMM chunk.
        Comm.CopyTo(span[pos..]);
        pos += 4;
        BinaryPrimitives.WriteUInt32BigEndian(span[pos..], (uint)commBody.Length);
        pos += 4;
        commBody.CopyTo(span[pos..]);
        pos += commBody.Length;
        if ((commBody.Length & 1) == 1)
        {
            span[pos] = 0;
            pos++;
        }

        // SSND chunk.
        Ssnd.CopyTo(span[pos..]);
        pos += 4;
        BinaryPrimitives.WriteUInt32BigEndian(span[pos..], (uint)ssndBody.Length);
        pos += 4;
        ssndBody.CopyTo(span[pos..]);
        pos += ssndBody.Length;
        if ((ssndBody.Length & 1) == 1)
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
            || !head.AsSpan(0, 4).SequenceEqual(Form)
            || !head.AsSpan(8, 4).SequenceEqual(AiffTag))
        {
            throw new StoneEnvelopeException("Not an AIFF file.");
        }

        byte[]? ssndBlob = null;
        await foreach ((string tag, byte[] body) in EnumerateChunksAsync(source, cancellationToken).ConfigureAwait(false))
        {
            if (tag == "SSND")
            {
                ssndBlob = body.Length >= 8 ? body[8..] : Array.Empty<byte>();
                break;
            }
        }
        if (ssndBlob is null)
        {
            throw new StoneEnvelopeException("AIFF: no SSND chunk.");
        }

        UcmsEnvelope envelope = UcmsEnvelope.Parse(ssndBlob);
        return new StoneExtractionResult(envelope.Payload, envelope.Extension);
    }

    /// <summary>
    /// IEEE 754 80-bit extended-precision big-endian, big-enough subset to
    /// encode positive integer sample rates (8 kHz to 192 kHz). Matches
    /// <c>_aiff_extended_float</c> in <c>masquerade.py:679-693</c>.
    /// </summary>
    internal static byte[] EncodeExtendedFloat(uint value)
    {
        byte[] result = new byte[10];
        if (value == 0)
        {
            return result;
        }
        int exp = 31 - System.Numerics.BitOperations.LeadingZeroCount(value);
        ulong mantissa = (ulong)value << (63 - exp);
        ushort biasedExp = (ushort)(exp + 16383);

        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0, 2), biasedExp);
        BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(2, 8), mantissa);
        return result;
    }

    /// <summary>Inverse of <see cref="EncodeExtendedFloat"/>; positive integers only.</summary>
    internal static uint DecodeExtendedFloat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 10)
        {
            throw new ArgumentException("Extended float must be 10 bytes.", nameof(bytes));
        }
        if (bytes.IndexOfAnyExcept((byte)0) < 0)
        {
            return 0;
        }
        ushort biasedExp = BinaryPrimitives.ReadUInt16BigEndian(bytes[..2]);
        ulong mantissa = BinaryPrimitives.ReadUInt64BigEndian(bytes[2..10]);
        int exp = (biasedExp & 0x7FFF) - 16383;
        return (uint)(mantissa >> (63 - exp));
    }

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
            uint size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));

            byte[] body = new byte[size];
            int bodyRead = await ReadExactAsync(source, body, cancellationToken).ConfigureAwait(false);
            if (bodyRead < (int)size)
            {
                yield break;
            }
            yield return (tag, body);

            if ((size & 1) == 1)
            {
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
