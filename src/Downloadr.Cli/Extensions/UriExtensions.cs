namespace Downloadr.Cli.Extensions;

using System;
using System.Linq;

public static class UriExtensions
{
    public static string ToSafeFileName(this Uri url)
    {
        var lastSegment = url.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
        var baseName = string.IsNullOrWhiteSpace(lastSegment) ? url.Host : lastSegment;
        var decoded = Uri.UnescapeDataString(baseName);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(decoded.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(cleaned) ? url.Host : cleaned;
    }
}


