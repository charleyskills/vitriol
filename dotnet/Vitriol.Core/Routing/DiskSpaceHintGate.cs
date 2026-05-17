using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Routing;

/// <summary>
/// Emits a status-bar warning when the destination's filesystem has less than
/// <c>2 × src_size × 1.5</c> free bytes and the source is bigger than 100 MB.
/// Never claims the job. Mirrors <c>router.py:90-101</c>.
/// </summary>
public sealed class DiskSpaceHintGate : IRoutingGate
{
    private const double DiskSpaceWarnMultiplier = 1.5;

    private const long LargeFileFloor = 100L * 1024 * 1024;

    public int Order => 5;

    public string Name => "DiskSpaceHint";

    public ValueTask<RoutingDecision> TryHandleAsync(
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            FileInfo src = new(context.Job.Source);
            if (!src.Exists)
            {
                return ValueTask.FromResult<RoutingDecision>(RoutingDecision.NotApplicable.Instance);
            }

            long srcSize = src.Length;
            if (srcSize <= LargeFileFloor)
            {
                return ValueTask.FromResult<RoutingDecision>(RoutingDecision.NotApplicable.Instance);
            }

            string destDir = Path.GetDirectoryName(context.Job.Destination) ?? ".";
            if (destDir.Length == 0)
            {
                destDir = ".";
            }

            string fullDestDir = Path.GetFullPath(destDir);
            string driveRoot = Path.GetPathRoot(fullDestDir) ?? fullDestDir;
            DriveInfo drive = new(driveRoot);

            if (!drive.IsReady)
            {
                return ValueTask.FromResult<RoutingDecision>(RoutingDecision.NotApplicable.Instance);
            }

            long free = drive.AvailableFreeSpace;
            if (srcSize * 2 * DiskSpaceWarnMultiplier > free)
            {
                context.AddWarning(
                    $"Low disk space at output target: {free / (1024 * 1024)} MB free; "
                    + $"conversion may need up to {(srcSize * 2) / (1024 * 1024)} MB.");
            }
        }
        catch (IOException) { /* don't fail the conversion over a hint */ }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }

        return ValueTask.FromResult<RoutingDecision>(RoutingDecision.NotApplicable.Instance);
    }
}
