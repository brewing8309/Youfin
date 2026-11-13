using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Youfin.Core;

/// <summary>Immutable in-memory snapshot of all videos, swapped atomically on rebuild.</summary>
public sealed class VideoIndex
{
    public static readonly VideoIndex Empty = new(Array.Empty<VideoRecord>(), new Dictionary<string, string>());

    private readonly Dictionary<Guid, VideoRecord> _byId;
    private readonly Dictionary<string, VideoRecord> _byKey = new();
    private readonly ILookup<string, VideoRecord> _byChannel;
    private readonly IReadOnlyDictionary<string, string> _aliases;

    public VideoIndex(IEnumerable<VideoRecord> videos, IReadOnlyDictionary<string, string> aliases)
    {
        All = videos.ToList();
        _aliases = aliases;
        _byId = All.ToDictionary(v => v.ItemId);
        foreach (var v in All)
        {
            _byKey.TryAdd(v.VideoKey, v);
        }

        _byChannel = All.ToLookup(v => v.Channel.Key);
    }

    public IReadOnlyList<VideoRecord> All { get; }

    public VideoRecord? Get(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>Resolves "yt:&lt;id&gt;" or "item:&lt;guid&gt;" keys; an item key also finds a video that now has a YouTube ID.</summary>
    public VideoRecord? GetByKey(string key)
    {
        if (_byKey.TryGetValue(key, out var v))
        {
            return v;
        }

        return key.StartsWith("item:", StringComparison.Ordinal) && Guid.TryParse(key.AsSpan(5), out var id) ? Get(id) : null;
    }

    public Page<VideoRecord> ByKeys(IEnumerable<string> keys, int start, int limit) =>
        Paginate(keys.Select(GetByKey).OfType<VideoRecord>().ToList(), start, limit);


    /// <summary>Display name: user alias, else the most frequent name among the channel's videos.</summary>
    public string ChannelName(string key) =>
        _aliases.TryGetValue(key, out var a) && !string.IsNullOrWhiteSpace(a)
            ? a
            : _byChannel[key].GroupBy(v => v.Channel.Name).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key).FirstOrDefault() ?? key;

    public Page<ChannelSummary> Channels(string? search, int start, int limit, ISet<string>? favorites = null, bool onlyFavorites = false)
    {
        var q = _byChannel.Select(g =>
        {
            return new ChannelSummary(g.Key, ChannelName(g.Key), g.Count(),
                g.Max(v => v.Published), g.OrderByDescending(v => v.Published ?? v.Added).First().ItemId);
        });
        if (favorites is not null)
        {
            q = q.Select(c => c with { IsFavorite = favorites.Contains(c.Key) });
        }

        if (onlyFavorites)
        {
            q = q.Where(c => c.IsFavorite);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return Paginate(q.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(), start, limit);
    }

    public Page<VideoRecord> Videos(string? channelKey, VideoSort sort, bool descending, int start, int limit,
        string? search = null, string? category = null)
    {
        IEnumerable<VideoRecord> q = channelKey is null ? All : _byChannel[channelKey];
        if (!string.IsNullOrWhiteSpace(category))
        {
            q = q.Where(v => v.Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            q = q.Where(v => Matches(v, terms));
        }

        q = (sort, descending) switch
        {
            (VideoSort.Title, false) => q.OrderBy(v => v.Title, StringComparer.OrdinalIgnoreCase),
            (VideoSort.Title, true) => q.OrderByDescending(v => v.Title, StringComparer.OrdinalIgnoreCase),
            (VideoSort.Published, false) => q.OrderBy(v => v.Published ?? DateTime.MinValue),
            (VideoSort.Published, true) => q.OrderByDescending(v => v.Published ?? DateTime.MinValue),
            (VideoSort.Added, false) => q.OrderBy(v => v.Added),
            _ => q.OrderByDescending(v => v.Added),
        };
        return Paginate(q.ToList(), start, limit);
    }

    /// <summary>All terms must match title, channel, description, YouTube ID, tags or categories (case-insensitive).</summary>
    private bool Matches(VideoRecord v, string[] terms)
    {
        var channel = ChannelName(v.Channel.Key);
        return terms.All(t =>
            v.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
            || channel.Contains(t, StringComparison.OrdinalIgnoreCase)
            || (v.Overview?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
            || (v.YouTubeId?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
            || v.Tags.Any(x => x.Contains(t, StringComparison.OrdinalIgnoreCase))
            || v.Categories.Any(x => x.Contains(t, StringComparison.OrdinalIgnoreCase)));
    }

    public IReadOnlyList<CategorySummary> Categories() =>
        All.SelectMany(v => v.Categories.Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategorySummary(g.Key, g.Count()))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static Page<T> Paginate<T>(List<T> all, int start, int limit)
    {
        start = Math.Max(0, start);
        limit = Math.Clamp(limit, 1, 200);
        return new Page<T>(all.Skip(start).Take(limit).ToList(), all.Count, start);
    }
}
