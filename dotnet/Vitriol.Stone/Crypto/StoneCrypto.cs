using System.Security.Cryptography;
using System.Text;

namespace Vitriol.Stone.Crypto;

/// <summary>
/// PBKDF2-HMAC-SHA256 key derivation + SIV-style deterministic IV for Stone
/// v3. Byte-for-byte match with <c>app/format_handlers/_stone_crypto.py</c>.
///
/// <para><b>No-oracle property.</b> Wrong password yields garbage bytes, not an
/// error signal. Higher-level callers must treat the returned bytes as
/// authoritative regardless of correctness. This is intentional; AEAD modes
/// (AES-GCM) would provide a wrong-key oracle which the design forbids.</para>
/// </summary>
public static class StoneCrypto
{
    public const int PbkdfIterations = 200_000;

    public static ReadOnlySpan<byte> PbkdfSalt =>
        Encoding.ASCII.GetBytes("transmute-stone-v3");

    public const int KeyLength = 32;

    public const int IvLength = 16;

    public const int SaltFieldLength = 4;

    private static readonly KeyCache Cache = new();

    /// <summary>
    /// Derives a 32-byte AES-256 key via PBKDF2-HMAC-SHA256 with 200,000
    /// iterations and the fixed salt <c>"transmute-stone-v3"</c>. Empty
    /// password is allowed and produces a deterministic default key shared
    /// across all installations.
    /// </summary>
    public static byte[] DeriveKey(ReadOnlyMemory<byte> password)
    {
        if (Cache.TryGet(password.Span, out byte[]? cached))
        {
            return cached!;
        }

        // Rfc2898DeriveBytes matches PBKDF2-HMAC-SHA256 byte-for-byte.
        byte[] saltCopy = PbkdfSalt.ToArray();
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            password.Span,
            saltCopy,
            PbkdfIterations,
            HashAlgorithmName.SHA256,
            KeyLength);

        Cache.Store(password.Span, key);
        return key;
    }

    /// <summary>
    /// SIV-style deterministic IV: <c>HMAC-SHA256(key, plaintext)[:16]</c>.
    /// Same (key, plaintext) → same IV. Different plaintexts under the same
    /// key get different IVs (no two-time-pad reuse).
    /// </summary>
    public static byte[] DeriveIv(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext)
    {
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(key, plaintext, hash);
        return hash[..IvLength].ToArray();
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with AES-256-CTR under a key
    /// derived from <paramref name="password"/>. Returns (iv, ciphertext).
    /// </summary>
    public static (byte[] Iv, byte[] Ciphertext) Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlyMemory<byte> password)
    {
        byte[] key = DeriveKey(password);
        byte[] iv = DeriveIv(key, plaintext);

        byte[] ciphertext = new byte[plaintext.Length];
        using AesCtrTransform ctr = new(key, iv);
        ctr.TransformBlock(plaintext, ciphertext);
        return (iv, ciphertext);
    }

    /// <summary>
    /// Decrypts under <paramref name="password"/>. <b>No authentication
    /// check</b>; wrong password yields garbage of the same length.
    /// </summary>
    public static byte[] Decrypt(
        ReadOnlySpan<byte> iv,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlyMemory<byte> password)
    {
        byte[] key = DeriveKey(password);
        byte[] plaintext = new byte[ciphertext.Length];
        using AesCtrTransform ctr = new(key, iv);
        ctr.TransformBlock(ciphertext, plaintext);
        return plaintext;
    }

    /// <summary>Forgets all cached derived keys.</summary>
    public static void ClearKeyCache() => Cache.Clear();
}
