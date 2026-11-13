using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.Youfin.Data;

/// <summary>Youfin-owned state only (things Jellyfin does not store). Persisted as JSON in the plugin data folder.</summary>
public sealed class PlaylistData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<Guid> VideoIds { get; set; } = new();
}

public sealed class YoufinState
{
    public Dictionary<string, string> ChannelMerges { get; set; } = new();
    public Dictionary<string, string> ChannelAliases { get; set; } = new();
    public Dictionary<string, HashSet<string>> FavoriteChannels { get; set; } = new();

    /// <summary>Playlists per Jellyfin user id.</summary>
    public Dictionary<string, List<PlaylistData>> Playlists { get; set; } = new();
}

public sealed class UserDataStore
{
    private readonly string _file;
    private readonly object _lock = new();

    public UserDataStore(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        _file = Path.Combine(dataFolder, "state.json");
        State = Load();
    }

    public YoufinState State { get; private set; }

    public void Update(Action<YoufinState> change)
    {
        lock (_lock)
        {
            change(State);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(State));
            File.Move(tmp, _file, overwrite: true);
        }
    }

    private YoufinState Load()
    {
        try
        {
            return File.Exists(_file) ? JsonSerializer.Deserialize<YoufinState>(File.ReadAllText(_file)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new();
        }
    }
}
