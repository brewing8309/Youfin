using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Youfin.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youfin.Services;

/// <summary>Reads videos from the Jellyfin library into <see cref="VideoRecord"/>s. Read-only; never writes items.</summary>
public sealed class LibraryScanner
{
    public const string ProviderKey = "YoutubeMetadata";

    private static readonly BaseItemKind[] VideoKinds =
        [BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.MusicVideo, BaseItemKind.Video];

    private readonly ILibraryManager _library;
    private readonly ILogger<LibraryScanner> _log;

    public LibraryScanner(ILibraryManager library, ILogger<LibraryScanner> log)
    {
        _library = library;
        _log = log;
    }

    /// <summary>Raw records; channel merges are applied later by <see cref="VideoIndex"/>.</summary>
    public IReadOnlyList<VideoRecord> Scan()
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = VideoKinds,
            Recursive = true,
            IsVirtualItem = false,
        };
        var libraries = IncludedLibraries();
        if (libraries.Length > 0)
        {
            query.AncestorIds = libraries;   // only the libraries chosen in the plugin settings
        }

        var items = _library.GetItemList(query);

        var result = new List<VideoRecord>(items.Count);
        foreach (var item in items)
        {
            try
            {
                result.Add(ToRecord(item));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Youfin: skipping item {Id} ({Path})", item.Id, item.Path);
            }
        }

        _log.LogInformation("Youfin: indexed {Count} videos", result.Count);
        return result;
    }

    /// <summary>Libraries chosen in the plugin settings; empty means all libraries.</summary>
    public static Guid[] IncludedLibraries() =>
        (Plugin.Instance?.Configuration.IncludedLibraryIds ?? Array.Empty<string>())
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToArray();

    private VideoRecord ToRecord(BaseItem item)
    {
        item.ProviderIds.TryGetValue(ProviderKey, out var ytId);
        ytId ??= YouTubeIdParser.ParseVideoId(item.Path);
        var info = InfoJsonReader.TryReadForVideo(item.Path);

        string? personId = null, personName = null;
        var director = _library.GetPeople(item).FirstOrDefault(p => p.Type == PersonKind.Director);
        if (director is not null)
        {
            personName = director.Name;
            director.ProviderIds?.TryGetValue(ProviderKey, out personId);
        }

        string? seriesId = null, seriesName = null;
        if (item is MediaBrowser.Controller.Entities.TV.Episode ep)
        {
            seriesName = ep.SeriesName;
            if (ep.Series?.ProviderIds.TryGetValue(ProviderKey, out var sid) == true)
            {
                seriesId = sid;
            }
        }

        var folder = string.IsNullOrEmpty(item.Path) ? null : new DirectoryInfo(Path.GetDirectoryName(item.Path) ?? "").Name;
        var channel = ChannelResolver.Resolve(new ChannelSources(personId, personName, seriesId, seriesName, info, folder));

        return new VideoRecord(item.Id, ytId, item.Name ?? "", item.Overview, item.PremiereDate, item.DateCreated,
            item.RunTimeTicks, item.Path, channel, info?.Tags ?? Array.Empty<string>(), info?.Categories ?? Array.Empty<string>());
    }
}
