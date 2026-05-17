using Vitriol.Core.Detection.Sniffers;

namespace Vitriol.Tests.Detection;

public sealed class MagicByteSnifferTests
{
    private readonly MagicByteSniffer _sniffer = new();

    [Fact]
    public void Detects_png_signature()
    {
        byte[] head = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        _sniffer.TrySniff(head, "anything").ShouldBe(".png");
    }

    [Fact]
    public void Detects_jpeg_signature()
    {
        byte[] head = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        _sniffer.TrySniff(head, "photo.dat").ShouldBe(".jpg");
    }

    [Fact]
    public void Detects_gif87a_and_gif89a()
    {
        _sniffer.TrySniff("GIF87a"u8.ToArray(), "x").ShouldBe(".gif");
        _sniffer.TrySniff("GIF89a"u8.ToArray(), "x").ShouldBe(".gif");
    }

    [Fact]
    public void Detects_bmp_signature()
    {
        _sniffer.TrySniff("BM\x00\x00"u8.ToArray(), "x").ShouldBe(".bmp");
    }

    [Fact]
    public void Detects_webp_signature()
    {
        byte[] head = new byte[16];
        "RIFF"u8.CopyTo(head.AsSpan(0));
        "WEBP"u8.CopyTo(head.AsSpan(8));
        _sniffer.TrySniff(head, "x").ShouldBe(".webp");
    }

    [Fact]
    public void Detects_tiff_little_endian()
    {
        byte[] head = { (byte)'I', (byte)'I', (byte)'*', 0x00 };
        _sniffer.TrySniff(head, "x").ShouldBe(".tiff");
    }

    [Fact]
    public void Detects_tiff_big_endian()
    {
        byte[] head = { (byte)'M', (byte)'M', 0x00, (byte)'*' };
        _sniffer.TrySniff(head, "x").ShouldBe(".tiff");
    }

    [Fact]
    public void Detects_ico_signature()
    {
        byte[] head = { 0x00, 0x00, 0x01, 0x00, 0x01, 0x00 };
        _sniffer.TrySniff(head, "x").ShouldBe(".ico");
    }

    [Fact]
    public void Detects_glb_signature()
    {
        _sniffer.TrySniff("glTF\x02\x00\x00\x00"u8.ToArray(), "x").ShouldBe(".glb");
    }

    [Fact]
    public void Detects_pdf_signature()
    {
        _sniffer.TrySniff("%PDF-1.7"u8.ToArray(), "x").ShouldBe(".pdf");
    }

    [Fact]
    public void Detects_rtf_signature()
    {
        _sniffer.TrySniff(@"{\rtf1"u8.ToArray(), "x").ShouldBe(".rtf");
    }

    [Fact]
    public void Detects_xml()
    {
        _sniffer.TrySniff("<?xml version=\"1.0\"?>"u8.ToArray(), "x").ShouldBe(".xml");
    }

    [Fact]
    public void Detects_svg_via_xml_lookahead()
    {
        byte[] head = "<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\">"u8.ToArray();
        _sniffer.TrySniff(head, "x").ShouldBe(".svg");
    }

    [Fact]
    public void Detects_svg_without_xml_declaration()
    {
        _sniffer.TrySniff("<svg width=\"10\">"u8.ToArray(), "x").ShouldBe(".svg");
    }

    [Fact]
    public void Detects_json()
    {
        _sniffer.TrySniff("{\"key\": 1}"u8.ToArray(), "x").ShouldBe(".json");
        _sniffer.TrySniff("[1, 2, 3]"u8.ToArray(), "x").ShouldBe(".json");
    }

    [Fact]
    public void Detects_gltf_via_json_asset_version_marker()
    {
        byte[] head = "{\"asset\": {\"version\": \"2.0\"}, \"scenes\": []}"u8.ToArray();
        _sniffer.TrySniff(head, "x").ShouldBe(".gltf");
    }

    [Fact]
    public void Detects_html_doctype()
    {
        _sniffer.TrySniff("<!DOCTYPE html>\n<html>"u8.ToArray(), "x").ShouldBe(".html");
    }

    [Fact]
    public void Detects_html_lowercase_tag()
    {
        _sniffer.TrySniff("<html lang=\"en\">"u8.ToArray(), "x").ShouldBe(".html");
    }

    [Fact]
    public void Returns_null_for_zip_signature_letting_zip_sniffer_take_over()
    {
        byte[] head = { (byte)'P', (byte)'K', 0x03, 0x04, 0x00 };
        _sniffer.TrySniff(head, "x").ShouldBeNull();
    }

    [Fact]
    public void Returns_null_for_unknown_content()
    {
        _sniffer.TrySniff(new byte[] { 0x55, 0xAA, 0xBE, 0xEF }, "x").ShouldBeNull();
    }

    [Fact]
    public void Returns_null_for_empty_head()
    {
        _sniffer.TrySniff(ReadOnlySpan<byte>.Empty, "x").ShouldBeNull();
    }

    [Fact]
    public void Stl_only_trusted_when_extension_agrees()
    {
        byte[] solidHead = "solid object".PadRight(80, ' ').Select(c => (byte)c).ToArray();
        _sniffer.TrySniff(solidHead, "model.stl").ShouldBe(".stl");
        _sniffer.TrySniff(solidHead, "ambiguous.txt").ShouldBeNull();
    }
}
