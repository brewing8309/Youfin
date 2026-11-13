using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Youfin.Core;

/// <summary>Extracts YouTube video/channel IDs from file names (yt-dlp style "[id]").</summary>
public static partial class YouTubeIdParser
{
    [GeneratedRegex(@"\[([A-Za-z0-9_-]{11})\]")]
    private static partial Regex VideoRegex();

    [GeneratedRegex(@"\[(UC[A-Za-z0-9_-]{22})\]")]
    private static partial Regex ChannelRegex();

    /// <summary>Returns the last 11-char bracketed ID in the file name, or null.</summary>
    public static string? ParseVideoId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var name = System.IO.Path.GetFileName(path);
        var matches = VideoRegex().Matches(name);
        return matches.Count == 0 ? null : matches[^1].Groups[1].Value;
    }

    /// <summary>Returns a bracketed 24-char channel ID (UC...), or null.</summary>
    public static string? ParseChannelId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var m = ChannelRegex().Match(System.IO.Path.GetFileName(path));
        return m.Success ? m.Groups[1].Value : null;
    }
}
