using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Youfin.Core;

/// <summary>Immutable in-memory snapshot of all videos, swapped atomically on rebuild.</summary>
public sealed class VideoIndex
{
    public static readonly VideoIndex Empty = new(Array.Empty<VideoRecord>(), new Dictionary<string, string>());

    private readonly Dictionary<Guid, VideoRecord> _byId;
    private readonly ILookup<string, VideoRecord> _byChannel;
    private readonly IReadOnlyDictionary<string, string> _aliases;

    public VideoIndex(IEnumerable<VideoRecord> videos, IReadOnlyDictionary<string, string> aliases)
    {
        All = videos.ToList();
        _aliases = aliases;
        _byId = All.ToDictionary(v => v.ItemId);
        _byChannel = All.ToLookup(v => v.Channel.Key);
    }

    public IReadOnlyList<VideoRecord> All { get; }

    public VideoRecord? Get(Guid id) => _byId.GetValueOrDefault(id);

    public string ChannelName(string key, string fallback) =>
        _aliases.TryGetValue(key, out var a) && !string.IsNullOrWhiteSpace(a) ? a : fallback;

    public Page<ChannelSummary> Channels(string? search, int start, int limit)
    {
        var q = _byChannel.Select(g =>
        {
            var first = g.First();
            return new ChannelSummary(g.Key, ChannelName(g.Key, first.Channel.Name), g.Count(),
                g.Max(v => v.Published), g.OrderByDescending(v => v.Published ?? v.Added).First().ItemId);
        });
        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return Paginate(q.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(), start, limit);
    }

    public Page<VideoRecord> Videos(string? channelKey, VideoSort sort, bool descending, int start, int limit)
    {
        IEnumerable<VideoRecord> q = channelKey is null ? All : _byChannel[channelKey];
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

    private static Page<T> Paginate<T>(List<T> all, int start, int limit)
    {
        start = Math.Max(0, start);
        limit = Math.Clamp(limit, 1, 200);
        return new Page<T>(all.Skip(start).Take(limit).ToList(), all.Count, start);
    }
}
