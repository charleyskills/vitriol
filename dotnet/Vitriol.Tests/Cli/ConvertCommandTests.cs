using System.Text;
using Vitriol.Cli;

namespace Vitriol.Tests.Cli;

public sealed class ConvertCommandTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    [Fact]
    public void Parse_minimal_positional_args()
    {
        (ConvertCommand.Args? args, string? error) = ConvertCommand.Parse(new[] { "in.txt", "out.txt" });

        error.ShouldBeNull();
        args.ShouldNotBeNull();
        args!.Source.ShouldBe("in.txt");
        args.Destination.ShouldBe("out.txt");
        args.Verify.ShouldBeFalse();
        args.Masquerade.ShouldBeFalse();
        args.Compiler.ShouldBeFalse();
        args.Password.ShouldBeNull();
    }

    [Fact]
    public void Parse_short_flag_aliases()
    {
        (ConvertCommand.Args? args, string? err) = ConvertCommand.Parse(
            new[] { "-v", "-m", "-p", "chopin", "in", "out" });

        err.ShouldBeNull();
        args!.Verify.ShouldBeTrue();
        args.Masquerade.ShouldBeTrue();
        args.Password.ShouldBe("chopin");
    }

    [Fact]
    public void Parse_all_long_flags()
    {
        (ConvertCommand.Args? args, string? err) = ConvertCommand.Parse(new[]
        {
            "--verify", "--masquerade", "--compiler", "--verbose",
            "--password", "secret",
            "src.zip", "dst.png",
        });

        err.ShouldBeNull();
        args!.Verify.ShouldBeTrue();
        args.Masquerade.ShouldBeTrue();
        args.Compiler.ShouldBeTrue();
        args.Verbose.ShouldBeTrue();
        args.Password.ShouldBe("secret");
        args.Source.ShouldBe("src.zip");
        args.Destination.ShouldBe("dst.png");
    }

    [Fact]
    public void Parse_missing_destination_returns_error()
    {
        (ConvertCommand.Args? args, string? err) = ConvertCommand.Parse(new[] { "only-one" });
        args.ShouldBeNull();
        err.ShouldNotBeNull();
        err.ShouldContain("requires <src> and <dst>");
    }

    [Fact]
    public void Parse_unknown_option_returns_error()
    {
        (ConvertCommand.Args? args, string? err) = ConvertCommand.Parse(
            new[] { "--bogus", "in", "out" });
        args.ShouldBeNull();
        err.ShouldNotBeNull();
        err.ShouldContain("unknown option");
    }

    [Fact]
    public void Parse_password_without_value_returns_error()
    {
        (ConvertCommand.Args? args, string? err) = ConvertCommand.Parse(
            new[] { "in", "out", "--password" });
        args.ShouldBeNull();
        err.ShouldNotBeNull();
        err.ShouldContain("--password requires a value");
    }

    [Fact]
    public void Parse_extra_positional_returns_error()
    {
        (ConvertCommand.Args? args, string? err) = ConvertCommand.Parse(
            new[] { "a", "b", "c" });
        args.ShouldBeNull();
        err.ShouldNotBeNull();
        err.ShouldContain("unexpected positional");
    }

    [Fact]
    public async Task RunAsync_source_not_found_returns_usage_exit_code()
    {
        StringBuilder sb = new();
        using StringWriter sw = new(sb);

        int code = await ConvertCommand.RunAsync(
            new[] { "/tmp/nope-vitriol-test.txt", "/tmp/whatever.txt" },
            sw,
            default);

        code.ShouldBe(ExitCodes.Usage);
        sb.ToString().ShouldContain("source not found");
    }

    [Fact]
    public async Task RunAsync_unsupported_pair_returns_unsupported_exit_code()
    {
        string srcPath = WriteTemp(new byte[] { 1, 2, 3 }, ".unknown1");
        string dstPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"vitriol-out-{Guid.NewGuid():N}.unknown2");
        _tempPaths.Add(dstPath);

        StringBuilder sb = new();
        using StringWriter sw = new(sb);

        int code = await ConvertCommand.RunAsync(
            new[] { srcPath, dstPath },
            sw,
            default);

        code.ShouldBe(ExitCodes.Unsupported);
    }

    [Fact]
    public async Task RunAsync_txt_to_log_stream_pass_through_succeeds()
    {
        string body = "  whitespace\nmust be preserved  \n";
        string srcPath = WriteTemp(Encoding.UTF8.GetBytes(body), ".txt");
        string dstPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"vitriol-out-{Guid.NewGuid():N}.log");
        _tempPaths.Add(dstPath);

        StringBuilder sb = new();
        using StringWriter sw = new(sb);

        int code = await ConvertCommand.RunAsync(
            new[] { srcPath, dstPath },
            sw,
            default);

        code.ShouldBe(ExitCodes.Success);
        File.Exists(dstPath).ShouldBeTrue();
        (await File.ReadAllBytesAsync(dstPath)).ShouldBe(Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    public async Task RunAsync_stone_round_trip_via_zip_with_verify_returns_byte_equal()
    {
        // .bin → .zip via Philosopher's Stone, then --verify forces a forward+reverse
        // SHA-256 compare. ZipStoneHost stores the source verbatim, so this is a
        // byte-equal round-trip.
        byte[] payload = new byte[512];
        new Random(7).NextBytes(payload);
        string srcPath = WriteTemp(payload, ".bin");
        string dstPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"vitriol-out-{Guid.NewGuid():N}.zip");
        _tempPaths.Add(dstPath);

        StringBuilder sb = new();
        using StringWriter sw = new(sb);

        int code = await ConvertCommand.RunAsync(
            new[] { srcPath, dstPath, "--masquerade", "--verify" },
            sw,
            default);

        code.ShouldBe(ExitCodes.Success);
        sb.ToString().ShouldContain("verified");
        File.Exists(dstPath).ShouldBeTrue();
    }

    private string WriteTemp(byte[] payload, string ext)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"vitriol-cli-{Guid.NewGuid():N}{ext}");
        File.WriteAllBytes(path, payload);
        _tempPaths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string p in _tempPaths)
        {
            try { File.Delete(p); } catch (IOException) { }
        }
    }
}
