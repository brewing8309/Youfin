using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Youfin.Core;
using Jellyfin.Plugin.Youfin.Data;
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
    public ActionResult<Page<ChannelSummary>> Channels(
        [FromQuery] string? search, [FromQuery] bool favorites = false, [FromQuery] int startIndex = 0, [FromQuery] int limit = 50) =>
        _svc.Index.Channels(search, startIndex, limit, MyFavorites(), favorites);

    [HttpPost("Channels/{key}/Favorite")]
    public IActionResult AddFavorite([FromRoute] string key) => SetFavorite(key, true);

    [HttpDelete("Channels/{key}/Favorite")]
    public IActionResult RemoveFavorite([FromRoute] string key) => SetFavorite(key, false);

    /// <summary>Rename a channel for display only; empty name removes the alias. Original metadata is untouched.</summary>
    [HttpPut("Channels/{key}/Name")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult Rename([FromRoute] string key, [FromBody] NameBody body)
    {
        _svc.Store.Update(st =>
        {
            st.Channel(key).Alias = string.IsNullOrWhiteSpace(body.Name) ? null : body.Name.Trim();
        });
        _svc.Rebuild();
        return NoContent();
    }

    /// <summary>Merge channel <paramref name="key"/> into another channel.</summary>
    [HttpPost("Channels/{key}/Merge")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult Merge([FromRoute] string key, [FromBody] MergeBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Into) || body.Into == key)
        {
            return BadRequest("Invalid target");
        }

        _svc.Store.Update(st => st.Channel(key).MergedInto = body.Into);
        _svc.Rebuild();
        return NoContent();
    }

    [HttpDelete("Channels/{key}/Merge")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult Unmerge([FromRoute] string key)
    {
        _svc.Store.Update(st => st.Channel(key).MergedInto = null);
        _svc.Rebuild();
        return NoContent();
    }

    [HttpGet("Categories")]
    public ActionResult<IReadOnlyList<CategorySummary>> Categories() => Ok(_svc.Index.Categories());

    [HttpGet("Playlists")]
    public ActionResult<IEnumerable<PlaylistDto>> Playlists() =>
        Ok(Mine().Select(p => new PlaylistDto(p.Id, p.Name, p.VideoKeys.Count)));

    [HttpPost("Playlists")]
    public ActionResult<PlaylistDto> CreatePlaylist([FromBody] NameBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return BadRequest("Name required");
        }

        var pl = new CollectionData { Name = body.Name.Trim() };
        _svc.Store.Update(st => PlaylistsOf(st).Add(pl));
        return new PlaylistDto(pl.Id, pl.Name, 0);
    }

    [HttpDelete("Playlists/{id}")]
    public IActionResult DeletePlaylist([FromRoute] string id)
    {
        _svc.Store.Update(st => PlaylistsOf(st).RemoveAll(p => p.Id == id));
        return NoContent();
    }

    [HttpPost("Playlists/{id}/Videos/{videoId}")]
    public IActionResult AddToPlaylist([FromRoute] string id, [FromRoute] Guid videoId)
    {
        if (_svc.Index.Get(videoId) is null)
        {
            return NotFound();
        }

        var found = false;
        _svc.Store.Update(st =>
        {
            var pl = PlaylistsOf(st).FirstOrDefault(p => p.Id == id);
            if (pl is null) { return; }
            found = true;
            var vk = _svc.Index.Get(videoId)!.VideoKey;
            if (!pl.VideoKeys.Contains(vk)) { pl.VideoKeys.Add(vk); }
        });
        return found ? NoContent() : NotFound();
    }

    [HttpDelete("Playlists/{id}/Videos/{videoId}")]
    public IActionResult RemoveFromPlaylist([FromRoute] string id, [FromRoute] Guid videoId)
    {
        _svc.Store.Update(st => PlaylistsOf(st).FirstOrDefault(p => p.Id == id)?.VideoKeys.RemoveAll(k => _svc.Index.GetByKey(k)?.ItemId == videoId));
        return NoContent();
    }

    private ISet<string> MyFavorites() =>
        _svc.Store.State.Users.GetValueOrDefault(UserId.ToString("N"))?.FavoriteChannels ?? new HashSet<string>();

    private List<CollectionData> Mine() => _svc.Store.State.Users.GetValueOrDefault(UserId.ToString("N"))?.Collections ?? new();

    private List<CollectionData> PlaylistsOf(YoufinState st) => st.User(UserId.ToString("N")).Collections;

    private IActionResult SetFavorite(string key, bool on)
    {
        var k = UserId.ToString("N");
        _svc.Store.Update(st =>
        {
            var set = st.User(k).FavoriteChannels;
            if (on) { set.Add(key); } else { set.Remove(key); }
        });
        return NoContent();
    }

    /// <param name="filter">Resume, Recent (played, newest first), Favorites; omitted = all.</param>
    [HttpGet("Videos")]
    public ActionResult<Page<VideoDto>> Videos(
        [FromQuery] string? channel, [FromQuery] VideoSort sort = VideoSort.Added, [FromQuery] bool desc = true,
        [FromQuery] string? filter = null, [FromQuery] string? q = null, [FromQuery] string? category = null,
        [FromQuery] string? playlist = null, [FromQuery] int startIndex = 0, [FromQuery] int limit = 50)
    {
        var index = _svc.Index;
        var user = _users.GetUserById(UserId);
        Page<VideoRecord> p;
        if (!string.IsNullOrEmpty(playlist))
        {
            var pl = Mine().FirstOrDefault(x => x.Id == playlist);
            if (pl is null) { return NotFound(); }
            p = index.ByKeys(pl.VideoKeys, startIndex, limit);
        }
        else if (!string.IsNullOrEmpty(filter) && user is not null)
        {
            var iq = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.MusicVideo, BaseItemKind.Video],
                Recursive = true,
            };
            switch (filter.ToLowerInvariant())
            {
                case "resume":
                    iq.IsResumable = true;
                    iq.OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)];
                    break;
                case "recent":
                    iq.IsPlayed = true;
                    iq.OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)];
                    break;
                case "favorites":
                    iq.IsFavorite = true;
                    break;
                default:
                    return BadRequest("Unknown filter");
            }

            var recs = _library.GetItemList(iq).Select(i => index.Get(i.Id)).OfType<VideoRecord>()
                .Where(v => channel is null || v.Channel.Key == channel).ToList();
            start(ref startIndex, ref limit);
            p = new Page<VideoRecord>(recs.Skip(startIndex).Take(limit).ToList(), recs.Count, startIndex);
        }
        else
        {
            p = index.Videos(channel, sort, desc, startIndex, limit, q, category);
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
public sealed record NameBody(string? Name);

public sealed record MergeBody(string? Into);

public sealed record PlaylistDto(string Id, string Name, int VideoCount);

public sealed record VideoDto(Guid ItemId, string? YouTubeId, string Title, string ChannelKey, string ChannelName, DateTime? Published, DateTime Added, long? RuntimeTicks,
    long? PositionTicks, bool Played, bool IsFavorite)
{
    public static VideoDto From(VideoRecord v, string channelName, long? pos, bool played, bool fav) =>
        new(v.ItemId, v.YouTubeId, v.Title, v.Channel.Key, channelName, v.Published, v.Added, v.RuntimeTicks, pos, played, fav);
}
