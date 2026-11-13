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
/// Mirrors channels as Jellyfin collections (BoxSets) so native apps (FireTV, Swiftfin, Infuse) can browse by channel.
/// Off by default (Plugin setting SyncCollections). Only adds videos; never removes collections or items.
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

    public string Name => "Youfin: Kanal-Collections synchronisieren";

    public string Key => "YoufinCollectionSync";

    public string Description => "Legt pro YouTube-Kanal eine Collection an (für Apps ohne Plugin-Oberfläche). Nur aktiv, wenn in den Plugin-Einstellungen aktiviert.";

    public string Category => "Youfin";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
        [new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(24).Ticks }];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.SyncCollections != true)
        {
            _log.LogInformation("Youfin: collection sync disabled");
            return;
        }

        _index.Rebuild();
        var index = _index.Index;
        var existing = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.BoxSet], Recursive = true })
            .Where(c => c.ProviderIds.ContainsKey(ProviderKey))
            .ToDictionary(c => c.ProviderIds[ProviderKey], c => c);

        var channels = index.Channels(null, 0, int.MaxValue).Items.Where(c => c.VideoCount >= MinVideos).ToList();
        var done = 0;
        foreach (var ch in channels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var ids = index.Videos(ch.Key, Core.VideoSort.Published, false, 0, 200).Items.Select(v => v.ItemId).ToList();
                if (existing.TryGetValue(ch.Key, out var box))
                {
                    var have = ((MediaBrowser.Controller.Entities.Movies.BoxSet)box).GetLinkedChildren().Select(i => i.Id).ToHashSet();
                    var missing = ids.Where(i => !have.Contains(i)).ToList();
                    if (missing.Count > 0)
                    {
                        await _collections.AddToCollectionAsync(box.Id, missing).ConfigureAwait(false);
                    }
                }
                else
                {
                    await _collections.CreateCollectionAsync(new CollectionCreationOptions
                    {
                        Name = ch.Name,
                        ItemIdList = ids.Select(i => i.ToString("N")).ToList(),
                        ProviderIds = new Dictionary<string, string> { [ProviderKey] = ch.Key },
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Youfin: collection sync failed for channel {Channel}", ch.Name);
            }

            progress.Report(100.0 * ++done / channels.Count);
        }

        _log.LogInformation("Youfin: synced {Count} channel collections", channels.Count);
    }
}
