using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Youfin.Data;

namespace Jellyfin.Plugin.Youfin.Core;

/// <summary>
/// Immutable in-memory snapshot, swapped atomically. Scanned records stay untouched; channel merges,
/// aliases, series and per-video overrides are applied as an overlay, so state changes need no library rescan.
/// </summary>
public sealed class VideoIndex
{
    public static readonly VideoIndex Empty = new(Array.Empty<VideoRecord>(), new Dictionary<string, string>());

    private readonly Dictionary<Guid, VideoRecord> _byId;
    private readonly Dictionary<string, VideoRecord> _byKey = new();
    private readonly ILookup<string, VideoRecord> _byChannel;
    private readonly IReadOnlyDictionary<string, string> _aliases;
    private readonly IReadOnlyDictionary<string, SeriesData> _series;
    private readonly Dictionary<Guid, string> _seriesOfVideo = new();
    private readonly ILookup<string, VideoRecord> _bySeries;
    private readonly IReadOnlyDictionary<string, VideoOverride> _overrides;

    public VideoIndex(
        IEnumerable<VideoRecord> videos,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyDictionary<string, string>? merges = null,
        IReadOnlyDictionary<string, SeriesData>? series = null,
        IReadOnlyDictionary<string, VideoOverride>? overrides = null)
    {
        All = videos.Select(v => merges is { Count: > 0 } && ChannelResolver.ApplyMerges(v.Channel.Key, merges) is var k && k != v.Channel.Key
            ? v with { Channel = v.Channel with { Key = k } } : v).ToList();
        _aliases = aliases;
        _series = series ?? new Dictionary<string, SeriesData>();
        _overrides = overrides ?? new Dictionary<string, VideoOverride>();
        _byId = All.ToDictionary(v => v.ItemId);
        foreach (var v in All)
        {
            _byKey.TryAdd(v.VideoKey, v);
        }

        _byChannel = All.ToLookup(v => v.Channel.Key);
        foreach (var v in All)
        {
            if (ResolveSeries(v) is { } sid)
            {
                _seriesOfVideo[v.ItemId] = sid;
            }
        }

        _bySeries = All.Where(v => _seriesOfVideo.ContainsKey(v.ItemId)).ToLookup(v => _seriesOfVideo[v.ItemId]);
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

    /// <summary>Series id of a video, or null for a standalone video. Precedence: detached > explicit series > channel rule.</summary>
    public string? SeriesOf(Guid videoId) => _seriesOfVideo.GetValueOrDefault(videoId);

    public bool IsStandalone(Guid videoId) => !_seriesOfVideo.ContainsKey(videoId);

    public SeriesData? GetSeries(string id) => _series.GetValueOrDefault(id);

    private string? ResolveSeries(VideoRecord v)
    {
        var ov = OverrideOf(v);
        if (ov?.Detached == true)
        {
            return null;
        }

        if (ov?.SeriesId is { } sid && _series.ContainsKey(sid))
        {
            return sid;
        }

        return _series.Where(s => s.Value.AutoChannelKey == v.Channel.Key).OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => s.Key).FirstOrDefault();
    }

    private VideoOverride? OverrideOf(VideoRecord v)
    {
        if (_overrides.TryGetValue(v.VideoKey, out var o))
        {
            return o;
        }

        return _overrides.GetValueOrDefault("item:" + v.ItemId.ToString("N"));
    }

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

    public Page<SeriesSummary> SeriesList(string? search, int start, int limit)
    {
        var q = _series.Select(s =>
        {
            var vids = _bySeries[s.Key].ToList();
            return new SeriesSummary(s.Key, s.Value.Name, vids.Count, vids.Max(v => v.Published),
                OrderedSeries(s.Key).FirstOrDefault()?.ItemId, s.Value.AutoChannelKey);
        });
        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(s => s.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return Paginate(q.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList(), start, limit);
    }

    /// <summary>Series members in the series' own order (published = chronological episodes).</summary>
    public IReadOnlyList<VideoRecord> OrderedSeries(string seriesId)
    {
        var members = _bySeries[seriesId];
        var sort = _series.GetValueOrDefault(seriesId)?.Sort ?? "published";
        return (sort switch
        {
            "title" => members.OrderBy(v => v.Title, StringComparer.OrdinalIgnoreCase),
            "manual" => members.OrderBy(v => OverrideOf(v)?.Order ?? int.MaxValue).ThenBy(v => v.Published ?? DateTime.MinValue),
            _ => members.OrderBy(v => v.Published ?? DateTime.MinValue).ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase),
        }).ToList();
    }

    /// <param name="seriesId">Only members of this series, in series order when <paramref name="sort"/> is Series.</param>
    /// <param name="standalone">true = only videos without series, false = only series videos.</param>
    public Page<VideoRecord> Videos(string? channelKey, VideoSort sort, bool descending, int start, int limit,
        string? search = null, string? category = null, string? seriesId = null, bool? standalone = null)
    {
        IEnumerable<VideoRecord> q = seriesId is not null ? OrderedSeries(seriesId)
            : channelKey is null ? All : _byChannel[channelKey];
        if (seriesId is not null && channelKey is not null)
        {
            q = q.Where(v => v.Channel.Key == channelKey);
        }

        if (standalone is { } s)
        {
            q = q.Where(v => IsStandalone(v.ItemId) == s);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            q = q.Where(v => v.Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            q = q.Where(v => Matches(v, terms));
        }

        if (sort != VideoSort.Series || seriesId is null)
        {
            q = (sort, descending) switch
            {
                (VideoSort.Title, false) => q.OrderBy(v => v.Title, StringComparer.OrdinalIgnoreCase),
                (VideoSort.Title, true) => q.OrderByDescending(v => v.Title, StringComparer.OrdinalIgnoreCase),
                (VideoSort.Published, false) => q.OrderBy(v => v.Published ?? DateTime.MinValue),
                (VideoSort.Published, true) => q.OrderByDescending(v => v.Published ?? DateTime.MinValue),
                (VideoSort.Added, false) => q.OrderBy(v => v.Added),
                _ => q.OrderByDescending(v => v.Added),
            };
        }

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

    public Page<VideoRecord> ByKeys(IEnumerable<string> keys, int start, int limit) =>
        Paginate(keys.Select(GetByKey).OfType<VideoRecord>().ToList(), start, limit);

    private static Page<T> Paginate<T>(List<T> all, int start, int limit)
    {
        start = Math.Max(0, start);
        limit = Math.Clamp(limit, 1, 200);
        return new Page<T>(all.Skip(start).Take(limit).ToList(), all.Count, start);
    }
}
