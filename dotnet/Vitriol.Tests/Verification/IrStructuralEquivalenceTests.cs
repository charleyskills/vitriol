using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;
using Vitriol.Core.Verification;

namespace Vitriol.Tests.Verification;

public sealed class IrStructuralEquivalenceTests : IDisposable
{
    private readonly List<TempScope> _scopes = new();

    [Fact]
    public async Task Not_applicable_when_no_reader_is_registered()
    {
        IStructuralEquivalence comparator = BuildComparator();
        TempScope scope = NewScope();
        string a = Path.Combine(scope.Path, "a.unknownext");
        string b = Path.Combine(scope.Path, "b.unknownext");
        await File.WriteAllBytesAsync(a, new byte[] { 1 });
        await File.WriteAllBytesAsync(b, new byte[] { 2 });

        StructuralComparisonResult result = await comparator.CompareAsync(a, b, ".unknownext", default);
        StructuralComparisonResult.NotApplicable na =
            result.ShouldBeOfType<StructuralComparisonResult.NotApplicable>();
        na.Reason.ShouldContain(".unknownext");
    }

    [Fact]
    public async Task Equivalent_when_reader_returns_equal_ir_for_both_files()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddSingleton<IFormatReader>(new FixedReader(".same", TextDoc.Empty));
        ServiceProvider sp = services.BuildServiceProvider();
        IStructuralEquivalence comparator = sp.GetRequiredService<IStructuralEquivalence>();

        TempScope scope = NewScope();
        string a = Path.Combine(scope.Path, "a.same");
        string b = Path.Combine(scope.Path, "b.same");
        await File.WriteAllBytesAsync(a, new byte[] { 1, 2, 3 });
        await File.WriteAllBytesAsync(b, new byte[] { 9, 8, 7 });

        StructuralComparisonResult result = await comparator.CompareAsync(a, b, ".same", default);
        result.ShouldBeOfType<StructuralComparisonResult.Equivalent>();
    }

    [Fact]
    public async Task Different_when_reader_returns_distinct_ir_per_file()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddSingleton<IFormatReader>(new ByContentReader(".diff"));
        ServiceProvider sp = services.BuildServiceProvider();
        IStructuralEquivalence comparator = sp.GetRequiredService<IStructuralEquivalence>();

        TempScope scope = NewScope();
        string a = Path.Combine(scope.Path, "a.diff");
        string b = Path.Combine(scope.Path, "b.diff");
        await File.WriteAllBytesAsync(a, "hello"u8.ToArray());
        await File.WriteAllBytesAsync(b, "world"u8.ToArray());

        StructuralComparisonResult result = await comparator.CompareAsync(a, b, ".diff", default);
        StructuralComparisonResult.Different diff =
            result.ShouldBeOfType<StructuralComparisonResult.Different>();
        diff.Description.ShouldContain("Structural drift");
    }

    [Fact]
    public async Task Error_when_one_path_is_missing()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddSingleton<IFormatReader>(new FixedReader(".same", TextDoc.Empty));
        ServiceProvider sp = services.BuildServiceProvider();
        IStructuralEquivalence comparator = sp.GetRequiredService<IStructuralEquivalence>();

        TempScope scope = NewScope();
        string a = Path.Combine(scope.Path, "exists.same");
        await File.WriteAllBytesAsync(a, new byte[] { 1 });
        string missing = Path.Combine(scope.Path, "missing.same");

        StructuralComparisonResult result = await comparator.CompareAsync(a, missing, ".same", default);
        result.ShouldBeOfType<StructuralComparisonResult.Error>();
    }

    private IStructuralEquivalence BuildComparator()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        return services.BuildServiceProvider().GetRequiredService<IStructuralEquivalence>();
    }

    private TempScope NewScope()
    {
        TempScope s = new("vitriol-structural-test-");
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

    private sealed class FixedReader : IFormatReader
    {
        public FixedReader(string ext, IDocument doc)
        {
            SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ext };
            _doc = doc;
        }
        private readonly IDocument _doc;
        public DocKind Kind => DocKind.Text;
        public IReadOnlySet<string> SupportedExtensions { get; }
        public ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken ct)
            => new(_doc);
    }

    /// <summary>
    /// Reader that produces a TextDoc whose single Paragraph mirrors the
    /// source bytes as a UTF-8 string. Different files → different IRs.
    /// </summary>
    private sealed class ByContentReader : IFormatReader
    {
        public ByContentReader(string ext)
        {
            SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ext };
        }
        public DocKind Kind => DocKind.Text;
        public IReadOnlySet<string> SupportedExtensions { get; }

        public async ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken ct)
        {
            using MemoryStream buf = new();
            await input.CopyToAsync(buf, ct).ConfigureAwait(false);
            string text = System.Text.Encoding.UTF8.GetString(buf.ToArray());
            return new TextDoc(EquatableArray.Create<Block>(
                new Paragraph(EquatableArray.Create(new Run(text)))));
        }
    }
}
