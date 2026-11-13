using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Youfin.Core;
using Jellyfin.Plugin.Youfin.Data;
using Xunit;

public class SeriesTests
{
    private static VideoRecord V(string yt, string channel, int day, string? title = null) =>
        new(Guid.NewGuid(), yt, title ?? yt, null, new DateTime(2024, 1, 1).AddDays(day), new DateTime(2025, 1, 1), null, null,
            new ChannelKey("id:" + channel, channel), [], []);

    private static VideoIndex Idx(VideoRecord[] vids, Dictionary<string, SeriesData>? series = null,
        Dictionary<string, VideoOverride>? ov = null, Dictionary<string, string>? merges = null) =>
        new(vids, new Dictionary<string, string>(), merges, series, ov);

    [Fact]
    public void WithoutSeriesEverythingIsStandalone()
    {
        var v = new[] { V("a", "A", 1), V("b", "A", 2) };
        var i = Idx(v);
        Assert.All(v, x => Assert.True(i.IsStandalone(x.ItemId)));
        Assert.Equal(2, i.Videos(null, VideoSort.Added, true, 0, 10, standalone: true).Total);
        Assert.Empty(i.SeriesList(null, 0, 10).Items);
    }

    [Fact]
    public void ChannelRuleAddsVideosAndOthersStayStandalone()
    {
        var v = new[] { V("a", "A", 1), V("b", "A", 2), V("c", "B", 3) };
        var i = Idx(v, new() { ["s1"] = new() { Name = "Harry", AutoChannelKey = "id:A" } });
        Assert.Equal("s1", i.SeriesOf(v[0].ItemId));
        Assert.Equal("s1", i.SeriesOf(v[1].ItemId));
        Assert.True(i.IsStandalone(v[2].ItemId));
        Assert.Equal(2, i.SeriesList(null, 0, 10).Items[0].VideoCount);
    }

    [Fact]
    public void ExplicitSeriesCanSpanChannelsAndBeatsRule()
    {
        var v = new[] { V("a", "A", 1), V("b", "B", 2) };
        var i = Idx(v,
            new() { ["rule"] = new() { Name = "Rule", AutoChannelKey = "id:A" }, ["live"] = new() { Name = "Live" } },
            new() { ["yt:a"] = new() { SeriesId = "live" }, ["yt:b"] = new() { SeriesId = "live" } });
        Assert.Equal("live", i.SeriesOf(v[0].ItemId));
        Assert.Equal("live", i.SeriesOf(v[1].ItemId));
    }

    [Fact]
    public void DetachedBeatsRuleAndExplicit()
    {
        var v = new[] { V("a", "A", 1), V("b", "A", 2) };
        var i = Idx(v, new() { ["s"] = new() { Name = "S", AutoChannelKey = "id:A" } },
            new() { ["yt:a"] = new() { Detached = true, SeriesId = "s" } });
        Assert.True(i.IsStandalone(v[0].ItemId));
        Assert.Equal("s", i.SeriesOf(v[1].ItemId));
    }

    [Fact]
    public void OverrideToDeletedSeriesFallsBackToStandalone()
    {
        var v = new[] { V("a", "A", 1) };
        var i = Idx(v, new(), new() { ["yt:a"] = new() { SeriesId = "gone" } });
        Assert.True(i.IsStandalone(v[0].ItemId));
    }

    [Fact]
    public void ItemKeyOverrideStillAppliesAfterVideoGetsYouTubeId()
    {
        var v = V("a", "A", 1);
        var i = Idx([v], new() { ["s"] = new() { Name = "S" } }, new() { ["item:" + v.ItemId.ToString("N")] = new() { SeriesId = "s" } });
        Assert.Equal("s", i.SeriesOf(v.ItemId));
    }

    [Fact]
    public void SeriesOrderPublishedManualTitle()
    {
        var v = new[] { V("a", "A", 3, "Z"), V("b", "A", 1, "Y"), V("c", "A", 2, "X") };
        List<string> Order(string sort, Dictionary<string, VideoOverride>? ov = null) =>
            Idx(v, new() { ["s"] = new() { Name = "S", AutoChannelKey = "id:A", Sort = sort } }, ov).OrderedSeries("s").Select(x => x.YouTubeId!).ToList();
        Assert.Equal(["b", "c", "a"], Order("published"));
        Assert.Equal(["c", "b", "a"], Order("title"));
        Assert.Equal(["a", "c", "b"], Order("manual", new() { ["yt:a"] = new() { SeriesId = "s", Order = 0 }, ["yt:c"] = new() { SeriesId = "s", Order = 1 } }));
    }

    [Fact]
    public void MergesApplyAsOverlayWithoutRescan()
    {
        var v = new[] { V("a", "A", 1), V("b", "B", 2) };
        var i = Idx(v, merges: new() { ["id:B"] = "id:A" });
        Assert.Equal(1, i.Channels(null, 0, 10).Total);
        Assert.Equal("id:B", v[1].Channel.Key);   // source record untouched
    }

    [Fact]
    public void NoVideoIsEverListedTwiceAcrossSeriesAndStandalone()
    {
        var v = Enumerable.Range(0, 60).Select(n => V("v" + n, "C" + n % 3, n)).ToArray();
        var i = Idx(v, new() { ["s1"] = new() { Name = "1", AutoChannelKey = "id:C0" }, ["s2"] = new() { Name = "2", AutoChannelKey = "id:C0" } },
            new() { ["yt:v1"] = new() { SeriesId = "s2" } });
        var inSeries = i.SeriesList(null, 0, 50).Items.SelectMany(s => i.OrderedSeries(s.Id)).Select(x => x.ItemId).ToList();
        Assert.Equal(inSeries.Count, inSeries.Distinct().Count());
        Assert.Equal(60, inSeries.Count + i.Videos(null, VideoSort.Added, true, 0, 200, standalone: true).Total);
        Assert.Equal(60, i.Videos(null, VideoSort.Added, true, 0, 200).Total);   // "Alle Videos" stays flat
    }

    [Fact]
    public void SeriesViewScalesToTwentyThousandVideos()
    {
        var v = Enumerable.Range(0, 20000).Select(n => V("id" + n.ToString("D9"), "C" + n % 300, n % 3000)).ToArray();
        var ov = Enumerable.Range(0, 2000).ToDictionary(n => "yt:id" + n.ToString("D9"), n => new VideoOverride { Detached = n % 2 == 0, SeriesId = n % 2 == 1 ? "x" : null });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var i = Idx(v, new() { ["x"] = new() { Name = "X" }, ["y"] = new() { Name = "Y", AutoChannelKey = "id:C5" } }, ov);
        Assert.True(i.Videos(null, VideoSort.Added, true, 0, 50, standalone: true).Total > 0);
        Assert.Equal(2, i.SeriesList(null, 0, 10).Total);
        Assert.True(sw.ElapsedMilliseconds < 3000, "took " + sw.ElapsedMilliseconds + "ms");
    }
}
