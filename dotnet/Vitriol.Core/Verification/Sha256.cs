using System.Security.Cryptography;

namespace Vitriol.Core.Verification;

/// <summary>
/// SHA-256 helpers using <see cref="IncrementalHash"/> so multi-GB files
/// stream through without ever holding the full payload in memory. Mirrors
/// <c>_sha256_file</c> at <c>app/core/conversion_queue.py:233</c>.
/// </summary>
public static class Sha256
{
    public const int DigestLength = 32;

    private const int ChunkSize = 64 * 1024;

    /// <summary>
    /// Returns the SHA-256 digest of the file at <paramref name="path"/> as a
    /// 64-character lowercase hex string.
    /// </summary>
    public static async ValueTask<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[] digest = await HashFileBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(digest);
    }

    public static async ValueTask<byte[]> HashFileBytesAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[ChunkSize];

        await using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: ChunkSize, useAsync: true);
        while (true)
        {
            int read = await fs.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            hasher.AppendData(buffer, 0, read);
        }
        return hasher.GetHashAndReset();
    }

    public static byte[] HashBytes(ReadOnlySpan<byte> data)
    {
        byte[] digest = new byte[DigestLength];
        SHA256.HashData(data, digest);
        return digest;
    }
}
