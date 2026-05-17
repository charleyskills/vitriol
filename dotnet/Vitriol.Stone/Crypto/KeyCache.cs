using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Vitriol.Stone.Crypto;

/// <summary>
/// Module-level cache keyed by <c>SHA-256(password)</c> so we don't keep raw
/// passwords in memory. Mirrors <c>_KEY_CACHE</c> in
/// <c>app/format_handlers/_stone_crypto.py:42</c>.
/// </summary>
internal sealed class KeyCache
{
    // 32-byte SHA-256 fingerprint → derived 32-byte AES-256 key.
    private readonly ConcurrentDictionary<FingerprintKey, byte[]> _entries = new();

    public bool TryGet(ReadOnlySpan<byte> password, out byte[]? key)
    {
        FingerprintKey fp = Fingerprint(password);
        if (_entries.TryGetValue(fp, out byte[]? cached))
        {
            key = cached;
            return true;
        }
        key = null;
        return false;
    }

    public void Store(ReadOnlySpan<byte> password, byte[] key)
    {
        FingerprintKey fp = Fingerprint(password);
        _entries[fp] = key;
    }

    public void Clear() => _entries.Clear();

    private static FingerprintKey Fingerprint(ReadOnlySpan<byte> password)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(password, digest);
        return new FingerprintKey(digest);
    }

    private readonly struct FingerprintKey : IEquatable<FingerprintKey>
    {
        private readonly ulong _a;
        private readonly ulong _b;
        private readonly ulong _c;
        private readonly ulong _d;

        public FingerprintKey(ReadOnlySpan<byte> digest)
        {
            _a = BitConverter.ToUInt64(digest[..8]);
            _b = BitConverter.ToUInt64(digest[8..16]);
            _c = BitConverter.ToUInt64(digest[16..24]);
            _d = BitConverter.ToUInt64(digest[24..32]);
        }

        public bool Equals(FingerprintKey other) =>
            _a == other._a && _b == other._b && _c == other._c && _d == other._d;

        public override bool Equals(object? obj) => obj is FingerprintKey o && Equals(o);

        public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);
    }
}
