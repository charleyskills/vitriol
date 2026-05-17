namespace Vitriol.Tests.Ir;

public sealed class IrEqualityTests
{
    [Fact]
    public void Run_is_value_equal()
    {
        Run a = new("hello", RunStyle.Bold, "https://example.com");
        Run b = new("hello", RunStyle.Bold, "https://example.com");
        Run c = new("hello", RunStyle.Italic, "https://example.com");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldNotBe(c);
    }

    [Fact]
    public void Run_style_flags_compose()
    {
        Run r = new("x", RunStyle.Bold | RunStyle.Italic);

        r.Bold.ShouldBeTrue();
        r.Italic.ShouldBeTrue();
        r.Underline.ShouldBeFalse();
        r.Code.ShouldBeFalse();
    }

    [Fact]
    public void Heading_is_value_equal_with_nested_runs()
    {
        Heading a = new(1, EquatableArray.Create(new Run("Title")));
        Heading b = new(1, EquatableArray.Create(new Run("Title")));
        Heading c = new(2, EquatableArray.Create(new Run("Title")));
        Heading d = new(1, EquatableArray.Create(new Run("Other")));

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldNotBe(c);
        a.ShouldNotBe(d);
    }

    [Fact]
    public void Paragraph_is_value_equal()
    {
        Paragraph a = new(EquatableArray.Create(new Run("a"), new Run("b")));
        Paragraph b = new(EquatableArray.Create(new Run("a"), new Run("b")));

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ListBlock_is_value_equal()
    {
        EquatableArray<EquatableArray<Block>> items = EquatableArray.Create(
            EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("first")))),
            EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("second")))));

        ListBlock a = new(Ordered: true, items);
        ListBlock b = new(Ordered: true, items);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void TableBlock_is_value_equal_with_nested_rows()
    {
        EquatableArray<Block> cell11 = EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(new Run("r1c1"))));
        EquatableArray<Block> cell12 = EquatableArray.Create<Block>(
            new Paragraph(EquatableArray.Create(new Run("r1c2"))));
        EquatableArray<EquatableArray<Block>> row1 = EquatableArray.Create(cell11, cell12);
        EquatableArray<EquatableArray<EquatableArray<Block>>> rows = EquatableArray.Create(row1);

        TableBlock a = new(rows);
        TableBlock b = new(rows);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ImageBlock_compares_data_bytes_value_wise()
    {
        ReadOnlyMemory<byte> data1 = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        ReadOnlyMemory<byte> data2 = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        ReadOnlyMemory<byte> data3 = new byte[] { 0x00, 0x00 };

        ImageBlock a = new(data1, "image/png", "alt");
        ImageBlock b = new(data2, "image/png", "alt");
        ImageBlock c = new(data3, "image/png", "alt");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldNotBe(c);
    }

    [Fact]
    public void VitriolOrigin_compares_bytes_value_wise()
    {
        ReadOnlyMemory<byte> data1 = new byte[] { 1, 2, 3 };
        ReadOnlyMemory<byte> data2 = new byte[] { 1, 2, 3 };

        VitriolOrigin a = new(data1, ".png");
        VitriolOrigin b = new(data2, ".png");
        VitriolOrigin c = new(data1, ".jpg");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldNotBe(c);
    }

    [Fact]
    public void TextDoc_with_origin_is_value_equal()
    {
        ReadOnlyMemory<byte> bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        VitriolOrigin origin = new(bytes, ".png");

        DocumentMetadata md1 = DocumentMetadata.Empty.WithOrigin(origin);
        DocumentMetadata md2 = DocumentMetadata.Empty.WithOrigin(new VitriolOrigin(bytes, ".png"));

        TextDoc a = new(EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("x")))), md1);
        TextDoc b = new(EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("x")))), md2);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Cell_holds_typed_value()
    {
        Cell d = new(123.45m);
        Cell i = new(42);
        Cell s = new("hello");
        Cell n = new();

        d.Value.ShouldBe(123.45m);
        i.Value.ShouldBe(42);
        s.Value.ShouldBe("hello");
        n.Value.ShouldBeNull();
    }

    [Fact]
    public void Sheet_and_Tabular_are_value_equal()
    {
        Sheet a = new("S1", EquatableArray.Create(
            EquatableArray.Create(new Cell(1), new Cell(2))));
        Sheet b = new("S1", EquatableArray.Create(
            EquatableArray.Create(new Cell(1), new Cell(2))));

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());

        Tabular ta = new(EquatableArray.Create(a));
        Tabular tb = new(EquatableArray.Create(b));

        ta.ShouldBe(tb);
        ta.GetHashCode().ShouldBe(tb.GetHashCode());
    }

    [Fact]
    public void BinaryDoc_compares_bytes_value_wise()
    {
        byte[] payload = { 1, 2, 3, 4 };
        BinaryDoc a = new(payload, "application/octet-stream");
        BinaryDoc b = new((byte[])payload.Clone(), "application/octet-stream");
        BinaryDoc c = new(new byte[] { 1, 2, 3 }, "application/octet-stream");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldNotBe(c);
    }

    [Fact]
    public void Block_subtypes_dispatch_polymorphic_equality()
    {
        Block h = new Heading(1, EquatableArray.Create(new Run("x")));
        Block p = new Paragraph(EquatableArray.Create(new Run("x")));

        // Same shape but different concrete type — must NOT be equal.
        h.ShouldNotBe(p);
    }
}
