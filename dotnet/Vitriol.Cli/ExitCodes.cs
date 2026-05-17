namespace Vitriol.Cli;

/// <summary>
/// CLI exit codes. Mirrors the sysexits(3) conventions where possible.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>Usage error — bad arguments, unknown subcommand.</summary>
    public const int Usage = 2;

    /// <summary>Unsupported conversion — no routing gate handled the pair.</summary>
    public const int Unsupported = 64;

    /// <summary>Verification failed — forward+reverse didn't reproduce the original.</summary>
    public const int VerificationFailed = 65;

    /// <summary>Internal error — unexpected exception. Treat as a bug.</summary>
    public const int InternalError = 70;
}
