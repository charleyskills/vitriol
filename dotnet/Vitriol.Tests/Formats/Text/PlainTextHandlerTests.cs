using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;
using Vitriol.Formats.Text;

namespace Vitriol.Tests.Formats.Text;

public sealed class PlainTextHandlerTests
{
    private readonly PlainTextHandler _handler = new();

    [Fact]
    public async Task Read_then_write_round_trips_blank_line_separated_paragraphs()
    {
        string text = "Para one.\n\nPara two.\n\nPara three.";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(text));

        IDocument doc = await _handler.ReadAsync(src, new ReadContext(".txt"), default);
        TextDoc td = doc.ShouldBeOfType<TextDoc>();
        td.Blocks.Count.ShouldBe(3);

        await using MemoryStream dst = new();
        await _handler.WriteAsync(td, dst, new WriteContext(".txt"), default);

        Encoding.UTF8.GetString(dst.ToArray()).ShouldBe(text);
    }

    [Fact]
    public async Task Stream_convert_preserves_bytes_exactly()
    {
        string text = "  whitespace-sensitive\nand\r\nnewlines   ";
        await using MemoryStream src = new(Encoding.UTF8.GetBytes(text));
        await using MemoryStream dst = new();

        await _handler.StreamConvertAsync(src, dst, ".txt", ".log", progress: null, default);

        Encoding.UTF8.GetString(dst.ToArray()).ShouldBe(text);
    }

    [Fact]
    public void Can_stream_returns_true_for_supported_pairs()
    {
        _handler.CanStream(".txt", ".txt").ShouldBeTrue();
        _handler.CanStream(".txt", ".log").ShouldBeTrue();
        _handler.CanStream(".html", ".xml").ShouldBeTrue();
    }

    [Fact]
    public void Can_stream_returns_false_for_unsupported_extension()
    {
        _handler.CanStream(".txt", ".pdf").ShouldBeFalse();
        _handler.CanStream(".pdf", ".txt").ShouldBeFalse();
    }

    [Fact]
    public async Task Write_rejects_non_text_document()
    {
        Tabular tabular = Tabular.Empty;
        await using MemoryStream dst = new();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(tabular, dst, new WriteContext(".txt"), default));
    }

    [Fact]
    public async Task Write_accepts_binary_doc_and_decodes_as_text()
    {
        BinaryDoc binary = new(Encoding.UTF8.GetBytes("hello\n\nworld"), "text/plain");

        await using MemoryStream dst = new();
        await _handler.WriteAsync(binary, dst, new WriteContext(".txt"), default);

        Encoding.UTF8.GetString(dst.ToArray()).ShouldBe("hello\n\nworld");
    }

    [Fact]
    public async Task Same_handler_pass_through_via_router_preserves_bytes_exactly()
    {
        // Whitespace-sensitive content that the IR adapter would mangle (trimming,
        // paragraph join). Stream pass-through must keep it byte-identical.
        string body = "line 1\n   indented\n\n\n\n   trailing blanks\n";

        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddVitriolText();
        ServiceProvider sp = services.BuildServiceProvider();

        IConversionRouter router = sp.GetRequiredService<IConversionRouter>();

        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"vitriol-text-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string srcPath = System.IO.Path.Combine(dir, "in.txt");
            string dstPath = System.IO.Path.Combine(dir, "out.log");
            await File.WriteAllBytesAsync(srcPath, Encoding.UTF8.GetBytes(body));

            ConversionJob job = new(srcPath, dstPath, ".txt", ".log");
            await router.ConvertAsync(job, progress: null, default);

            (await File.ReadAllBytesAsync(dstPath)).ShouldBe(Encoding.UTF8.GetBytes(body));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
