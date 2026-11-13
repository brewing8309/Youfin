using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Youfin.Core;
using Jellyfin.Plugin.Youfin.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Youfin.Api;

[ApiController]
[Authorize]
[Route("Youfin")]
[Produces("application/json")]
public class YoufinController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider Mime = new();
    private readonly IndexService _svc;
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly IUserDataManager _userData;

    public YoufinController(IndexService svc, ILibraryManager library, IUserManager users, IUserDataManager userData)
    {
        _svc = svc;
        _library = library;
        _users = users;
        _userData = userData;
    }

    private Guid UserId => Guid.TryParse(User.FindFirstValue("Jellyfin-UserId"), out var g) ? g : Guid.Empty;

    [HttpGet("Channels")]
    public ActionResult<Page<ChannelSummary>> Channels([FromQuery] string? search, [FromQuery] int startIndex = 0, [FromQuery] int limit = 50) =>
        _svc.Index.Channels(search, startIndex, limit);

    /// <param name="filter">Resume, Recent (played, newest first), Favorites; omitted = all.</param>
    [HttpGet("Videos")]
    public ActionResult<Page<VideoDto>> Videos(
        [FromQuery] string? channel, [FromQuery] VideoSort sort = VideoSort.Added, [FromQuery] bool desc = true,
        [FromQuery] string? filter = null, [FromQuery] int startIndex = 0, [FromQuery] int limit = 50)
    {
        var index = _svc.Index;
        var user = _users.GetUserById(UserId);
        Page<VideoRecord> p;
        if (!string.IsNullOrEmpty(filter) && user is not null)
        {
            var q = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.MusicVideo, BaseItemKind.Video],
                Recursive = true,
            };
            switch (filter.ToLowerInvariant())
            {
                case "resume":
                    q.IsResumable = true;
                    q.OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)];
                    break;
                case "recent":
                    q.IsPlayed = true;
                    q.OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)];
                    break;
                case "favorites":
                    q.IsFavorite = true;
                    break;
                default:
                    return BadRequest("Unknown filter");
            }

            var recs = _library.GetItemList(q).Select(i => index.Get(i.Id)).OfType<VideoRecord>()
                .Where(v => channel is null || v.Channel.Key == channel).ToList();
            start(ref startIndex, ref limit);
            p = new Page<VideoRecord>(recs.Skip(startIndex).Take(limit).ToList(), recs.Count, startIndex);
        }
        else
        {
            p = index.Videos(channel, sort, desc, startIndex, limit);
        }

        return new Page<VideoDto>(p.Items.Select(v => ToDto(v, index, user)).ToList(), p.Total, p.StartIndex);

        static void start(ref int s, ref int l)
        {
            s = Math.Max(0, s);
            l = Math.Clamp(l, 1, 200);
        }
    }

    [HttpGet("Videos/{id}")]
    public ActionResult<VideoDto> Video([FromRoute] Guid id)
    {
        var index = _svc.Index;
        var v = index.Get(id);
        return v is null ? NotFound() : ToDto(v, index, _users.GetUserById(UserId));
    }

    private VideoDto ToDto(VideoRecord v, VideoIndex index, Jellyfin.Database.Implementations.Entities.User? user)
    {
        long? pos = null;
        var played = false;
        var fav = false;
        if (user is not null && _library.GetItemById(v.ItemId) is { } item)
        {
            var d = _userData.GetUserData(user, item);
            if (d is not null)
            {
                pos = d.PlaybackPositionTicks;
                played = d.Played;
                fav = d.IsFavorite;
            }
        }

        return VideoDto.From(v, index.ChannelName(v.Channel.Key), pos, played, fav);
    }

    /// <summary>Serves the embedded web UI (static files only; data calls are authorized).</summary>
    [HttpGet("Web/{**path}")]
    [AllowAnonymous]
    public IActionResult Web(string? path)
    {
        var name = string.IsNullOrEmpty(path) ? "index.html" : path;
        var res = typeof(Plugin).Namespace + ".Web.app." + name.Replace('/', '.');
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(res);
        if (stream is null || name.Contains("..", StringComparison.Ordinal))
        {
            return NotFound();
        }

        Mime.TryGetContentType(name, out var type);
        return File(stream, type ?? "application/octet-stream");
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
public sealed record VideoDto(Guid ItemId, string? YouTubeId, string Title, string ChannelKey, string ChannelName, DateTime? Published, DateTime Added, long? RuntimeTicks,
    long? PositionTicks, bool Played, bool IsFavorite)
{
    public static VideoDto From(VideoRecord v, string channelName, long? pos, bool played, bool fav) =>
        new(v.ItemId, v.YouTubeId, v.Title, v.Channel.Key, channelName, v.Published, v.Added, v.RuntimeTicks, pos, played, fav);
}
