using System.Collections;
using System.Collections.Immutable;

namespace Vitriol.Core.Collections;

/// <summary>
/// Value-equal wrapper around <see cref="ImmutableArray{T}"/>.
/// Records that hold collections need this — <c>ImmutableArray&lt;T&gt;</c>'s
/// default <c>Equals</c> is reference-based, which breaks deep value equality
/// for the IR records (<see cref="Vitriol.Core.Ir.TextDoc"/>, etc.).
/// </summary>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    public static EquatableArray<T> Empty { get; } = new(ImmutableArray<T>.Empty);

    public T this[int index] => _array[index];

    public int Count => _array.IsDefault ? 0 : _array.Length;

    public int Length => Count;

    public bool IsDefaultOrEmpty => _array.IsDefaultOrEmpty;

    public ImmutableArray<T> AsImmutableArray() => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    public ReadOnlySpan<T> AsSpan() => AsImmutableArray().AsSpan();

    public bool Equals(EquatableArray<T> other)
    {
        if (_array.IsDefaultOrEmpty)
        {
            return other._array.IsDefaultOrEmpty;
        }

        if (other._array.IsDefaultOrEmpty)
        {
            return false;
        }

        return _array.AsSpan().SequenceEqual(other._array.AsSpan(), EqualityComparer<T>.Default);
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array.IsDefaultOrEmpty)
        {
            return 0;
        }

        var hash = default(HashCode);
        foreach (T item in _array)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() => AsImmutableArray().GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)AsImmutableArray()).GetEnumerator();

    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

public static class EquatableArray
{
    public static EquatableArray<T> Create<T>(params T[] items) =>
        new(ImmutableArray.Create(items));

    public static EquatableArray<T> CreateRange<T>(IEnumerable<T> items) =>
        new(items as ImmutableArray<T>? ?? ImmutableArray.CreateRange(items));

    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items) =>
        new(ImmutableArray.CreateRange(items));

    public static EquatableArray<T> ToEquatableArray<T>(this ImmutableArray<T> array) =>
        new(array);
}
