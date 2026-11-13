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
        new UserDataStore(dir).Update(s => s.Channel("x").Alias = "y");
        Assert.Equal("y", new UserDataStore(dir).State.AliasMap()["x"]);
        File.WriteAllText(Path.Combine(dir, "state.json"), "{broken");
        Assert.Empty(new UserDataStore(dir).State.AliasMap());
        Assert.True(File.Exists(Path.Combine(dir, "state.json.corrupt")));
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

public class SearchTests
{
    private static VideoRecord V(string t, string ch, string? yt, string? ov, string[] tags, string[] cats) =>
        new(Guid.NewGuid(), yt, t, ov, null, DateTime.UtcNow, null, null, new ChannelKey("id:" + ch, ch), tags, cats);

    private static VideoIndex Idx() => new([
        V("Rust Tutorial", "Code", "aaaaaaaaaaa", "learn borrowing", ["rust"], ["Education"]),
        V("Gaming Night", "Play", "bbbbbbbbbbb", null, [], ["Gaming"]),
    ], new Dictionary<string, string>());

    [Theory]
    [InlineData("rust", 1)]
    [InlineData("play", 1)]            // channel
    [InlineData("borrowing", 1)]       // description
    [InlineData("bbbbbbbbbbb", 1)]     // youtube id
    [InlineData("rust education", 1)]  // all terms, tags + category
    [InlineData("rust gaming", 0)]
    [InlineData("zzz", 0)]
    public void SearchMatches(string q, int expected) =>
        Assert.Equal(expected, Idx().Videos(null, VideoSort.Title, false, 0, 50, search: q).Total);

    [Fact]
    public void CategoriesAndFilter()
    {
        var i = Idx();
        Assert.Equal(2, i.Categories().Count);
        Assert.Equal(1, i.Videos(null, VideoSort.Title, false, 0, 50, category: "gaming").Total);
    }

    [Fact]
    public void FavoriteChannelsFlagAndFilter()
    {
        var i = Idx();
        var fav = new HashSet<string> { "id:Code" };
        var all = i.Channels(null, 0, 10, fav);
        Assert.True(all.Items.First(c => c.Key == "id:Code").IsFavorite);
        Assert.Single(i.Channels(null, 0, 10, fav, onlyFavorites: true).Items);
    }
}

public class PerformanceTests
{
    [Fact]
    public void TwentyThousandVideosStayFast()
    {
        var rnd = new Random(1);
        var vids = Enumerable.Range(0, 20000).Select(i => new VideoRecord(Guid.NewGuid(), "id" + i.ToString("D9"), "Video " + i,
            "desc " + rnd.Next(), new DateTime(2020, 1, 1).AddHours(i), new DateTime(2025, 1, 1).AddMinutes(i), null, null,
            new ChannelKey("id:C" + i % 300, "Chan " + i % 300), ["t" + i % 50], ["Cat" + i % 10])).ToList();
        var idx = new VideoIndex(vids, new Dictionary<string, string>());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(300, idx.Channels(null, 0, 50).Total);
        Assert.Equal(20000, idx.Videos(null, VideoSort.Published, true, 0, 50).Total);
        Assert.True(idx.Videos(null, VideoSort.Title, false, 0, 50, search: "video 1999").Total > 0);
        Assert.Equal(10, idx.Categories().Count);
        Assert.True(sw.ElapsedMilliseconds < 2000, "took " + sw.ElapsedMilliseconds + "ms");
    }
}
