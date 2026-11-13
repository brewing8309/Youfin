using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Youfin.Core;
using Jellyfin.Plugin.Youfin.Data;
using Xunit;

public class IndexTests
{
    private static VideoRecord V(string t, string ch, int day) =>
        new(Guid.NewGuid(), null, t, null, new DateTime(2024, 1, day), new DateTime(2025, 1, day), null, null,
            new ChannelKey("id:" + ch, ch), [], []);

    [Fact]
    public void ChannelsCountAndSearchAndAlias()
    {
        var idx = new VideoIndex([V("a", "A", 1), V("b", "A", 2), V("c", "B", 3)],
            new Dictionary<string, string> { ["id:B"] = "Zeta" });
        var p = idx.Channels(null, 0, 10);
        Assert.Equal(2, p.Total);
        Assert.Equal(2, p.Items.First(c => c.Key == "id:A").VideoCount);
        Assert.Single(idx.Channels("zet", 0, 10).Items);
    }

    [Fact]
    public void VideosSortAndPaginate()
    {
        var idx = new VideoIndex(Enumerable.Range(1, 25).Select(i => V("t" + i, "A", i)), new Dictionary<string, string>());
        var p = idx.Videos("id:A", VideoSort.Published, true, 20, 10);
        Assert.Equal(25, p.Total);
        Assert.Equal(5, p.Items.Count);
        Assert.Equal("t5", p.Items[0].Title);
        Assert.Empty(idx.Videos("id:none", VideoSort.Added, true, 0, 10).Items);
    }

    [Fact]
    public void StorePersistsAndSurvivesCorruption()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        new UserDataStore(dir).Update(s => s.ChannelAliases["x"] = "y");
        Assert.Equal("y", new UserDataStore(dir).State.ChannelAliases["x"]);
        File.WriteAllText(Path.Combine(dir, "state.json"), "{broken");
        Assert.Empty(new UserDataStore(dir).State.ChannelAliases);
    }
}

public class ChannelNameTests
{
    private static VideoRecord V(string name) =>
        new(Guid.NewGuid(), null, "t", null, null, DateTime.UtcNow, null, null, new ChannelKey("id:X", name), [], []);

    [Fact]
    public void UsesMostFrequentNameUnlessAliased()
    {
        var vids = new[] { V("Good"), V("Good"), V("Odd") };
        Assert.Equal("Good", new VideoIndex(vids, new Dictionary<string, string>()).ChannelName("id:X"));
        Assert.Equal("Mine", new VideoIndex(vids, new Dictionary<string, string> { ["id:X"] = "Mine" }).ChannelName("id:X"));
    }
}
