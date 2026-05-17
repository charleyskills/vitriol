using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;
using Vitriol.Core.Verification;
using Vitriol.Stone;

namespace Vitriol.Tests.Verification;

public sealed class RoundTripVerifierTests : IDisposable
{
    private readonly List<TempScope> _scopes = new();

    [Theory]
    [InlineData(".txt")]
    [InlineData(".zip")]
    [InlineData(".png")]
    public async Task Stone_host_round_trips_to_byte_equal(string hostExt)
    {
        ServiceProvider sp = BuildStoneProvider();
        IRoundTripVerifier verifier = sp.GetRequiredService<IRoundTripVerifier>();

        byte[] payload = new byte[2048];
        new Random(31).NextBytes(payload);

        TempScope scope = NewScope();
        string srcPath = Path.Combine(scope.Path, "source.bin");
        string dstPath = Path.Combine(scope.Path, "carrier" + hostExt);
        await File.WriteAllBytesAsync(srcPath, payload);

        // The verifier handles forward+reverse internally; we pass it the
        // forward job and it generates the reverse pair.
        ConversionJob job = new(srcPath, dstPath, ".bin", hostExt) { Masquerade = true };

        RoundTripResult result = await verifier.VerifyAsync(job, progress: null, default);

        RoundTripResult.ByteEqual byteEqual = result.ShouldBeOfType<RoundTripResult.ByteEqual>();
        byteEqual.Sha256.Length.ShouldBe(64);
    }

    [Fact]
    public async Task Unsupported_conversion_reports_io_error()
    {
        ServiceProvider sp = BuildStoneProvider();
        IRoundTripVerifier verifier = sp.GetRequiredService<IRoundTripVerifier>();

        TempScope scope = NewScope();
        string src = Path.Combine(scope.Path, "in.foo");
        string dst = Path.Combine(scope.Path, "out.bar");
        await File.WriteAllBytesAsync(src, new byte[] { 1, 2, 3 });

        // No reader/writer/media/Stone handler for .foo or .bar.
        ConversionJob job = new(src, dst, ".foo", ".bar");

        RoundTripResult result = await verifier.VerifyAsync(job, progress: null, default);

        RoundTripResult.IoError err = result.ShouldBeOfType<RoundTripResult.IoError>();
        err.Message.ShouldContain("conversion refused");
    }

    [Fact]
    public async Task Bytes_differ_when_reverse_does_not_recover_source()
    {
        // Mock readers/writers that succeed but don't round-trip. With the
        // structural fallback disabled, the verifier must report BytesDiffer
        // (the SHA-256 mismatch is the only signal).
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddSingleton<IFormatReader>(new MockReader(".aaa"));
        services.AddSingleton<IFormatWriter>(new MockWriter(".bbb"));
        services.AddSingleton<IFormatReader>(new MockReader(".bbb"));
        services.AddSingleton<IFormatWriter>(new MockWriter(".aaa"));

        ServiceProvider sp = services.BuildServiceProvider();
        RoundTripVerifier verifierByteOnly = new(
            sp.GetRequiredService<IConversionRouter>(),
            structural: null,
            options: new RoundTripVerifierOptions { AlsoCheckStructural = false });

        TempScope scope = NewScope();
        string src = Path.Combine(scope.Path, "in.aaa");
        string dst = Path.Combine(scope.Path, "out.bbb");
        await File.WriteAllBytesAsync(src, "real source bytes"u8.ToArray());

        ConversionJob job = new(src, dst, ".aaa", ".bbb");
        RoundTripResult result = await verifierByteOnly.VerifyAsync(job, progress: null, default);

        RoundTripResult.BytesDiffer differ = result.ShouldBeOfType<RoundTripResult.BytesDiffer>();
        differ.SourceSha256.ShouldNotBe(differ.ReverseSha256);
    }

    [Fact]
    public async Task Falls_back_to_structurally_equal_when_bytes_drift_but_ir_matches()
    {
        // Same mock readers/writers — both return TextDoc.Empty regardless of
        // input. Bytes drift (mock writer emits empty file vs the input
        // bytes), but the IR comparison via structural equivalence sees both
        // sides as TextDoc.Empty.
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddSingleton<IFormatReader>(new MockReader(".aaa"));
        services.AddSingleton<IFormatWriter>(new MockWriter(".bbb"));
        services.AddSingleton<IFormatReader>(new MockReader(".bbb"));
        services.AddSingleton<IFormatWriter>(new MockWriter(".aaa"));

        ServiceProvider sp = services.BuildServiceProvider();
        IRoundTripVerifier verifier = sp.GetRequiredService<IRoundTripVerifier>();

        TempScope scope = NewScope();
        string src = Path.Combine(scope.Path, "in.aaa");
        string dst = Path.Combine(scope.Path, "out.bbb");
        await File.WriteAllBytesAsync(src, "real source bytes"u8.ToArray());

        ConversionJob job = new(src, dst, ".aaa", ".bbb");
        RoundTripResult result = await verifier.VerifyAsync(job, progress: null, default);

        result.ShouldBeOfType<RoundTripResult.StructurallyEqual>();
    }

    private ServiceProvider BuildStoneProvider()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddVitriolStone();
        return services.BuildServiceProvider();
    }

    private TempScope NewScope()
    {
        TempScope s = new("vitriol-verify-test-");
        _scopes.Add(s);
        return s;
    }

    public void Dispose()
    {
        foreach (TempScope s in _scopes)
        {
            s.Dispose();
        }
    }

    private sealed class MockReader : IFormatReader
    {
        public MockReader(string ext)
        {
            SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ext };
        }
        public DocKind Kind => DocKind.Text;
        public IReadOnlySet<string> SupportedExtensions { get; }
        public ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken ct)
            => new(TextDoc.Empty);
    }

    private sealed class MockWriter : IFormatWriter
    {
        public MockWriter(string ext)
        {
            SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ext };
        }
        public DocKind Kind => DocKind.Text;
        public IReadOnlySet<string> SupportedExtensions { get; }
        public ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken ct)
            => ValueTask.CompletedTask;
    }
}
