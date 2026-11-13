using System.Collections.Generic;
using Jellyfin.Plugin.Youfin.Core;
using Xunit;

public class CoreTests
{
    [Theory]
    [InlineData("/v/Title [dQw4w9WgXcQ].mkv", "dQw4w9WgXcQ")]
    [InlineData("/v/[abc] Title [dQw4w9WgXcQ].webm", "dQw4w9WgXcQ")]
    [InlineData("/v/no id.mkv", null)]
    [InlineData(null, null)]
    public void ParsesVideoId(string? path, string? expected) =>
        Assert.Equal(expected, YouTubeIdParser.ParseVideoId(path));

    [Fact]
    public void ParsesChannelId() =>
        Assert.Equal("UCuAXFkgsw1L7xaCfnd5JJOw", YouTubeIdParser.ParseChannelId("x [UCuAXFkgsw1L7xaCfnd5JJOw].jpg"));

    [Fact]
    public void InfoJsonParsesAndToleratesGarbage()
    {
        var i = InfoJsonReader.Parse("""{"channel_id":"UC1","uploader":"Up","tags":["a","b"],"categories":["Gaming"]}""");
        Assert.NotNull(i);
        Assert.Equal("UC1", i!.ChannelId);
        Assert.Equal("Up", i.Channel);
        Assert.Equal(2, i.Tags.Count);
        Assert.Null(InfoJsonReader.Parse("not json"));
        Assert.Null(InfoJsonReader.TryReadForVideo("/nonexistent/x.mkv"));
    }

    [Fact]
    public void ResolverPrefersPersonIdThenInfoThenName()
    {
        var info = new InfoJson("UCinfo", "InfoName", [], []);
        Assert.Equal("id:UCperson", ChannelResolver.Resolve(new("UCperson", "P", "UCs", "S", info, "F")).Key);
        Assert.Equal("id:UCinfo", ChannelResolver.Resolve(new(null, null, null, null, info, "F")).Key);
        Assert.Equal("name:folder", ChannelResolver.Resolve(new(null, null, null, null, null, "Folder")).Key);
        Assert.Equal(ChannelResolver.UnknownKey, ChannelResolver.Resolve(new(null, null, null, null, null, null)).Key);
    }

    [Fact]
    public void ResolverAppliesMergesWithoutLooping()
    {
        var merges = new Dictionary<string, string> { ["id:A"] = "id:B", ["id:B"] = "id:A" };
        var k = ChannelResolver.Resolve(new("A", "n", null, null, null, null), merges);
        Assert.Contains(k.Key, new[] { "id:A", "id:B" });
        var m2 = new Dictionary<string, string> { ["id:A"] = "id:B" };
        Assert.Equal("id:B", ChannelResolver.Resolve(new("A", "n", null, null, null, null), m2).Key);
    }
}
