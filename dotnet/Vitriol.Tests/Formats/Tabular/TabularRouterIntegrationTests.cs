using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;
using Vitriol.Formats.Tabular;
using Vitriol.Formats.Text;

namespace Vitriol.Tests.Formats.TabularHandlers;

/// <summary>
/// End-to-end integration: a tabular handler in the registry must route
/// through the full <see cref="Vitriol.Core.Routing.WholeFileIrGate"/> with
/// the <see cref="Vitriol.Core.Ir.Adapters.TabularToTextDocAdapter"/> bridge
/// firing for cross-kind pairs. Sprint 1's adapter never had a real
/// producer/consumer until Sprint 8.
/// </summary>
public sealed class TabularRouterIntegrationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task Csv_to_csv_round_trip_via_router_preserves_content()
    {
        string body = "name,score\nAlice,42\nBob,99\n";
        string dir = MakeScope();
        string srcPath = Path.Combine(dir, "in.csv");
        string dstPath = Path.Combine(dir, "out.csv");
        await File.WriteAllBytesAsync(srcPath, Encoding.UTF8.GetBytes(body));

        IConversionRouter router = BuildProvider().GetRequiredService<IConversionRouter>();
        ConversionJob job = new(srcPath, dstPath, ".csv", ".csv");
        await router.ConvertAsync(job, progress: null, default);

        string roundBody = await File.ReadAllTextAsync(dstPath);
        roundBody.Replace("\r\n", "\n").TrimEnd('\n').ShouldBe(body.TrimEnd('\n'));
    }

    [Fact]
    public async Task Csv_to_markdown_engages_tabular_to_textdoc_adapter()
    {
        string body = "region,revenue\nEU,1000\nUS,2500\n";
        string dir = MakeScope();
        string srcPath = Path.Combine(dir, "sales.csv");
        string dstPath = Path.Combine(dir, "report.html");
        await File.WriteAllBytesAsync(srcPath, Encoding.UTF8.GetBytes(body));

        IConversionRouter router = BuildProvider().GetRequiredService<IConversionRouter>();
        ConversionJob job = new(srcPath, dstPath, ".csv", ".html");
        await router.ConvertAsync(job, progress: null, default);

        string rendered = await File.ReadAllTextAsync(dstPath);
        // Sheet name "sales" → Heading. Rows rendered as " | "-joined.
        rendered.ShouldContain("sales");
        rendered.ShouldContain("region | revenue");
        rendered.ShouldContain("EU | 1000");
        rendered.ShouldContain("US | 2500");
    }

    [Fact]
    public async Task Xlsx_to_csv_dispatches_via_router_with_drop_warning()
    {
        // Build a 2-sheet XLSX programmatically through the handler so we
        // exercise the same write code path the rest of the suite uses.
        Tabular tabular = new(EquatableArray.Create(
            new Sheet("First", EquatableArray.Create(
                EquatableArray.Create(new Cell("a"), new Cell("b")),
                EquatableArray.Create(new Cell(1L), new Cell(2L)))),
            new Sheet("Second", EquatableArray.Create(
                EquatableArray.Create(new Cell("dropped"))))));

        string dir = MakeScope();
        string xlsxPath = Path.Combine(dir, "workbook.xlsx");
        await using (FileStream fs = File.Create(xlsxPath))
        {
            XlsxHandler xlsx = new();
            await xlsx.WriteAsync(tabular, fs, new WriteContext(".xlsx"), default);
        }

        string csvPath = Path.Combine(dir, "out.csv");
        List<ConversionEvent> events = new();
        IProgress<ConversionEvent> progress = new SyncProgress(events.Add);

        IConversionRouter router = BuildProvider().GetRequiredService<IConversionRouter>();
        ConversionJob job = new(xlsxPath, csvPath, ".xlsx", ".csv");
        await router.ConvertAsync(job, progress, default);

        string csv = await File.ReadAllTextAsync(csvPath);
        csv.ShouldContain("a,b");
        csv.ShouldNotContain("dropped");

        events.OfType<ConversionEvent.Warning>()
            .Any(w => w.Message.Contains("can only emit one sheet"))
            .ShouldBeTrue();
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddVitriolCore();
        services.AddVitriolTabular();
        services.AddVitriolText();
        return services.BuildServiceProvider();
    }

    private string MakeScope()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vitriol-tab-{Guid.NewGuid():N}");
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

    private sealed class SyncProgress : IProgress<ConversionEvent>
    {
        private readonly Action<ConversionEvent> _handler;
        public SyncProgress(Action<ConversionEvent> h) => _handler = h;
        public void Report(ConversionEvent value) => _handler(value);
    }
}
