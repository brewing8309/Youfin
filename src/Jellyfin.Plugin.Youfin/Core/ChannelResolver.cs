using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Youfin.Core;

/// <summary>Identity of a channel: stable key plus display name.</summary>
public sealed record ChannelKey(string Key, string Name);

/// <summary>Candidate sources for a video's channel, in priority order.</summary>
public sealed record ChannelSources(
    string? PersonChannelId,
    string? PersonName,
    string? SeriesChannelId,
    string? SeriesName,
    InfoJson? Info,
    string? FolderName);

public static class ChannelResolver
{
    public const string UnknownKey = "name:unknown";

    /// <summary>Resolves channel by ID (person, series, info.json) then name fallback; applies merge map.</summary>
    public static ChannelKey Resolve(ChannelSources s, IReadOnlyDictionary<string, string>? merges = null)
    {
        var name = First(s.PersonName, s.Info?.Channel, s.SeriesName, s.FolderName) ?? "Unbekannt";
        var id = First(s.PersonChannelId, s.SeriesChannelId, s.Info?.ChannelId);

        string key = id is not null
            ? "id:" + id
            : First(s.PersonName, s.Info?.Channel, s.SeriesName, s.FolderName) is { } n
                ? "name:" + n.Trim().ToLowerInvariant()
                : UnknownKey;

        if (merges is not null)
        {
            var guard = 0;
            while (merges.TryGetValue(key, out var target) && target != key && guard++ < 16)
            {
                key = target;
            }
        }

        return new ChannelKey(key, name);
    }

    private static string? First(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v;
            }
        }

        return null;
    }
}
