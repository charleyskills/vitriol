namespace Vitriol.Core.Verification;

/// <summary>
/// Owns a temporary directory and removes it (recursively) on dispose.
/// Mirrors the <c>tempfile.mkdtemp</c> / <c>shutil.rmtree(..., ignore_errors=True)</c>
/// dance in <c>app/core/conversion_queue.py:162, 229</c>.
/// </summary>
public sealed class TempScope : IDisposable, IAsyncDisposable
{
    public TempScope(string prefix = "vitriol-")
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Path = root;
    }

    public string Path { get; }

    /// <summary>Returns an absolute path inside this scope with the given filename.</summary>
    public string PathFor(string fileName) => System.IO.Path.Combine(Path, fileName);

    public void Dispose() => CleanUp();

    public ValueTask DisposeAsync()
    {
        CleanUp();
        return ValueTask.CompletedTask;
    }

    private void CleanUp()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException) { /* leave it; OS will reap eventually */ }
        catch (UnauthorizedAccessException) { }
    }
}
