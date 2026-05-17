namespace Vitriol.Core.Routing;

/// <summary>
/// Picks the file-size threshold at which the router prefers the streaming
/// path over the whole-file IR path. Mirrors <c>streaming_threshold</c> in
/// <c>app/core/config.py:82-96</c>. Same formula:
/// <c>safe_budget = available_ram / 4</c>, divided by a per-category
/// memory multiplier, floored at 64 MB.
/// </summary>
public static class StreamingThresholds
{
    public const long DefaultThreshold = 64L * 1024 * 1024;

    public const long FallbackBudget = 500L * 1024 * 1024;

    private static readonly Dictionary<string, int> CategoryMultipliers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image"] = 8,
            ["pdf"] = 4,
            ["office_zip"] = 3,
            ["video"] = 1,
            ["audio"] = 1,
            ["text"] = 2,
            ["3d_model"] = 5,
            ["default"] = 4,
        };

    private static readonly Dictionary<string, string> ExtensionCategories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // image
            [".png"] = "image", [".bmp"] = "image", [".jpg"] = "image", [".jpeg"] = "image",
            [".webp"] = "image", [".gif"] = "image", [".tiff"] = "image", [".tif"] = "image",
            [".ico"] = "image", [".tga"] = "image", [".ppm"] = "image", [".pgm"] = "image",
            [".pbm"] = "image", [".dds"] = "image", [".heic"] = "image", [".heif"] = "image",
            [".svg"] = "image",
            // pdf
            [".pdf"] = "pdf",
            // office zip
            [".docx"] = "office_zip", [".xlsx"] = "office_zip", [".pptx"] = "office_zip",
            [".epub"] = "office_zip", [".odt"] = "office_zip",
            // video
            [".mp4"] = "video", [".mkv"] = "video", [".webm"] = "video", [".avi"] = "video",
            [".mov"] = "video", [".wmv"] = "video", [".flv"] = "video", [".mpg"] = "video",
            [".mpeg"] = "video", [".3gp"] = "video", [".ts"] = "video", [".mts"] = "video",
            [".vob"] = "video", [".ogv"] = "video", [".m4v"] = "video",
            // audio
            [".mp3"] = "audio", [".wav"] = "audio", [".flac"] = "audio", [".ogg"] = "audio",
            [".opus"] = "audio", [".m4a"] = "audio", [".aac"] = "audio", [".wma"] = "audio",
            [".aiff"] = "audio", [".aif"] = "audio", [".alac"] = "audio", [".ac3"] = "audio",
            [".amr"] = "audio", [".au"] = "audio", [".mka"] = "audio",
            // text
            [".txt"] = "text", [".md"] = "text", [".html"] = "text", [".htm"] = "text",
            [".json"] = "text", [".xml"] = "text", [".yaml"] = "text", [".yml"] = "text",
            [".ini"] = "text", [".log"] = "text", [".csv"] = "text", [".tsv"] = "text",
            [".rtf"] = "text", [".py"] = "text",
            // 3d models
            [".glb"] = "3d_model", [".gltf"] = "3d_model", [".obj"] = "3d_model",
            [".stl"] = "3d_model", [".fbx"] = "3d_model", [".ply"] = "3d_model",
            [".dae"] = "3d_model", [".3ds"] = "3d_model",
        };

    /// <summary>
    /// Threshold (bytes) above which a source of the given extension should
    /// use the streaming path.
    /// </summary>
    public static long For(string extension)
    {
        string category = ExtensionCategories.GetValueOrDefault(extension, "default");
        int multiplier = CategoryMultipliers.GetValueOrDefault(category, CategoryMultipliers["default"]);
        long safeBudget = AvailableMemoryBudget();
        long threshold = safeBudget / multiplier;
        return Math.Max(DefaultThreshold, threshold);
    }

    private static long AvailableMemoryBudget()
    {
        // GC.GetGCMemoryInfo gives us a reasonable lower bound on available
        // memory. The Python version uses psutil.virtual_memory().available;
        // GC.GetGCMemoryInfo.TotalAvailableMemoryBytes is the .NET analogue.
        // Divide by 4 to leave 75% headroom for the rest of the process.
        try
        {
            long total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (total <= 0)
            {
                return FallbackBudget;
            }
            return total / 4;
        }
        catch (InvalidOperationException)
        {
            return FallbackBudget;
        }
    }
}
