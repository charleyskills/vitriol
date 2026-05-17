using Vitriol.Core.Ir;
using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Verification;

/// <summary>
/// Default <see cref="IStructuralEquivalence"/>. Looks up an
/// <see cref="IFormatReader"/> for the supplied extension, reads both files
/// into IR, and compares with the records' built-in value equality (delivered
/// in Sprint 1 via <c>EquatableArray</c> / <c>EquatableDictionary</c>).
///
/// <para>When no reader is registered (media formats, archives), returns
/// <see cref="StructuralComparisonResult.NotApplicable"/>. The verifier
/// surfaces this so callers know the byte-equal check was the only signal.</para>
/// </summary>
public sealed class IrStructuralEquivalence : IStructuralEquivalence
{
    private readonly IFormatRegistry _registry;

    public IrStructuralEquivalence(IFormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public async ValueTask<StructuralComparisonResult> CompareAsync(
        string firstPath,
        string secondPath,
        string extension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firstPath);
        ArgumentNullException.ThrowIfNull(secondPath);
        ArgumentNullException.ThrowIfNull(extension);

        IFormatReader? reader = _registry.GetReader(extension);
        if (reader is null)
        {
            return new StructuralComparisonResult.NotApplicable(
                $"No reader registered for {extension}; structural comparison unavailable.");
        }

        IDocument first;
        IDocument second;
        try
        {
            ReadContext ctx = new(extension);
            await using FileStream fs1 = File.OpenRead(firstPath);
            first = await reader.ReadAsync(fs1, ctx, cancellationToken).ConfigureAwait(false);

            await using FileStream fs2 = File.OpenRead(secondPath);
            second = await reader.ReadAsync(fs2, ctx, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException e)
        {
            return new StructuralComparisonResult.Error($"Read failed: {e.Message}");
        }
        catch (UnauthorizedAccessException e)
        {
            return new StructuralComparisonResult.Error($"Read failed: {e.Message}");
        }

        if (first.Equals(second))
        {
            return new StructuralComparisonResult.Equivalent(
                $"Structural equivalence over {extension}: documents are .Equals.");
        }

        string diff = DescribeFirstDifference(first, second);
        return new StructuralComparisonResult.Different(
            $"Structural drift in {extension}: {diff}");
    }

    /// <summary>
    /// Cheap first-difference summary. Walks block lists side-by-side and
    /// reports the first divergence so the caller can pinpoint the loss
    /// instead of just being told "they're different".
    /// </summary>
    private static string DescribeFirstDifference(IDocument first, IDocument second)
    {
        if (first.GetType() != second.GetType())
        {
            return $"document kinds differ: {first.GetType().Name} vs {second.GetType().Name}";
        }

        if (first is TextDoc a && second is TextDoc b)
        {
            if (a.Blocks.Count != b.Blocks.Count)
            {
                return $"block count {a.Blocks.Count} vs {b.Blocks.Count}";
            }
            for (int i = 0; i < a.Blocks.Count; i++)
            {
                if (!a.Blocks[i].Equals(b.Blocks[i]))
                {
                    return $"block {i} ({a.Blocks[i].GetType().Name}) differs";
                }
            }
            if (!a.Metadata.Equals(b.Metadata))
            {
                return "metadata differs";
            }
        }

        if (first is Tabular ta && second is Tabular tb)
        {
            if (ta.Sheets.Count != tb.Sheets.Count)
            {
                return $"sheet count {ta.Sheets.Count} vs {tb.Sheets.Count}";
            }
            for (int i = 0; i < ta.Sheets.Count; i++)
            {
                if (!ta.Sheets[i].Equals(tb.Sheets[i]))
                {
                    return $"sheet {i} ('{ta.Sheets[i].Name}') differs";
                }
            }
        }

        return "documents differ but the diff walker could not localize it";
    }
}
