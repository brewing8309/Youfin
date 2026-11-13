using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Youfin.Core;
using Jellyfin.Plugin.Youfin.Data;
using Xunit;

public class MigrationTests
{
    private const string V1 = """
    {"ChannelMerges":{"id:B":"id:A"},"ChannelAliases":{"id:A":"Alpha"},
     "FavoriteChannels":{"u1":["id:A"]},
     "Playlists":{"u1":[{"Id":"p1","Name":"GTA","VideoIds":["11111111-1111-1111-1111-111111111111","not-a-guid"]}]}}
    """;

    private static string Dir()
    {
        var d = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void MigratesV1AndKeepsBackup()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "state.json"), V1);
        var s = new UserDataStore(d).State;

        Assert.Equal(2, s.SchemaVersion);
        Assert.Equal("Alpha", s.AliasMap()["id:A"]);
        Assert.Equal("id:A", s.MergeMap()["id:B"]);
        Assert.Contains("id:A", s.Users["u1"].FavoriteChannels);
        var c = Assert.Single(s.Users["u1"].Collections);
        Assert.Equal("GTA", c.Name);
        Assert.Equal(["item:11111111111111111111111111111111"], c.VideoKeys);   // invalid id dropped, order kept
        Assert.Equal(V1, File.ReadAllText(Path.Combine(d, "state.json.v1.bak")));
    }

    [Fact]
    public void MigrationIsIdempotentAndDoesNotOverwriteBackup()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "state.json"), V1);
        var first = new UserDataStore(d).State;
        var second = new UserDataStore(d).State;   // loads the already migrated v2 file
        Assert.Equal(first.Users["u1"].Collections[0].Id, second.Users["u1"].Collections[0].Id);
        Assert.Equal(V1, File.ReadAllText(Path.Combine(d, "state.json.v1.bak")));
    }

    [Fact]
    public void EmptyOrMissingStateIsFine()
    {
        Assert.Equal(2, new UserDataStore(Dir()).State.SchemaVersion);
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "state.json"), "{}");
        Assert.Empty(new UserDataStore(d).State.Users);
    }

    [Fact]
    public void PruneDropsEmptyEntries()
    {
        var d = Dir();
        var store = new UserDataStore(d);
        store.Update(s => { s.Channel("x"); s.Overrides["yt:a"] = new VideoOverride(); s.Overrides["yt:b"] = new VideoOverride { Detached = true }; });
        Assert.Empty(store.State.Channels);
        Assert.Equal(["yt:b"], store.State.Overrides.Keys);
    }
}

public class VideoKeyTests
{
    private static VideoRecord V(Guid id, string? yt) =>
        new(id, yt, "t", null, null, DateTime.UtcNow, null, null, new ChannelKey("id:X", "X"), [], []);

    [Fact]
    public void KeyPrefersYouTubeId()
    {
        var id = Guid.NewGuid();
        Assert.Equal("yt:abc", V(id, "abc").VideoKey);
        Assert.Equal("item:" + id.ToString("N"), V(id, null).VideoKey);
    }

    [Fact]
    public void LegacyItemKeyResolvesToVideoWithYouTubeId()
    {
        var id = Guid.NewGuid();
        var idx = new VideoIndex([V(id, "abc")], new Dictionary<string, string>());
        Assert.Equal(id, idx.GetByKey("yt:abc")!.ItemId);
        Assert.Equal(id, idx.GetByKey("item:" + id.ToString("N"))!.ItemId);
        Assert.Null(idx.GetByKey("yt:none"));
        Assert.Null(idx.GetByKey("item:garbage"));
    }

    [Fact]
    public void ByKeysKeepsOrderAndSkipsUnknown()
    {
        var a = V(Guid.NewGuid(), "a"); var b = V(Guid.NewGuid(), "b");
        var idx = new VideoIndex([a, b], new Dictionary<string, string>());
        var p = idx.ByKeys(["yt:b", "yt:zzz", "yt:a"], 0, 10);
        Assert.Equal([b.ItemId, a.ItemId], p.Items.Select(v => v.ItemId));
    }
}
