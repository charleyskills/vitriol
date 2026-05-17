using System.Text;
using Vitriol.Cli;

namespace Vitriol.Tests.Cli;

public sealed class ProgramTests
{
    [Fact]
    public async Task No_args_prints_usage_and_returns_usage_exit_code()
    {
        StringBuilder sb = new();
        using StringWriter sw = new(sb);

        int code = await Program.RunAsync(Array.Empty<string>(), sw, default);

        code.ShouldBe(ExitCodes.Usage);
        sb.ToString().ShouldContain("vitriol");
        sb.ToString().ShouldContain("convert");
    }

    [Fact]
    public async Task Help_returns_success_and_prints_usage()
    {
        StringBuilder sb = new();
        using StringWriter sw = new(sb);

        int code = await Program.RunAsync(new[] { "--help" }, sw, default);

        code.ShouldBe(ExitCodes.Success);
        sb.ToString().ShouldContain("vitriol");
    }

    [Fact]
    public async Task Version_returns_success_and_prints_version()
    {
        StringBuilder sb = new();
        using StringWriter sw = new(sb);

        int code = await Program.RunAsync(new[] { "--version" }, sw, default);

        code.ShouldBe(ExitCodes.Success);
        sb.ToString().ShouldContain("vitriol");
    }

    [Fact]
    public async Task Unknown_subcommand_returns_usage_exit_code()
    {
        StringBuilder sb = new();
        using StringWriter sw = new(sb);

        int code = await Program.RunAsync(new[] { "frobnicate" }, sw, default);

        code.ShouldBe(ExitCodes.Usage);
        sb.ToString().ShouldContain("unknown subcommand");
    }
}
