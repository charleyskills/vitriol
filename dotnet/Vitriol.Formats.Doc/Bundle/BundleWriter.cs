using Vitriol.Core.Collections;
using Vitriol.Core.Ir;

namespace Vitriol.Formats.Doc.Bundle;

/// <summary>
/// Builds the Markdown image-bundle folder layout:
/// <c>&lt;parent&gt;/&lt;stem&gt;/&lt;stem&gt;.md + images/&lt;name&gt;.&lt;ext&gt;</c>
/// when a <see cref="TextDoc"/> contains <see cref="ImageBlock"/>s with
/// embedded bytes. Mirrors <c>app/core/bundle_writer.py</c>.
///
/// <para>Returns the path the caller should write the Markdown content to
/// (either the flat destination if no images, or the nested
/// <c>&lt;stem&gt;.md</c> inside the bundle folder). The returned
/// <see cref="TextDoc"/> has its <see cref="ImageBlock.Href"/> values mutated
/// to <c>"images/&lt;name&gt;.&lt;ext&gt;"</c> so the Markdown emitter sees
/// the correct relative paths.</para>
/// </summary>
public static class BundleWriter
{
    public sealed record Plan(string MarkdownPath, TextDoc BundledDoc);

    private static readonly Dictionary<string, string> MimeToExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/gif"] = ".gif",
            ["image/webp"] = ".webp",
            ["image/bmp"] = ".bmp",
            ["image/tiff"] = ".tiff",
            ["image/heic"] = ".heic",
            ["image/heif"] = ".heif",
            ["image/svg+xml"] = ".svg",
            ["image/avif"] = ".avif",
            ["image/jxl"] = ".jxl",
            ["image/x-icon"] = ".ico",
        };

    /// <summary>
    /// Prepares the bundle layout on disk (creates the nested folder and
    /// writes the image files when needed) and returns the path to write the
    /// Markdown content to. The <see cref="Plan.BundledDoc"/> is the same
    /// IR but with image hrefs pointing at the relative <c>images/</c> paths.
    /// </summary>
    public static async ValueTask<Plan> PrepareAsync(
        TextDoc doc,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(destinationPath);

        List<ImageBlock> images = new();
        CollectImages(doc.Blocks, images);

        if (images.Count == 0 || images.All(i => i.Data.IsEmpty))
        {
            return new Plan(destinationPath, doc);
        }

        string parent = Path.GetDirectoryName(Path.GetFullPath(destinationPath)) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(destinationPath);
        if (string.IsNullOrEmpty(stem))
        {
            stem = "document";
        }

        string bundleDir = Path.Combine(parent, stem);
        string imagesDir = Path.Combine(bundleDir, "images");
        Directory.CreateDirectory(imagesDir);

        // Walk the IR, write image bytes to disk, build a map for href mutation.
        HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<ImageBlock, string> imageHrefMap = new();
        int counter = 1;
        foreach (ImageBlock image in images)
        {
            if (image.Data.IsEmpty)
            {
                continue;
            }
            string ext = ExtensionFor(image);
            string baseName = SanitizeBase(image.Alt) ?? $"image{counter}";
            string fileName = MakeUnique(baseName + ext, usedFileNames);
            usedFileNames.Add(fileName);

            string imagePath = Path.Combine(imagesDir, fileName);
            await File.WriteAllBytesAsync(imagePath, image.Data.ToArray(), cancellationToken)
                .ConfigureAwait(false);

            imageHrefMap[image] = $"images/{fileName}";
            counter++;
        }

        TextDoc bundledDoc = doc with
        {
            Blocks = new EquatableArray<Block>(
                doc.Blocks.AsSpan().ToArray().Select(b => RewriteHrefs(b, imageHrefMap)).ToArray()),
        };

        string mdPath = Path.Combine(bundleDir, stem + ".md");
        return new Plan(mdPath, bundledDoc);
    }

    private static void CollectImages(EquatableArray<Block> blocks, List<ImageBlock> sink)
    {
        foreach (Block b in blocks)
        {
            switch (b)
            {
                case ImageBlock img:
                    sink.Add(img);
                    break;
                case ListBlock list:
                    foreach (EquatableArray<Block> item in list.Items)
                    {
                        CollectImages(item, sink);
                    }
                    break;
                case TableBlock table:
                    foreach (EquatableArray<EquatableArray<Block>> row in table.Rows)
                    {
                        foreach (EquatableArray<Block> cell in row)
                        {
                            CollectImages(cell, sink);
                        }
                    }
                    break;
                case Blockquote bq:
                    CollectImages(bq.Blocks, sink);
                    break;
            }
        }
    }

    private static Block RewriteHrefs(Block block, Dictionary<ImageBlock, string> map)
    {
        switch (block)
        {
            case ImageBlock img when map.TryGetValue(img, out string? href):
                return img with { Href = href };
            case ListBlock list:
                return list with
                {
                    Items = new EquatableArray<EquatableArray<Block>>(
                        list.Items.AsSpan().ToArray().Select(item =>
                            new EquatableArray<Block>(
                                item.AsSpan().ToArray()
                                    .Select(b => RewriteHrefs(b, map))
                                    .ToArray())).ToArray()),
                };
            case TableBlock table:
                return table with
                {
                    Rows = new EquatableArray<EquatableArray<EquatableArray<Block>>>(
                        table.Rows.AsSpan().ToArray().Select(row =>
                            new EquatableArray<EquatableArray<Block>>(
                                row.AsSpan().ToArray().Select(cell =>
                                    new EquatableArray<Block>(
                                        cell.AsSpan().ToArray()
                                            .Select(b => RewriteHrefs(b, map))
                                            .ToArray())).ToArray())).ToArray()),
                };
            case Blockquote bq:
                return bq with
                {
                    Blocks = new EquatableArray<Block>(
                        bq.Blocks.AsSpan().ToArray()
                            .Select(b => RewriteHrefs(b, map)).ToArray()),
                };
            default:
                return block;
        }
    }

    internal static string ExtensionFor(ImageBlock image)
    {
        if (MimeToExtension.TryGetValue(image.Mime, out string? ext))
        {
            return ext;
        }
        return ".bin";
    }

    private static string? SanitizeBase(string? alt)
    {
        if (string.IsNullOrWhiteSpace(alt))
        {
            return null;
        }
        // Strip path-unsafe characters while preserving readable names.
        Span<char> buf = stackalloc char[Math.Min(alt.Length, 64)];
        int len = 0;
        foreach (char c in alt.AsSpan(0, Math.Min(alt.Length, 64)))
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                buf[len++] = c;
            }
            else if (c == ' ' || c == '.')
            {
                buf[len++] = '_';
            }
        }
        return len > 0 ? new string(buf[..len]) : null;
    }

    private static string MakeUnique(string proposed, HashSet<string> used)
    {
        if (!used.Contains(proposed))
        {
            return proposed;
        }
        string stem = Path.GetFileNameWithoutExtension(proposed);
        string ext = Path.GetExtension(proposed);
        int n = 2;
        while (true)
        {
            string candidate = $"{stem}_{n}{ext}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
            n++;
        }
    }
}
