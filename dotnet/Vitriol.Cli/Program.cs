namespace Vitriol.Cli;

/// <summary>
/// Entry point for the <c>vitriol</c> CLI. Dispatches to subcommands; the
/// only one wired up today is <c>convert</c>. Hand-rolled to avoid pulling
/// the beta System.CommandLine API surface.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        return await RunAsync(args, Console.Error, cts.Token).ConfigureAwait(false);
    }

    internal static async Task<int> RunAsync(string[] args, TextWriter stderr, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintRootUsage(stderr);
            return ExitCodes.Usage;
        }

        switch (args[0])
        {
            case "convert":
                return await ConvertCommand.RunAsync(args[1..], stderr, cancellationToken).ConfigureAwait(false);
            case "-h":
            case "--help":
            case "help":
                PrintRootUsage(stderr);
                return ExitCodes.Success;
            case "-V":
            case "--version":
                PrintVersion(stderr);
                return ExitCodes.Success;
            default:
                stderr.WriteLine($"vitriol: unknown subcommand '{args[0]}'");
                if (args[0].StartsWith("--", StringComparison.Ordinal))
                {
                    // Common mistake: typing `--convert` (flag syntax) instead of `convert`
                    // (bare subcommand). The README shows `dotnet run -- convert …` where the
                    // `--` is a dotnet-run separator, not part of the subcommand name.
                    string suggestion = args[0][2..];
                    stderr.WriteLine($"  hint: subcommands don't use '--'. Did you mean:  vitriol {suggestion} …?");
                }
                PrintRootUsage(stderr);
                return ExitCodes.Usage;
        }
    }

    private static void PrintRootUsage(TextWriter stderr)
    {
        stderr.WriteLine("vitriol - file converter (.NET 10 port)");
        stderr.WriteLine();
        stderr.WriteLine("usage:");
        stderr.WriteLine("  vitriol convert <src> <dst> [options]");
        stderr.WriteLine("  vitriol --version");
        stderr.WriteLine("  vitriol --help");
        stderr.WriteLine();
        stderr.WriteLine("see 'vitriol convert --help' for conversion options.");
    }

    private static void PrintVersion(TextWriter stderr)
    {
        string? v = typeof(Program).Assembly.GetName().Version?.ToString();
        stderr.WriteLine($"vitriol {v ?? "0.0.0"} (Vitriol .NET 10 port — in development)");
    }
}
