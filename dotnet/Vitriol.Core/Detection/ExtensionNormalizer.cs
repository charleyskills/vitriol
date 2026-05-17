namespace Vitriol.Core.Detection;

/// <summary>
/// Canonicalizes an extension string. Mirrors <c>normalize_ext</c> and
/// <c>ext_for</c> in <c>app/core/file_detector.py:39-51</c>: lowercase, ensure
/// leading dot, collapse aliases (<c>.jpeg → .jpg</c>), and match compound
/// tarball suffixes against the full filename.
/// </summary>
public static class ExtensionNormalizer
{
    private static readonly Dictionary<string, string> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpeg"] = ".jpg",
            [".tif"] = ".tiff",
            [".htm"] = ".html",
            [".yml"] = ".yaml",
            [".aif"] = ".aiff",
            [".mts"] = ".ts",
            [".tgz"] = ".tar.gz",
            [".tbz2"] = ".tar.bz2",
            [".tbz"] = ".tar.bz2",
            [".txz"] = ".tar.xz",
            [".tzst"] = ".tar.zst",
        };

    private static readonly string[] CompoundSuffixes =
    {
        ".tar.gz",
        ".tar.bz2",
        ".tar.xz",
        ".tar.zst",
    };

    public static string Normalize(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        string ext = extension.ToLowerInvariant();
        if (ext.Length == 0)
        {
            return ext;
        }
        if (ext[0] != '.')
        {
            ext = "." + ext;
        }
        return Aliases.TryGetValue(ext, out string? canonical) ? canonical : ext;
    }

    /// <summary>
    /// Returns the canonical extension for a file path, matching compound
    /// suffixes against the full filename.
    /// </summary>
    public static string FromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string name = Path.GetFileName(path).ToLowerInvariant();
        foreach (string compound in CompoundSuffixes)
        {
            if (name.EndsWith(compound, StringComparison.Ordinal))
            {
                return compound;
            }
        }
        string suffix = Path.GetExtension(name);
        return suffix.Length == 0 ? string.Empty : Normalize(suffix);
    }
}
