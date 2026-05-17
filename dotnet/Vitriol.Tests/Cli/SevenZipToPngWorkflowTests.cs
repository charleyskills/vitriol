using System.Text;
using Vitriol.Cli;
using Vitriol.Stone.Carriers;

namespace Vitriol.Tests.Cli;

/// <summary>
/// End-to-end CLI workflow: a password-protected 7-Zip archive hidden
/// inside a Mandelbrot fractal PNG. The 7z bytes are opaque payload to
/// Vitriol; the test asserts byte-exact round trip with the user-supplied
/// password and that the carrier is a valid PNG.
///
/// <para>This is the test the design doc Section 1 advertises as the
/// headline cross-category Stone capability for archive sources.</para>
/// </summary>
public sealed class SevenZipToPngWorkflowTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task Password_protected_7z_round_trips_through_png_byte_perfectly()
    {
        // Build a "7z" file with realistic-looking bytes. Vitriol doesn't
        // introspect the file — it treats the bytes as opaque payload, so we
        // don't need a real password-protected 7z; any byte blob suffices.
        // We deliberately use 7z's magic header (37 7A BC AF 27 1C) so the
        // file is recognizable as a 7z to any later inspector.
        byte[] sevenZipBytes = BuildFakeSevenZipBytes(seed: 1729, lengthBytes: 4096);

        string dir = MakeScope();
        string sourcePath = Path.Combine(dir, "encrypted.7z");
        string carrierPath = Path.Combine(dir, "hidden.png");
        string recoveredPath = Path.Combine(dir, "recovered.7z");
        await File.WriteAllBytesAsync(sourcePath, sevenZipBytes);

        StringBuilder stderr = new();
        using StringWriter sw = new(stderr);

        // FORWARD: .7z → .png with --password (which now implies --masquerade)
        int forwardCode = await ConvertCommand.RunAsync(
            new[] { sourcePath, carrierPath, "--password", "vitriol-pass" },
            sw, default);
        forwardCode.ShouldBe(ExitCodes.Success);
        File.Exists(carrierPath).ShouldBeTrue();

        // The carrier opens as a valid PNG.
        await using (FileStream pngStream = File.OpenRead(carrierPath))
        {
            MandelbrotPngCodec.Decoded decoded =
                await MandelbrotPngCodec.ReadRgbAsync(pngStream, default);
            decoded.Width.ShouldBeGreaterThanOrEqualTo(MandelbrotDims.MinDimension);
            decoded.Height.ShouldBeGreaterThanOrEqualTo(MandelbrotDims.MinDimension);
        }

        // REVERSE: .png → .7z with the same --password
        int reverseCode = await ConvertCommand.RunAsync(
            new[] { carrierPath, recoveredPath, "--password", "vitriol-pass" },
            sw, default);
        reverseCode.ShouldBe(ExitCodes.Success);

        // Byte-exact round trip.
        byte[] recovered = await File.ReadAllBytesAsync(recoveredPath);
        recovered.ShouldBe(sevenZipBytes);
    }

    [Fact]
    public async Task Verify_flag_reports_byte_equal_for_password_round_trip()
    {
        byte[] sevenZipBytes = BuildFakeSevenZipBytes(seed: 42, lengthBytes: 1024);

        string dir = MakeScope();
        string sourcePath = Path.Combine(dir, "secrets.7z");
        string carrierPath = Path.Combine(dir, "carrier.png");
        await File.WriteAllBytesAsync(sourcePath, sevenZipBytes);

        StringBuilder stderr = new();
        using StringWriter sw = new(stderr);

        int code = await ConvertCommand.RunAsync(
            new[] { sourcePath, carrierPath, "--password", "chopin", "--verify" },
            sw, default);

        code.ShouldBe(ExitCodes.Success);
        stderr.ToString().ShouldContain("verified");
    }

    [Fact]
    public async Task Wrong_password_on_reverse_yields_garbage_no_error()
    {
        // No-oracle property: extracting with the wrong password returns
        // some bytes (not an error), but they don't match the original.
        // Matches the Sprint 3 UCMSv3 decrypt contract.
        byte[] sevenZipBytes = BuildFakeSevenZipBytes(seed: 7, lengthBytes: 2048);

        string dir = MakeScope();
        string sourcePath = Path.Combine(dir, "input.7z");
        string carrierPath = Path.Combine(dir, "carrier.png");
        string recoveredPath = Path.Combine(dir, "recovered.7z");
        await File.WriteAllBytesAsync(sourcePath, sevenZipBytes);

        StringBuilder stderr = new();
        using StringWriter sw = new(stderr);

        // Forward with rightpass.
        int forwardCode = await ConvertCommand.RunAsync(
            new[] { sourcePath, carrierPath, "--password", "rightpass" },
            sw, default);
        forwardCode.ShouldBe(ExitCodes.Success);

        // Reverse with wrongpass. Exit code should still be Success because
        // extraction itself doesn't fail — the bytes just don't match.
        int reverseCode = await ConvertCommand.RunAsync(
            new[] { carrierPath, recoveredPath, "--password", "wrongpass" },
            sw, default);
        reverseCode.ShouldBe(ExitCodes.Success);

        byte[] recovered = await File.ReadAllBytesAsync(recoveredPath);
        recovered.ShouldNotBe(sevenZipBytes);
    }

    [Fact]
    public async Task Require_password_without_password_returns_usage_error()
    {
        // --require-password is a defensive flag: refuse to run a Stone
        // conversion silently in plaintext if the user forgot --password.
        byte[] sevenZipBytes = BuildFakeSevenZipBytes(seed: 99, lengthBytes: 512);

        string dir = MakeScope();
        string sourcePath = Path.Combine(dir, "input.7z");
        string carrierPath = Path.Combine(dir, "carrier.png");
        await File.WriteAllBytesAsync(sourcePath, sevenZipBytes);

        StringBuilder stderr = new();
        using StringWriter sw = new(stderr);

        int code = await ConvertCommand.RunAsync(
            new[] { sourcePath, carrierPath, "--require-password" },
            sw, default);

        code.ShouldBe(ExitCodes.Usage);
        stderr.ToString().ShouldContain("--require-password");
        File.Exists(carrierPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Require_password_with_password_proceeds_normally()
    {
        byte[] sevenZipBytes = BuildFakeSevenZipBytes(seed: 101, lengthBytes: 1024);

        string dir = MakeScope();
        string sourcePath = Path.Combine(dir, "input.7z");
        string carrierPath = Path.Combine(dir, "carrier.png");
        string recoveredPath = Path.Combine(dir, "recovered.7z");
        await File.WriteAllBytesAsync(sourcePath, sevenZipBytes);

        StringBuilder stderr = new();
        using StringWriter sw = new(stderr);

        int forwardCode = await ConvertCommand.RunAsync(
            new[] { sourcePath, carrierPath, "--require-password", "--password", "chopin" },
            sw, default);
        forwardCode.ShouldBe(ExitCodes.Success);
        File.Exists(carrierPath).ShouldBeTrue();

        int reverseCode = await ConvertCommand.RunAsync(
            new[] { carrierPath, recoveredPath, "--require-password", "--password", "chopin" },
            sw, default);
        reverseCode.ShouldBe(ExitCodes.Success);

        byte[] recovered = await File.ReadAllBytesAsync(recoveredPath);
        recovered.ShouldBe(sevenZipBytes);
    }

    /// <summary>
    /// Constructs deterministic bytes with the 7-Zip magic prefix so the
    /// produced file is shape-identical to a real 7z. Vitriol treats the
    /// bytes as opaque, so the contents don't need to be a parseable
    /// archive.
    /// </summary>
    private static byte[] BuildFakeSevenZipBytes(int seed, int lengthBytes)
    {
        byte[] bytes = new byte[lengthBytes];
        new Random(seed).NextBytes(bytes);
        // 7z signature: 37 7A BC AF 27 1C
        byte[] magic = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private string MakeScope()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vitriol-7z-png-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (string d in _tempDirs)
        {
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
        }
    }
}
