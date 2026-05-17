using System.Security.Cryptography;

namespace Vitriol.Stone.Crypto;

/// <summary>
/// AES-256-CTR via the standard ECB-on-counter-block construction. .NET 10
/// has no first-class AES-CTR mode, so this composes <see cref="Aes"/> in ECB
/// mode operating on an incrementing 128-bit big-endian counter block, then
/// XORs the keystream against the input. Matches OpenSSL/cryptography
/// AES-256-CTR byte-for-byte.
///
/// <para>The counter starts at the supplied 16-byte IV and increments
/// big-endian per 16-byte block. CTR mode is naturally streamable; this class
/// exposes both one-shot <see cref="TransformBlock"/> and chunked
/// <see cref="TransformChunk"/> APIs.</para>
/// </summary>
public sealed class AesCtrTransform : IDisposable
{
    private const int BlockSize = 16;

    private readonly Aes _aes;
    private readonly ICryptoTransform _ecb;
    private readonly byte[] _counter = new byte[BlockSize];
    private readonly byte[] _keystreamBlock = new byte[BlockSize];
    private int _keystreamConsumed = BlockSize; // forces refill on first use

    public AesCtrTransform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(key.Length, 32, nameof(key));
        ArgumentOutOfRangeException.ThrowIfNotEqual(iv.Length, BlockSize, nameof(iv));

        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _aes.KeySize = 256;
        _aes.Key = key.ToArray();
        _ecb = _aes.CreateEncryptor();
        iv.CopyTo(_counter);
    }

    /// <summary>One-shot transform; output length equals input length.</summary>
    public void TransformBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("Output buffer is shorter than input.", nameof(output));
        }
        TransformChunk(input, output[..input.Length]);
    }

    /// <summary>
    /// Chunked transform. Subsequent calls continue the keystream from where
    /// the previous call left off, so callers can stream multi-GB plaintexts
    /// in chunks without holding everything in memory.
    /// </summary>
    public void TransformChunk(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("Output buffer is shorter than input.", nameof(output));
        }

        int pos = 0;
        while (pos < input.Length)
        {
            if (_keystreamConsumed >= BlockSize)
            {
                RefillKeystream();
            }

            int available = BlockSize - _keystreamConsumed;
            int n = Math.Min(available, input.Length - pos);

            ReadOnlySpan<byte> src = input.Slice(pos, n);
            Span<byte> dst = output.Slice(pos, n);
            ReadOnlySpan<byte> ks = _keystreamBlock.AsSpan(_keystreamConsumed, n);

            for (int i = 0; i < n; i++)
            {
                dst[i] = (byte)(src[i] ^ ks[i]);
            }

            _keystreamConsumed += n;
            pos += n;
        }
    }

    private void RefillKeystream()
    {
        // Encrypt one counter block to produce the next 16 bytes of keystream.
        // ICryptoTransform.TransformBlock requires byte[] buffers (no Span overload).
        // The buffers are tiny so the allocation cost is negligible.
        byte[] counterIn = _counter.AsSpan().ToArray();
        byte[] keystream = new byte[BlockSize];
        int written = _ecb.TransformBlock(counterIn, 0, BlockSize, keystream, 0);
        if (written != BlockSize)
        {
            throw new CryptographicException(
                $"AES ECB transform returned {written} bytes; expected {BlockSize}.");
        }
        keystream.AsSpan().CopyTo(_keystreamBlock);
        _keystreamConsumed = 0;
        IncrementCounterBe(_counter);
    }

    private static void IncrementCounterBe(Span<byte> counter)
    {
        // Big-endian 128-bit increment, matching OpenSSL/cryptography CTR.
        for (int i = counter.Length - 1; i >= 0; i--)
        {
            byte b = (byte)(counter[i] + 1);
            counter[i] = b;
            if (b != 0)
            {
                return;
            }
        }
        // Wrapped 2^128 — vanishingly improbable in practice but well-defined.
    }

    public void Dispose()
    {
        _ecb.Dispose();
        _aes.Dispose();
    }
}
