using System.Collections;
using System.Collections.Immutable;

namespace Vitriol.Core.Collections;

/// <summary>
/// Value-equal wrapper around <see cref="ImmutableDictionary{TKey,TValue}"/> with
/// order-insensitive equality on entries. Used by <see cref="Vitriol.Core.Ir.DocumentMetadata.Extras"/>
/// so handler-stashed metadata participates in IR record equality.
/// </summary>
public readonly struct EquatableDictionary<TKey, TValue>
    : IEquatable<EquatableDictionary<TKey, TValue>>, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly ImmutableDictionary<TKey, TValue> _dict;

    public EquatableDictionary(ImmutableDictionary<TKey, TValue> dict) => _dict = dict;

    public static EquatableDictionary<TKey, TValue> Empty { get; } =
        new(ImmutableDictionary<TKey, TValue>.Empty);

    private ImmutableDictionary<TKey, TValue> Inner =>
        _dict ?? ImmutableDictionary<TKey, TValue>.Empty;

    public TValue this[TKey key] => Inner[key];

    public IEnumerable<TKey> Keys => Inner.Keys;

    public IEnumerable<TValue> Values => Inner.Values;

    public int Count => Inner.Count;

    public bool ContainsKey(TKey key) => Inner.ContainsKey(key);

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (Inner.TryGetValue(key, out TValue? v))
        {
            value = v;
            return true;
        }
        value = default!;
        return false;
    }

    public bool Equals(EquatableDictionary<TKey, TValue> other)
    {
        ImmutableDictionary<TKey, TValue> a = Inner;
        ImmutableDictionary<TKey, TValue> b = other.Inner;

        if (a.Count != b.Count)
        {
            return false;
        }

        EqualityComparer<TValue> valueComparer = EqualityComparer<TValue>.Default;
        foreach (KeyValuePair<TKey, TValue> kv in a)
        {
            if (!b.TryGetValue(kv.Key, out TValue? bValue) || !valueComparer.Equals(kv.Value, bValue))
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj) =>
        obj is EquatableDictionary<TKey, TValue> other && Equals(other);

    public override int GetHashCode()
    {
        // Order-independent hash: XOR over (key, value) hash pairs so equal dicts
        // with different insertion order hash the same.
        int hash = 0;
        foreach (KeyValuePair<TKey, TValue> kv in Inner)
        {
            int kh = kv.Key.GetHashCode();
            int vh = kv.Value is null ? 0 : kv.Value.GetHashCode();
            hash ^= HashCode.Combine(kh, vh);
        }
        return hash;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Inner).GetEnumerator();

    public static implicit operator EquatableDictionary<TKey, TValue>(
        ImmutableDictionary<TKey, TValue> dict) => new(dict);

    public static bool operator ==(EquatableDictionary<TKey, TValue> left,
        EquatableDictionary<TKey, TValue> right) => left.Equals(right);

    public static bool operator !=(EquatableDictionary<TKey, TValue> left,
        EquatableDictionary<TKey, TValue> right) => !left.Equals(right);
}
