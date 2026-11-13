using System;
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

    public void Rebuild()
    {
        lock (_lock)
        {
            try
            {
                var state = Store.State;
                _index = new VideoIndex(_scanner.Scan(state.ChannelMerges), state.ChannelAliases);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Youfin: index rebuild failed; keeping previous index");
            }
        }
    }

    public Task StartAsync(CancellationToken ct)
    {
        _library.ItemAdded += OnChange;
        _library.ItemUpdated += OnChange;
        _library.ItemRemoved += OnChange;
        _timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _library.ItemAdded -= OnChange;
        _library.ItemUpdated -= OnChange;
        _library.ItemRemoved -= OnChange;
        return Task.CompletedTask;
    }

    public void Dispose() => _timer.Dispose();

    private void OnChange(object? sender, ItemChangeEventArgs e) => _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
}
