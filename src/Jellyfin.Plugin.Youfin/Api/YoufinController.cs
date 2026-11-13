using System;
using System.Linq;
using Jellyfin.Plugin.Youfin.Core;
using Jellyfin.Plugin.Youfin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Youfin.Api;

[ApiController]
[Authorize]
[Route("Youfin")]
[Produces("application/json")]
public class YoufinController : ControllerBase
{
    private readonly IndexService _svc;

    public YoufinController(IndexService svc) => _svc = svc;

    [HttpGet("Channels")]
    public ActionResult<Page<ChannelSummary>> Channels([FromQuery] string? search, [FromQuery] int startIndex = 0, [FromQuery] int limit = 50) =>
        _svc.Index.Channels(search, startIndex, limit);

    [HttpGet("Videos")]
    public ActionResult<Page<VideoDto>> Videos(
        [FromQuery] string? channel, [FromQuery] VideoSort sort = VideoSort.Added, [FromQuery] bool desc = true,
        [FromQuery] int startIndex = 0, [FromQuery] int limit = 50)
    {
        var p = _svc.Index.Videos(channel, sort, desc, startIndex, limit);
        return new Page<VideoDto>(p.Items.Select(v => VideoDto.From(v, _svc.Index.ChannelName(v.Channel.Key))).ToList(), p.Total, p.StartIndex);
    }

    [HttpPost("Rebuild")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult Rebuild()
    {
        _svc.Rebuild();
        return NoContent();
    }
}

/// <summary>Slim projection; clients fetch images/playback via standard Jellyfin endpoints using ItemId.</summary>
public sealed record VideoDto(Guid ItemId, string? YouTubeId, string Title, string ChannelKey, string ChannelName, DateTime? Published, DateTime Added, long? RuntimeTicks)
{
    public static VideoDto From(VideoRecord v, string channelName) =>
        new(v.ItemId, v.YouTubeId, v.Title, v.Channel.Key, channelName, v.Published, v.Added, v.RuntimeTicks);
}
