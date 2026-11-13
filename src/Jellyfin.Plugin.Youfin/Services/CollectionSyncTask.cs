using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youfin.Services;

/// <summary>
/// Mirrors Youfin series (and optionally channels) as Jellyfin collections (BoxSets) so native apps (FireTV, Swiftfin, Infuse)
/// can browse them. Off by default (Plugin setting SyncCollections). Never deletes collections or library items.
/// </summary>
public sealed class CollectionSyncTask : IScheduledTask
{
    public const string ProviderKey = "Youfin";
    private const int MinVideos = 2;

    private readonly IndexService _index;
    private readonly ICollectionManager _collections;
    private readonly ILibraryManager _library;
    private readonly ILogger<CollectionSyncTask> _log;

    public CollectionSyncTask(IndexService index, ICollectionManager collections, ILibraryManager library, ILogger<CollectionSyncTask> log)
    {
        _index = index;
        _collections = collections;
        _library = library;
        _log = log;
    }

    public string Name => "Youfin: Sync series collections";

    public string Key => "YoufinCollectionSync";

    public string Description => "Creates one Jellyfin collection per Youfin series (for apps that cannot show plugin pages). Only active if enabled in the plugin settings.";

    public string Category => "Youfin";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
        [new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(24).Ticks }];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg?.SyncCollections != true)
        {
            _log.LogInformation("Youfin: collection sync disabled");
            return;
        }

        _index.Rebuild();
        var index = _index.Index;

        // Desired state: key -> (name, video ids). Series always; channels only when enabled.
        var targets = new List<(string Key, string Name, List<Guid> Ids)>();
        foreach (var s in index.SeriesList(null, 0, int.MaxValue).Items)
        {
            targets.Add(("series:" + s.Id, s.Name, index.OrderedSeries(s.Id).Select(v => v.ItemId).ToList()));
        }

        if (cfg.SyncChannelCollections)
        {
            foreach (var c in index.Channels(null, 0, int.MaxValue).Items.Where(c => c.VideoCount >= MinVideos))
            {
                targets.Add((c.Key, c.Name, index.Videos(c.Key, Core.VideoSort.Published, false, 0, int.MaxValue).Items.Select(v => v.ItemId).ToList()));
            }
        }

        var existing = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.BoxSet], Recursive = true })
            .Where(c => c.ProviderIds.ContainsKey(ProviderKey))
            .ToDictionary(c => c.ProviderIds[ProviderKey], c => c);

        var done = 0;
        foreach (var (key, name, ids) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (existing.TryGetValue(key, out var box))
                {
                    var have = ((MediaBrowser.Controller.Entities.Movies.BoxSet)box).GetLinkedChildren().Select(i => i.Id).ToHashSet();
                    var missing = ids.Where(i => !have.Contains(i)).ToList();
                    if (missing.Count > 0)
                    {
                        await _collections.AddToCollectionAsync(box.Id, missing).ConfigureAwait(false);
                    }

                    // Only videos Youfin knows about are removed; anything a user added by hand stays.
                    var wanted = ids.ToHashSet();
                    var extra = have.Where(i => !wanted.Contains(i) && index.Get(i) is not null).ToList();
                    if (extra.Count > 0)
                    {
                        await _collections.RemoveFromCollectionAsync(box.Id, extra).ConfigureAwait(false);
                    }
                }
                else if (ids.Count > 0)
                {
                    await _collections.CreateCollectionAsync(new CollectionCreationOptions
                    {
                        Name = name,
                        ItemIdList = ids.Select(i => i.ToString("N")).ToList(),
                        ProviderIds = new Dictionary<string, string> { [ProviderKey] = key },
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Youfin: collection sync failed for {Name}", name);
            }

            progress.Report(100.0 * ++done / Math.Max(1, targets.Count));
        }

        // Series that no longer exist: empty their (Youfin-owned) collections; the empty shell is left for the user to delete.
        var liveKeys = targets.Select(t => t.Key).ToHashSet();
        foreach (var (key, box) in existing.Where(e => e.Key.StartsWith("series:", StringComparison.Ordinal) && !liveKeys.Contains(e.Key)))
        {
            try
            {
                var stale = ((MediaBrowser.Controller.Entities.Movies.BoxSet)box).GetLinkedChildren().Select(i => i.Id).Where(i => index.Get(i) is not null).ToList();
                if (stale.Count > 0)
                {
                    await _collections.RemoveFromCollectionAsync(box.Id, stale).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Youfin: could not empty stale collection {Key}", key);
            }
        }

        _log.LogInformation("Youfin: synced {Count} collections", targets.Count);
    }
}
