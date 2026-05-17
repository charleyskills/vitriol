using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vitriol.Core.Detection;
using Vitriol.Core.Pipeline;
using Vitriol.Core.Registry;
using Vitriol.Core.Verification;
using Vitriol.Formats.Image;
using Vitriol.Formats.Text;
using Vitriol.Stone;

namespace Vitriol.Cli;

/// <summary>
/// <c>vitriol convert &lt;src&gt; &lt;dst&gt; [--verify] [--password &lt;p&gt;]
/// [--masquerade] [--compiler] [--verbose]</c>
///
/// <para>Hand-rolled arg parser to avoid the System.CommandLine beta-API
/// surface. The option set is small and stable.</para>
/// </summary>
public sealed class ConvertCommand
{
    public sealed record Args(
        string Source,
        string Destination,
        bool Verify = false,
        bool Masquerade = false,
        bool Compiler = false,
        bool Verbose = false,
        string? Password = null);

    public static (Args? Parsed, string? Error) Parse(string[] argv)
    {
        string? src = null;
        string? dst = null;
        bool verify = false;
        bool masquerade = false;
        bool compiler = false;
        bool verbose = false;
        string? password = null;

        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            switch (a)
            {
                case "--verify":
                case "-v":
                    verify = true;
                    break;
                case "--masquerade":
                case "-m":
                    masquerade = true;
                    break;
                case "--compiler":
                    compiler = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--password":
                case "-p":
                    if (i + 1 >= argv.Length)
                    {
                        return (null, "--password requires a value");
                    }
                    password = argv[++i];
                    break;
                case "--":
                    // positional separator
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                    {
                        return (null, $"unknown option: {a}");
                    }
                    if (src is null) { src = a; }
                    else if (dst is null) { dst = a; }
                    else { return (null, $"unexpected positional argument: {a}"); }
                    break;
            }
        }

        if (src is null || dst is null)
        {
            return (null, "convert requires <src> and <dst> paths");
        }

        return (new Args(src, dst, verify, masquerade, compiler, verbose, password), null);
    }

    /// <summary>
    /// End-to-end: parse args, build the DI container, run the conversion or
    /// verification, return an exit code. Stays static so tests can call it
    /// with a custom <see cref="TextWriter"/> for stderr.
    /// </summary>
    public static async Task<int> RunAsync(string[] argv, TextWriter stderr, CancellationToken cancellationToken)
    {
        (Args? parsed, string? error) = Parse(argv);
        if (parsed is null)
        {
            stderr.WriteLine($"vitriol convert: {error}");
            PrintUsage(stderr);
            return ExitCodes.Usage;
        }

        if (!File.Exists(parsed.Source))
        {
            stderr.WriteLine($"vitriol convert: source not found: {parsed.Source}");
            return ExitCodes.Usage;
        }

        await using ServiceProvider services = BuildServices();
        IConversionRouter router = services.GetRequiredService<IConversionRouter>();
        IRoundTripVerifier verifier = services.GetRequiredService<IRoundTripVerifier>();

        string srcExt = ExtensionNormalizer.FromPath(parsed.Source);
        string dstExt = ExtensionNormalizer.FromPath(parsed.Destination);

        ConsoleProgressReporter progress = new(stderr, parsed.Verbose, parsed.Verify);

        ConversionJob job = new(parsed.Source, parsed.Destination, srcExt, dstExt)
        {
            VerifyRoundTrip = false, // the verifier runs the doubled conversion itself
            Masquerade = parsed.Masquerade,
            Compiler = parsed.Compiler,
            Password = parsed.Password is null
                ? ReadOnlyMemory<byte>.Empty
                : Encoding.UTF8.GetBytes(parsed.Password),
        };

        try
        {
            if (parsed.Verify)
            {
                RoundTripResult result = await verifier.VerifyAsync(job, progress, cancellationToken)
                    .ConfigureAwait(false);

                switch (result)
                {
                    case RoundTripResult.ByteEqual be:
                        stderr.WriteLine($"verified: round-trip byte-equal (SHA-256 = {be.Sha256})");
                        // Commit forward conversion now that verification passed.
                        await router.ConvertAsync(job, progress, cancellationToken).ConfigureAwait(false);
                        return ExitCodes.Success;
                    case RoundTripResult.StructurallyEqual se:
                        stderr.WriteLine($"verified: round-trip structurally-equivalent ({se.Description})");
                        await router.ConvertAsync(job, progress, cancellationToken).ConfigureAwait(false);
                        return ExitCodes.Success;
                    case RoundTripResult.BytesDiffer bd:
                        stderr.WriteLine($"verification failed: bytes differ. {bd.Description}");
                        return ExitCodes.VerificationFailed;
                    case RoundTripResult.StructurallyDiffers sd:
                        stderr.WriteLine($"verification failed: structural drift. {sd.Description}");
                        return ExitCodes.VerificationFailed;
                    case RoundTripResult.IoError ioe:
                        stderr.WriteLine($"verification could not complete: {ioe.Message}");
                        return ExitCodes.InternalError;
                }
                return ExitCodes.InternalError;
            }
            else
            {
                await router.ConvertAsync(job, progress, cancellationToken).ConfigureAwait(false);
                return ExitCodes.Success;
            }
        }
        catch (UnsupportedConversionException e)
        {
            stderr.WriteLine($"vitriol convert: unsupported conversion: {e.Message}");
            return ExitCodes.Unsupported;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("vitriol convert: cancelled");
            return ExitCodes.InternalError;
        }
        catch (Exception e)
        {
            stderr.WriteLine($"vitriol convert: internal error: {e}");
            return ExitCodes.InternalError;
        }
    }

    internal static ServiceProvider BuildServices()
    {
        ServiceCollection services = new();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddVitriolCore();
        services.AddVitriolStone();
        services.AddVitriolText();
        services.AddVitriolImage();
        return services.BuildServiceProvider();
    }

    public static void PrintUsage(TextWriter stderr)
    {
        stderr.WriteLine("usage: vitriol convert <src> <dst> [options]");
        stderr.WriteLine();
        stderr.WriteLine("options:");
        stderr.WriteLine("  -v, --verify              forward+reverse round-trip with SHA-256 compare");
        stderr.WriteLine("  -p, --password <secret>   password for Stone v3 encrypted carriers");
        stderr.WriteLine("  -m, --masquerade          engage Philosopher's Stone for this conversion");
        stderr.WriteLine("      --compiler            embed source into a self-extracting .py (target=.py)");
        stderr.WriteLine("      --verbose             print stage / progress events to stderr");
    }
}
