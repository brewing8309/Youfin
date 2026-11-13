using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Youfin.Data;

public sealed class ChannelConfig
{
    public string? Alias { get; set; }

    public string? MergedInto { get; set; }
}

public sealed class SeriesData
{
    public string Name { get; set; } = "";

    /// <summary>published | manual | title</summary>
    public string Sort { get; set; } = "published";

    /// <summary>New videos of this channel join the series automatically (unless detached).</summary>
    public string? AutoChannelKey { get; set; }
}

/// <summary>Sparse per-video deviation from the default (default = standalone video).</summary>
public sealed class VideoOverride
{
    public string? SeriesId { get; set; }

    /// <summary>Explicitly not part of a series, even if a channel rule would add it.</summary>
    public bool Detached { get; set; }

    /// <summary>Position inside a manually sorted series.</summary>
    public int? Order { get; set; }
}

public sealed class CollectionData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>normal | watchLater</summary>
    public string Kind { get; set; } = "normal";

    /// <summary>Ordered video keys ("yt:&lt;id&gt;" or "item:&lt;guid&gt;").</summary>
    public List<string> VideoKeys { get; set; } = new();
}

public sealed class UserState
{
    public HashSet<string> FavoriteChannels { get; set; } = new();

    public List<CollectionData> Collections { get; set; } = new();

    public HashSet<string> DismissedSuggestions { get; set; } = new();
}

/// <summary>Youfin-owned state only (things Jellyfin does not store). Schema version 2.</summary>
public sealed class YoufinState
{
    public const int CurrentVersion = 2;

    public int SchemaVersion { get; set; } = CurrentVersion;

    public Dictionary<string, ChannelConfig> Channels { get; set; } = new();

    public Dictionary<string, SeriesData> Series { get; set; } = new();

    public Dictionary<string, VideoOverride> Overrides { get; set; } = new();

    /// <summary>Per Jellyfin user id (format "N").</summary>
    public Dictionary<string, UserState> Users { get; set; } = new();

    public IReadOnlyDictionary<string, string> MergeMap() =>
        Channels.Where(c => !string.IsNullOrEmpty(c.Value.MergedInto)).ToDictionary(c => c.Key, c => c.Value.MergedInto!);

    public IReadOnlyDictionary<string, string> AliasMap() =>
        Channels.Where(c => !string.IsNullOrWhiteSpace(c.Value.Alias)).ToDictionary(c => c.Key, c => c.Value.Alias!);

    public UserState User(string userId)
    {
        if (!Users.TryGetValue(userId, out var u))
        {
            Users[userId] = u = new UserState();
        }

        return u;
    }

    public ChannelConfig Channel(string key)
    {
        if (!Channels.TryGetValue(key, out var c))
        {
            Channels[key] = c = new ChannelConfig();
        }

        return c;
    }

    /// <summary>Drops empty channel entries so the file only holds real deviations.</summary>
    public void Prune()
    {
        foreach (var k in Channels.Where(c => string.IsNullOrWhiteSpace(c.Value.Alias) && string.IsNullOrEmpty(c.Value.MergedInto)).Select(c => c.Key).ToList())
        {
            Channels.Remove(k);
        }

        foreach (var k in Overrides.Where(o => o.Value.SeriesId is null && !o.Value.Detached).Select(o => o.Key).ToList())
        {
            Overrides.Remove(k);
        }
    }
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

    /// <summary>Applies several changes and writes the file once.</summary>
    public void Update(Action<YoufinState> change)
    {
        lock (_lock)
        {
            change(State);
            State.Prune();
            Save();
        }
    }

    private void Save()
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(State));
        File.Move(tmp, _file, overwrite: true);
    }

    private YoufinState Load()
    {
        if (!File.Exists(_file))
        {
            return new();
        }

        string text;
        try
        {
            text = File.ReadAllText(_file);
        }
        catch (IOException)
        {
            return new();
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var version = doc.RootElement.TryGetProperty("SchemaVersion", out var v) && v.TryGetInt32(out var n) ? n : 1;
            if (version >= YoufinState.CurrentVersion)
            {
                return JsonSerializer.Deserialize<YoufinState>(text) ?? new();
            }

            var migrated = StateMigration.FromV1(doc.RootElement);
            Backup(".v1.bak", text);
            File.WriteAllText(_file, JsonSerializer.Serialize(migrated));
            return migrated;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Backup(".corrupt", text);
            return new();
        }
    }

    /// <summary>Keeps the original once, so a bad migration or corrupt file never destroys user data.</summary>
    private void Backup(string suffix, string content)
    {
        try
        {
            var path = _file + suffix;
            if (!File.Exists(path))
            {
                File.WriteAllText(path, content);
            }
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Version 1 (Phase 4) to version 2.</summary>
public static class StateMigration
{
    public static YoufinState FromV1(JsonElement root)
    {
        var s = new YoufinState();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return s;
        }

        foreach (var kv in Obj(root, "ChannelAliases"))
        {
            if (kv.Value.ValueKind == JsonValueKind.String)
            {
                s.Channel(kv.Name).Alias = kv.Value.GetString();
            }
        }

        foreach (var kv in Obj(root, "ChannelMerges"))
        {
            if (kv.Value.ValueKind == JsonValueKind.String)
            {
                s.Channel(kv.Name).MergedInto = kv.Value.GetString();
            }
        }

        foreach (var kv in Obj(root, "FavoriteChannels"))
        {
            if (kv.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in kv.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String))
                {
                    s.User(kv.Name).FavoriteChannels.Add(c.GetString()!);
                }
            }
        }

        foreach (var kv in Obj(root, "Playlists"))
        {
            if (kv.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var p in kv.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
            {
                var c = new CollectionData
                {
                    Id = p.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : Guid.NewGuid().ToString("N"),
                    Name = p.TryGetProperty("Name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString()! : "Playlist",
                };
                if (p.TryGetProperty("VideoIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                {
                    c.VideoKeys.AddRange(ids.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String && Guid.TryParse(x.GetString(), out _))
                        .Select(x => "item:" + Guid.Parse(x.GetString()!).ToString("N")));
                }

                s.User(kv.Name).Collections.Add(c);
            }
        }

        return s;
    }

    private static IEnumerable<JsonProperty> Obj(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Object ? e.EnumerateObject() : Enumerable.Empty<JsonProperty>();
}
