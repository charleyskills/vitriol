using Vitriol.Core.Pipeline;

namespace Vitriol.Core.Registry;

/// <summary>
/// Static policy tables. Mirrors the module-level constants in
/// <c>app/format_handlers/__init__.py</c> and the lossy-source table in
/// <c>app/format_handlers/masquerade.py:99-107</c>.
/// </summary>
public static class KnownExtensions
{
    /// <summary>
    /// Source extensions whose only meaningful operation in Vitriol is Stone
    /// mode. The router auto-engages Stone for these. Mirrors
    /// <c>STONE_ONLY_SOURCES</c> at <c>__init__.py:50</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> StoneOnlySources =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".exe",
        };

    /// <summary>
    /// Extensions that auto-execute on extraction. The router refuses any
    /// conversion where BOTH source and target are in this set, to prevent
    /// the tool from being used as a malware-wrapping pipeline. Mirrors
    /// <c>AUTO_EXECUTE_EXTS</c> at <c>__init__.py:63</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> AutoExecuteExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".py",
            ".exe",
        };

    /// <summary>
    /// Lossy formats that cannot serve as Stone sources — Stone needs lossless
    /// bytes to round-trip. Mirrors <c>LOSSY_EXTS</c> at <c>masquerade.py:99-107</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> LossySources =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            ".jpg", ".jpeg", ".webp", ".heic",
            // Audio
            ".mp3", ".ogg", ".opus", ".aac", ".wma", ".ac3", ".amr",
            // Video
            ".mp4", ".webm", ".mov", ".wmv", ".flv", ".mpg", ".3gp", ".ts",
            ".vob", ".ogv", ".avi",
        };

    /// <summary>
    /// Media-category lookup. Mirrors <c>MEDIA_CATEGORY_OF</c>, which Vitriol
    /// builds at handler-registration time. Hardcoded here so the router can
    /// answer category questions without needing the full handler graph.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, MediaCategory> MediaCategoryOf =
        new Dictionary<string, MediaCategory>(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            [".png"] = MediaCategory.Image,
            [".jpg"] = MediaCategory.Image,
            [".jpeg"] = MediaCategory.Image,
            [".webp"] = MediaCategory.Image,
            [".bmp"] = MediaCategory.Image,
            [".tiff"] = MediaCategory.Image,
            [".tif"] = MediaCategory.Image,
            [".gif"] = MediaCategory.Image,
            [".ico"] = MediaCategory.Image,
            [".tga"] = MediaCategory.Image,
            [".dib"] = MediaCategory.Image,
            [".msp"] = MediaCategory.Image,
            [".pcx"] = MediaCategory.Image,
            [".apng"] = MediaCategory.Image,
            [".avif"] = MediaCategory.Image,
            [".heic"] = MediaCategory.Image,
            [".heif"] = MediaCategory.Image,
            [".jxl"] = MediaCategory.Image,
            [".jp2"] = MediaCategory.Image,
            [".qoi"] = MediaCategory.Image,
            [".icns"] = MediaCategory.Image,
            [".sgi"] = MediaCategory.Image,
            [".pfm"] = MediaCategory.Image,
            [".pnm"] = MediaCategory.Image,
            [".ppm"] = MediaCategory.Image,
            [".pgm"] = MediaCategory.Image,
            [".pbm"] = MediaCategory.Image,
            [".dds"] = MediaCategory.Image,
            [".xbm"] = MediaCategory.Image,
            [".svg"] = MediaCategory.Image,
            // Audio
            [".mp3"] = MediaCategory.Audio,
            [".wav"] = MediaCategory.Audio,
            [".flac"] = MediaCategory.Audio,
            [".ogg"] = MediaCategory.Audio,
            [".opus"] = MediaCategory.Audio,
            [".m4a"] = MediaCategory.Audio,
            [".aac"] = MediaCategory.Audio,
            [".wma"] = MediaCategory.Audio,
            [".aiff"] = MediaCategory.Audio,
            [".aif"] = MediaCategory.Audio,
            [".alac"] = MediaCategory.Audio,
            [".ac3"] = MediaCategory.Audio,
            [".amr"] = MediaCategory.Audio,
            [".au"] = MediaCategory.Audio,
            [".mka"] = MediaCategory.Audio,
            [".oga"] = MediaCategory.Audio,
            [".mp2"] = MediaCategory.Audio,
            // Video
            [".mp4"] = MediaCategory.Video,
            [".mkv"] = MediaCategory.Video,
            [".webm"] = MediaCategory.Video,
            [".avi"] = MediaCategory.Video,
            [".mov"] = MediaCategory.Video,
            [".wmv"] = MediaCategory.Video,
            [".flv"] = MediaCategory.Video,
            [".mpg"] = MediaCategory.Video,
            [".mpeg"] = MediaCategory.Video,
            [".3gp"] = MediaCategory.Video,
            [".ts"] = MediaCategory.Video,
            [".m4v"] = MediaCategory.Video,
            [".asf"] = MediaCategory.Video,
            [".f4v"] = MediaCategory.Video,
            [".vob"] = MediaCategory.Video,
            [".ogv"] = MediaCategory.Video,
            // 3D models
            [".glb"] = MediaCategory.Model,
            [".gltf"] = MediaCategory.Model,
            [".obj"] = MediaCategory.Model,
            [".stl"] = MediaCategory.Model,
            [".fbx"] = MediaCategory.Model,
            [".ply"] = MediaCategory.Model,
            [".dae"] = MediaCategory.Model,
            [".3ds"] = MediaCategory.Model,
        };

    public static MediaCategory? TryCategoryOf(string extension) =>
        MediaCategoryOf.TryGetValue(extension, out MediaCategory cat) ? cat : null;

    public static bool IsStoneOnlySource(string extension) =>
        StoneOnlySources.Contains(extension);

    public static bool IsAutoExecute(string extension) =>
        AutoExecuteExtensions.Contains(extension);

    public static bool IsLossySource(string extension) =>
        LossySources.Contains(extension);
}
