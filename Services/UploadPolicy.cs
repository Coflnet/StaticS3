using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Coflnet.StaticS3.Services;

/// <summary>
/// Which content types the public object endpoint accepts, how large each may be and
/// (for the newer types) which key extensions are allowed.
/// </summary>
public static class UploadPolicy
{
    public const long ImageMaxBytes = 5 * 1024 * 1024;
    public const long VideoMaxBytes = 32 * 1024 * 1024;

    private sealed record Rule(long MaxBytes, string[]? Extensions);

    // PNG has no extension rule: other services already upload PNGs under extension-less keys.
    private static readonly Dictionary<string, Rule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = new(ImageMaxBytes, null),
        ["image/webp"] = new(ImageMaxBytes, [".webp"]),
        ["image/avif"] = new(ImageMaxBytes, [".avif"]),
        ["image/jpeg"] = new(ImageMaxBytes, [".jpg", ".jpeg"]),
        ["image/gif"] = new(ImageMaxBytes, [".gif"]),
        ["video/mp4"] = new(VideoMaxBytes, [".mp4"]),
        ["video/webm"] = new(VideoMaxBytes, [".webm"]),
    };

    public static IReadOnlyCollection<string> ContentTypes => Rules.Keys;

    /// <summary>The largest body any accepted type may have.</summary>
    public static long AbsoluteMaxBytes => Rules.Values.Max(rule => rule.MaxBytes);

    public static bool IsSupported(string? contentType) =>
        contentType != null && Rules.ContainsKey(contentType);

    public static long MaxBytesFor(string contentType) => Rules[contentType].MaxBytes;

    public static bool ExtensionMatches(string contentType, string key)
    {
        var extensions = Rules[contentType].Extensions;
        if (extensions == null)
            return true;
        return extensions.Contains(Path.GetExtension(key), StringComparer.OrdinalIgnoreCase);
    }
}
