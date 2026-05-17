using System.Text;
using Vitriol.Core.Pipeline;
using Vitriol.Formats.Doc;

namespace Vitriol.Tests.Formats.Doc;

public sealed class MarkdownHandlerTests
{
    private readonly MarkdownHandler _handler = new();

    [Fact]
    public void Reports_text_kind_and_md_extension()
    {
        _handler.Kind.ShouldBe(DocKind.Text);
        _handler.SupportedExtensions.ShouldContain(".md");
    }

    [Fact]
    public async Task Read_parses_atx_heading_and_paragraph()
    {
        string md = "# Hello\n\nA paragraph here.\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(md));

        TextDoc doc = (TextDoc)await _handler.ReadAsync(src, new ReadContext(".md"), default);
        doc.Blocks.Count.ShouldBe(2);
        Heading h = doc.Blocks[0].ShouldBeOfType<Heading>();
        h.Level.ShouldBe(1);
        h.Runs[0].Text.ShouldBe("Hello");

        Paragraph p = doc.Blocks[1].ShouldBeOfType<Paragraph>();
        p.Runs[0].Text.ShouldBe("A paragraph here.");
    }

    [Fact]
    public async Task Read_handles_bold_italic_code_inline_styles()
    {
        string md = "A **bold** and *italic* and `code` together.\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(md));

        TextDoc doc = (TextDoc)await _handler.ReadAsync(src, new ReadContext(".md"), default);
        Paragraph p = doc.Blocks[0].ShouldBeOfType<Paragraph>();

        bool sawBold = false, sawItalic = false, sawCode = false;
        foreach (Run r in p.Runs)
        {
            if (r.Bold) { sawBold = true; }
            if (r.Italic) { sawItalic = true; }
            if (r.Code) { sawCode = true; }
        }
        sawBold.ShouldBeTrue();
        sawItalic.ShouldBeTrue();
        sawCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Read_parses_unordered_list_with_items()
    {
        string md = "- one\n- two\n- three\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(md));

        TextDoc doc = (TextDoc)await _handler.ReadAsync(src, new ReadContext(".md"), default);
        ListBlock list = doc.Blocks[0].ShouldBeOfType<ListBlock>();
        list.Ordered.ShouldBeFalse();
        list.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Read_parses_fenced_code_block_with_language()
    {
        string md = "```python\nprint('hello')\n```\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(md));

        TextDoc doc = (TextDoc)await _handler.ReadAsync(src, new ReadContext(".md"), default);
        CodeBlock cb = doc.Blocks[0].ShouldBeOfType<CodeBlock>();
        cb.Language.ShouldBe("python");
        cb.Text.ShouldContain("print('hello')");
    }

    [Fact]
    public async Task Read_parses_horizontal_rule_and_blockquote()
    {
        string md = "---\n\n> a quote\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(md));

        TextDoc doc = (TextDoc)await _handler.ReadAsync(src, new ReadContext(".md"), default);
        doc.Blocks[0].ShouldBeOfType<HorizontalRule>();
        Blockquote bq = doc.Blocks[1].ShouldBeOfType<Blockquote>();
        bq.Blocks.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Read_parses_gfm_table()
    {
        string md = "| name | score |\n| --- | --- |\n| Alice | 42 |\n";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(md));

        TextDoc doc = (TextDoc)await _handler.ReadAsync(src, new ReadContext(".md"), default);
        TableBlock table = doc.Blocks[0].ShouldBeOfType<TableBlock>();
        table.Rows.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Write_emits_heading_then_paragraph_in_markdown()
    {
        TextDoc doc = new(EquatableArray.Create<Block>(
            new Heading(1, EquatableArray.Create(new Run("Hello"))),
            new Paragraph(EquatableArray.Create(new Run("A paragraph.")))));

        await using MemoryStream dst = new();
        await _handler.WriteAsync(doc, dst, new WriteContext(".md"), default);
        string md = Encoding.UTF8.GetString(dst.ToArray());

        md.ShouldContain("# Hello");
        md.ShouldContain("A paragraph.");
    }

    [Fact]
    public async Task Write_round_trips_a_simple_document_back_to_equivalent_ir()
    {
        TextDoc input = new(EquatableArray.Create<Block>(
            new Heading(2, EquatableArray.Create(new Run("Section"))),
            new Paragraph(EquatableArray.Create(new Run("Body text."))),
            new ListBlock(false, EquatableArray.Create(
                EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("alpha")))),
                EquatableArray.Create<Block>(new Paragraph(EquatableArray.Create(new Run("beta"))))))));

        await using MemoryStream dst = new();
        await _handler.WriteAsync(input, dst, new WriteContext(".md"), default);

        dst.Position = 0;
        TextDoc round = (TextDoc)await _handler.ReadAsync(dst, new ReadContext(".md"), default);
        round.Blocks.Count.ShouldBe(3);
        Heading h = round.Blocks[0].ShouldBeOfType<Heading>();
        h.Level.ShouldBe(2);
        h.Runs[0].Text.ShouldBe("Section");

        round.Blocks[1].ShouldBeOfType<Paragraph>().Runs[0].Text.ShouldBe("Body text.");
        round.Blocks[2].ShouldBeOfType<ListBlock>().Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Rejects_non_textdoc_input()
    {
        await using MemoryStream dst = new();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(Tabular.Empty, dst, new WriteContext(".md"), default));
    }
}
