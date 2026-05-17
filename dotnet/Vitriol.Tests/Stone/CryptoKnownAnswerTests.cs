using System.Security.Cryptography;
using System.Text;
using Vitriol.Stone.Crypto;

namespace Vitriol.Tests.Stone;

public sealed class CryptoKnownAnswerTests
{
    [Fact]
    public void Pbkdf2_matches_dotnet_built_in()
    {
        // Sanity check: StoneCrypto.DeriveKey must produce the exact bytes
        // Rfc2898DeriveBytes.Pbkdf2 produces with the same parameters.
        byte[] password = "open sesame"u8.ToArray();
        byte[] salt = Encoding.ASCII.GetBytes("transmute-stone-v3");

        byte[] reference = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 200_000, HashAlgorithmName.SHA256, 32);

        byte[] derived = StoneCrypto.DeriveKey(password);

        derived.ShouldBe(reference);
        derived.Length.ShouldBe(32);
    }

    [Fact]
    public void Empty_password_produces_deterministic_default_key()
    {
        byte[] a = StoneCrypto.DeriveKey(ReadOnlyMemory<byte>.Empty);
        byte[] b = StoneCrypto.DeriveKey(ReadOnlyMemory<byte>.Empty);
        a.ShouldBe(b);
    }

    [Fact]
    public void Different_passwords_produce_different_keys()
    {
        byte[] a = StoneCrypto.DeriveKey("alpha"u8.ToArray());
        byte[] b = StoneCrypto.DeriveKey("bravo"u8.ToArray());
        a.ShouldNotBe(b);
    }

    [Fact]
    public void Deterministic_iv_is_hmac_sha256_truncated_to_16()
    {
        byte[] key = new byte[32];
        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)i;
        }
        byte[] plaintext = "the quick brown fox jumps over the lazy dog"u8.ToArray();

        byte[] iv = StoneCrypto.DeriveIv(key, plaintext);

        Span<byte> reference = stackalloc byte[32];
        HMACSHA256.HashData(key, plaintext, reference);
        iv.AsSpan().SequenceEqual(reference[..16]).ShouldBeTrue();
    }

    [Fact]
    public void Encrypt_then_decrypt_recovers_plaintext()
    {
        byte[] password = "chopin"u8.ToArray();
        byte[] plaintext = "secret payload bytes for round-trip"u8.ToArray();

        (byte[] iv, byte[] ciphertext) = StoneCrypto.Encrypt(plaintext, password);
        byte[] recovered = StoneCrypto.Decrypt(iv, ciphertext, password);

        recovered.ShouldBe(plaintext);
        ciphertext.Length.ShouldBe(plaintext.Length);
        ciphertext.ShouldNotBe(plaintext);
    }

    [Fact]
    public void Wrong_password_yields_garbage_not_error()
    {
        byte[] password = "right"u8.ToArray();
        byte[] wrong = "wrong"u8.ToArray();
        byte[] plaintext = "secret"u8.ToArray();

        (byte[] iv, byte[] ciphertext) = StoneCrypto.Encrypt(plaintext, password);
        byte[] garbage = StoneCrypto.Decrypt(iv, ciphertext, wrong);

        garbage.Length.ShouldBe(plaintext.Length);
        garbage.ShouldNotBe(plaintext);
    }

    [Fact]
    public void Same_plaintext_and_password_produce_identical_ciphertext_deterministic_iv()
    {
        byte[] password = "stable"u8.ToArray();
        byte[] plaintext = "carrier-determining bytes"u8.ToArray();

        (byte[] iv1, byte[] ct1) = StoneCrypto.Encrypt(plaintext, password);
        (byte[] iv2, byte[] ct2) = StoneCrypto.Encrypt(plaintext, password);

        iv1.ShouldBe(iv2);
        ct1.ShouldBe(ct2);
    }

    [Fact]
    public void Aes_ctr_matches_dotnet_aes_in_ctr_via_ecb_construction()
    {
        // Compare AesCtrTransform output against a hand-rolled CTR built
        // from AES-ECB on the counter block. Same keystream → same output.
        byte[] key = new byte[32];
        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(i * 7 + 3);
        }
        byte[] iv = Enumerable.Range(0, 16).Select(i => (byte)(i * 11)).ToArray();
        byte[] plaintext = Enumerable.Range(0, 70).Select(i => (byte)i).ToArray();

        byte[] mine = new byte[plaintext.Length];
        using (AesCtrTransform ctr = new(key, iv))
        {
            ctr.TransformBlock(plaintext, mine);
        }

        byte[] reference = HandRolledCtr(key, iv, plaintext);
        mine.ShouldBe(reference);
    }

    [Fact]
    public void Aes_ctr_chunked_matches_one_shot()
    {
        byte[] key = new byte[32];
        byte[] iv = new byte[16];
        Random rng = new(42);
        rng.NextBytes(key);
        rng.NextBytes(iv);

        byte[] plaintext = new byte[1000];
        rng.NextBytes(plaintext);

        byte[] oneShot = new byte[plaintext.Length];
        using (AesCtrTransform a = new(key, iv))
        {
            a.TransformBlock(plaintext, oneShot);
        }

        byte[] chunked = new byte[plaintext.Length];
        using (AesCtrTransform b = new(key, iv))
        {
            int[] chunkSizes = { 1, 7, 16, 17, 31, 100, 200, 628 };
            int offset = 0;
            foreach (int n in chunkSizes)
            {
                int span = Math.Min(n, plaintext.Length - offset);
                b.TransformChunk(plaintext.AsSpan(offset, span), chunked.AsSpan(offset, span));
                offset += span;
            }
        }

        chunked.ShouldBe(oneShot);
    }

    private static byte[] HandRolledCtr(byte[] key, byte[] iv, byte[] plaintext)
    {
        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        using ICryptoTransform ecb = aes.CreateEncryptor();

        byte[] counter = (byte[])iv.Clone();
        byte[] result = new byte[plaintext.Length];
        byte[] keystream = new byte[16];

        for (int offset = 0; offset < plaintext.Length;)
        {
            byte[] counterCopy = (byte[])counter.Clone();
            ecb.TransformBlock(counterCopy, 0, 16, keystream, 0);
            int n = Math.Min(16, plaintext.Length - offset);
            for (int i = 0; i < n; i++)
            {
                result[offset + i] = (byte)(plaintext[offset + i] ^ keystream[i]);
            }
            offset += n;
            // big-endian +1
            for (int i = counter.Length - 1; i >= 0; i--)
            {
                if (++counter[i] != 0)
                {
                    break;
                }
            }
        }
        return result;
    }
}
