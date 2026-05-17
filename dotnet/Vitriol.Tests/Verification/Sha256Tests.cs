using Vitriol.Core.Verification;

namespace Vitriol.Tests.Verification;

public sealed class Sha256Tests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task Empty_file_hashes_to_known_constant()
    {
        // SHA-256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        string path = WriteTemp(Array.Empty<byte>());
        string hash = await Sha256.HashFileAsync(path, default);
        hash.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public async Task Hashes_match_dotnet_built_in_for_known_payload()
    {
        byte[] payload = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        string path = WriteTemp(payload);

        string mine = await Sha256.HashFileAsync(path, default);
        byte[] reference = System.Security.Cryptography.SHA256.HashData(payload);
        mine.ShouldBe(Convert.ToHexStringLower(reference));
    }

    [Fact]
    public async Task Hashes_match_for_large_chunked_input()
    {
        byte[] payload = new byte[200_000];
        new Random(11).NextBytes(payload);
        string path = WriteTemp(payload);

        string mine = await Sha256.HashFileAsync(path, default);
        byte[] reference = System.Security.Cryptography.SHA256.HashData(payload);
        mine.ShouldBe(Convert.ToHexStringLower(reference));
    }

    [Fact]
    public void Hash_bytes_returns_32_byte_digest()
    {
        byte[] digest = Sha256.HashBytes("hi"u8.ToArray());
        digest.Length.ShouldBe(32);
    }

    private string WriteTemp(byte[] payload)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vitriol-sha-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, payload);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string p in _tempFiles)
        {
            try { File.Delete(p); } catch (IOException) { }
        }
    }
}

public sealed class TempScopeTests
{
    [Fact]
    public void Creates_directory_and_cleans_up_on_dispose()
    {
        string capturedPath;
        using (TempScope scope = new())
        {
            capturedPath = scope.Path;
            Directory.Exists(scope.Path).ShouldBeTrue();
        }
        Directory.Exists(capturedPath).ShouldBeFalse();
    }

    [Fact]
    public void Path_for_concatenates_under_scope_root()
    {
        using TempScope scope = new();
        string nested = scope.PathFor("forward.png");
        nested.ShouldStartWith(scope.Path);
        nested.ShouldEndWith("forward.png");
    }

    [Fact]
    public void Cleanup_swallows_io_errors()
    {
        // Pre-delete the directory to force the cleanup to handle a missing path.
        TempScope scope = new();
        string p = scope.Path;
        Directory.Delete(p, recursive: true);

        // Dispose should not throw.
        scope.Dispose();
    }
}
