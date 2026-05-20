using System.Text;
using Vitriol.Core.Pipeline;
using Vitriol.Stone.Envelope;

namespace Vitriol.Stone.Hosts;

/// <summary>
/// TXT host: base64-encoded UCMSv1 envelope, wrapped to 76 columns. Output
/// looks like an unremarkable base64 dump (PEM-style). Mirrors
/// <c>_txt_embed</c> / <c>_txt_extract</c> in
/// <c>app/format_handlers/masquerade.py:1004-1024</c>.
/// </summary>
public sealed class TxtStoneHost : IStoneHost
{
    private const int Base64LineWidth = 76;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" };

    public bool CanEmbed => true;

    public bool CanExtract => true;

    public async ValueTask<bool> HasEnvelopeAsync(Stream source, string extension, CancellationToken cancellationToken)
    {
        // Peek the first 64 KB, strip whitespace/`#` comments, base64-decode,
        // and look for the UCMSv1 magic.
        byte[] head = new byte[64 * 1024];
        int read = 0;
        while (read < head.Length)
        {
            int n = await source.ReadAsync(head.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }
            read += n;
        }
        if (read == 0)
        {
            return false;
        }

        try
        {
            byte[] envelope = DecodeBase64(head.AsSpan(0, read));
            return envelope.AsSpan().IndexOf(UcmsMagic.V1) >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public ValueTask EmbedAsync(
        ReadOnlyMemory<byte> sourceBytes,
        string sourceExtension,
        Stream destination,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        UcmsEnvelope envelope = new(sourceExtension, sourceBytes);
        byte[] env = envelope.Build();
        string b64 = Convert.ToBase64String(env);

        StringBuilder sb = new(b64.Length + b64.Length / Base64LineWidth + 4);
        for (int i = 0; i < b64.Length; i += Base64LineWidth)
        {
            int end = Math.Min(i + Base64LineWidth, b64.Length);
            sb.Append(b64, i, end - i);
            sb.Append('\n');
        }

        byte[] output = Encoding.UTF8.GetBytes(sb.ToString());
        return destination.WriteAsync(output, cancellationToken);
    }

    public async ValueTask<StoneExtractionResult> ExtractAsync(
        Stream source,
        string sourceExtension,
        StoneOptions options,
        CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        byte[] envBytes = DecodeBase64(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));

        var envelope = UcmsEnvelope.Parse(envBytes);
        return new StoneExtractionResult(envelope.Payload, envelope.Extension);
    }

    /// <summary>
    /// Strips whitespace and lines beginning with <c>#</c>, then base64-decodes
    /// the joined body. Mirrors the line-stitching at <c>masquerade.py:1020</c>.
    /// </summary>
    internal static byte[] DecodeBase64(ReadOnlySpan<byte> raw)
    {
        // Decode UTF-8 to chars, strip whitespace and comments, then base64
        // decode. Strict text vs. binary distinction is irrelevant here since
        // base64 alphabet is ASCII-only.
        string text = Encoding.UTF8.GetString(raw);
        StringBuilder joined = new(text.Length);
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            joined.Append(line);
        }
        return Convert.FromBase64String(joined.ToString());
    }
}
