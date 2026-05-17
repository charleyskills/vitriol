namespace Vitriol.Tests.Collections;

public sealed class EquatableArrayTests
{
    [Fact]
    public void Empty_arrays_are_equal()
    {
        EquatableArray<int> a = EquatableArray<int>.Empty;
        EquatableArray<int> b = default;

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Element_wise_equality()
    {
        EquatableArray<string> a = EquatableArray.Create("x", "y", "z");
        EquatableArray<string> b = EquatableArray.Create("x", "y", "z");
        EquatableArray<string> c = EquatableArray.Create("x", "y", "Z");

        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        (a == c).ShouldBeFalse();
    }

    [Fact]
    public void Different_lengths_are_unequal()
    {
        EquatableArray<int> a = EquatableArray.Create(1, 2, 3);
        EquatableArray<int> b = EquatableArray.Create(1, 2);

        (a == b).ShouldBeFalse();
    }

    [Fact]
    public void Indexer_and_count_work()
    {
        EquatableArray<int> a = EquatableArray.Create(10, 20, 30);

        a.Count.ShouldBe(3);
        a.Length.ShouldBe(3);
        a[0].ShouldBe(10);
        a[2].ShouldBe(30);
    }

    [Fact]
    public void Implicit_from_ImmutableArray()
    {
        EquatableArray<int> a = System.Collections.Immutable.ImmutableArray.Create(1, 2, 3);

        a.Length.ShouldBe(3);
    }
}
