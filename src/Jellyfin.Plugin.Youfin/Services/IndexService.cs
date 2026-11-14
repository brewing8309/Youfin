using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Youfin.Core;
using Jellyfin.Plugin.Youfin.Data;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youfin.Services;

/// <summary>Holds the current index; rebuilds (debounced) on library changes.</summary>
public sealed class IndexService : IHostedService, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(30);

    private readonly LibraryScanner _scanner;
    private readonly ILibraryManager _library;
    private readonly ILogger<IndexService> _log;
    private readonly Timer _timer;
    private readonly object _lock = new();
    private volatile VideoIndex _index = VideoIndex.Empty;
    private IReadOnlyList<VideoRecord> _raw = Array.Empty<VideoRecord>();

    public IndexService(LibraryScanner scanner, UserDataStore store, ILibraryManager library, ILogger<IndexService> log)
    {
        _scanner = scanner;
        Store = store;
        _library = library;
        _log = log;
        _timer = new Timer(_ => Rebuild(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public UserDataStore Store { get; }

    public VideoIndex Index => _index;

    /// <summary>Rescans the Jellyfin library, then applies the Youfin overlay.</summary>
    public void Rebuild()
    {
        lock (_lock)
        {
            try
            {
                _raw = _scanner.Scan();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Youfin: library scan failed; keeping previous data");
                return;
            }

            Reapply();
        }
    }

    /// <summary>Re-applies merges, aliases, series and overrides to the last scan (no library access, fast).</summary>
    public void Reapply()
    {
        lock (_lock)
        {
            try
            {
                var st = Store.State;
                _index = new VideoIndex(_raw, st.AliasMap(), st.MergeMap(), st.Series, st.Overrides);
                NormalizeKeys(_index);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Youfin: index rebuild failed; keeping previous index");
            }
        }
    }

    /// <summary>Replaces legacy "item:" keys in collections with stable "yt:" keys once the video is known.</summary>
    private void NormalizeKeys(VideoIndex index)
    {
        var changed = Store.State.Users.Values.SelectMany(u => u.Collections).Any(c => c.VideoKeys.Any(k => Normalized(index, k) != k));
        if (!changed)
        {
            return;
        }

        Store.Update(st =>
        {
            foreach (var c in st.Users.Values.SelectMany(u => u.Collections))
            {
                c.VideoKeys = c.VideoKeys.Select(k => Normalized(index, k)).Distinct().ToList();
            }
        });
    }

    private static string Normalized(VideoIndex index, string key) => index.GetByKey(key)?.VideoKey ?? key;

    public Task StartAsync(CancellationToken ct)
    {
        _library.ItemAdded += OnChange;
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += OnConfigChanged;   // e.g. other libraries selected
        }

        _library.ItemUpdated += OnChange;
        _library.ItemRemoved += OnChange;
        _timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _library.ItemAdded -= OnChange;
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged -= OnConfigChanged;
        }

        _library.ItemUpdated -= OnChange;
        _library.ItemRemoved -= OnChange;
        return Task.CompletedTask;
    }

    public void Dispose() => _timer.Dispose();

    private void OnConfigChanged(object? sender, MediaBrowser.Model.Plugins.BasePluginConfiguration e) => _timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

    private void OnChange(object? sender, ItemChangeEventArgs e) => _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
}
