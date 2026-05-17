namespace Vitriol.Core.Verification;

/// <summary>
/// Compares two same-extension files for IR-level equivalence. Used as a
/// fallback signal when byte-level equality is unreachable (the IR path
/// re-encodes the document, so the bytes drift even when the content survives).
///
/// <para>The design-doc recommendation at <c>docs/dotnet10-lossless-file-conversion-plan.md</c>
/// Section 12: <em>"Add structural-equivalence verification for the IR path so
/// users have a signal when the round-trip is structurally lossy."</em></para>
/// </summary>
public interface IStructuralEquivalence
{
    ValueTask<StructuralComparisonResult> CompareAsync(
        string firstPath,
        string secondPath,
        string extension,
        CancellationToken cancellationToken);
}

public abstract record StructuralComparisonResult
{
    protected StructuralComparisonResult() { }

    /// <summary>The two files produce the same IR.</summary>
    public sealed record Equivalent(string Description) : StructuralComparisonResult;

    /// <summary>The two files produce different IR; the description names the first difference.</summary>
    public sealed record Different(string Description) : StructuralComparisonResult;

    /// <summary>No reader is registered for the extension; structural comparison can't be performed.</summary>
    public sealed record NotApplicable(string Reason) : StructuralComparisonResult;

    /// <summary>One of the reads failed (file missing, malformed, etc.).</summary>
    public sealed record Error(string Message) : StructuralComparisonResult;
}
