using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Youfin.Core;

/// <summary>Minimal per-video info Youfin keeps; everything else is read live from Jellyfin.</summary>
public sealed record VideoRecord(
    Guid ItemId,
    string? YouTubeId,
    string Title,
    string? Overview,
    DateTime? Published,
    DateTime Added,
    long? RuntimeTicks,
    string? Path,
    ChannelKey Channel,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Categories)
{
    /// <summary>Stable identity for user data: the YouTube ID, else the Jellyfin item id.</summary>
    public string VideoKey => KeyOf(YouTubeId, ItemId);

    public static string KeyOf(string? youTubeId, Guid itemId) =>
        string.IsNullOrEmpty(youTubeId) ? "item:" + itemId.ToString("N") : "yt:" + youTubeId;
}

public sealed record ChannelSummary(string Key, string Name, int VideoCount, DateTime? LatestPublished, Guid? SampleVideoId, bool IsFavorite = false);

public sealed record SeriesSummary(string Id, string Name, int VideoCount, DateTime? LatestPublished, Guid? FirstVideoId, string? AutoChannelKey, string Sort);

public sealed record CategorySummary(string Name, int VideoCount);

public enum VideoSort { Added, Published, Title, Series }

public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int StartIndex);
