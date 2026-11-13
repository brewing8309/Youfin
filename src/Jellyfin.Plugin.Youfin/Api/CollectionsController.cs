using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Youfin.Data;
using Jellyfin.Plugin.Youfin.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Youfin.Api;

/// <summary>Collections (formerly playlists): per user, free, ordered, a video may be in several. "Playlists" routes remain as aliases.</summary>
public class CollectionsController : YoufinApiBase
{
    public CollectionsController(IndexService svc) : base(svc)
    {
    }

    [HttpGet("Collections")]
    [HttpGet("Playlists")]
    public ActionResult<IEnumerable<PlaylistDto>> List()
    {
        var mine = MyCollections();
        var all = mine.ToList();
        if (all.All(c => c.Kind != "watchLater"))
        {
            all.Insert(0, FindCollection(WatchLaterId)!);
        }

        return Ok(all.OrderBy(c => c.Kind == "watchLater" ? 0 : 1)
            .Select(c => new PlaylistDto(c.Id, c.Name, c.VideoKeys.Count(k => Svc.Index.GetByKey(k) is not null), c.Kind)));
    }

    [HttpPost("Collections")]
    [HttpPost("Playlists")]
    public ActionResult<PlaylistDto> Create([FromBody] NameBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return BadRequest("Name required");
        }

        var c = new CollectionData { Name = body.Name.Trim() };
        Svc.Store.Update(st => st.User(UserKey).Collections.Add(c));
        return new PlaylistDto(c.Id, c.Name, 0, c.Kind);
    }

    [HttpPut("Collections/{id}")]
    public IActionResult Rename([FromRoute] string id, [FromBody] NameBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || id == WatchLaterId)
        {
            return BadRequest("Invalid");
        }

        var found = false;
        Svc.Store.Update(st =>
        {
            if (st.User(UserKey).Collections.FirstOrDefault(c => c.Id == id) is { Kind: "normal" } c) { c.Name = body.Name.Trim(); found = true; }
        });
        return found ? NoContent() : NotFound();
    }

    [HttpDelete("Collections/{id}")]
    [HttpDelete("Playlists/{id}")]
    public IActionResult Delete([FromRoute] string id)
    {
        Svc.Store.Update(st => st.User(UserKey).Collections.RemoveAll(c => c.Id == id && c.Kind == "normal"));
        return NoContent();
    }

    /// <summary>Appends videos (skips ones already in it). Creates "Später ansehen" on first use.</summary>
    [HttpPost("Collections/{id}/Videos")]
    public IActionResult AddVideos([FromRoute] string id, [FromBody] VideoIdsBody body)
    {
        if (FindCollection(id) is null)
        {
            return NotFound();
        }

        var keys = Resolve(body.VideoIds).Select(v => v.VideoKey).ToList();
        Svc.Store.Update(st =>
        {
            var list = st.User(UserKey).Collections;
            var c = list.FirstOrDefault(x => x.Id == id);
            if (c is null)
            {
                list.Add(c = new CollectionData { Id = WatchLaterId, Name = "Später ansehen", Kind = "watchLater" });
            }

            foreach (var k in keys.Where(k => !c.VideoKeys.Contains(k)))
            {
                c.VideoKeys.Add(k);
            }
        });
        return NoContent();
    }

    [HttpPost("Playlists/{id}/Videos/{videoId}")]
    public IActionResult AddOne([FromRoute] string id, [FromRoute] Guid videoId) => Svc.Index.Get(videoId) is null
        ? NotFound()
        : AddVideos(id, new VideoIdsBody([videoId]));

    [HttpDelete("Collections/{id}/Videos/{videoId}")]
    [HttpDelete("Playlists/{id}/Videos/{videoId}")]
    public IActionResult RemoveVideo([FromRoute] string id, [FromRoute] Guid videoId)
    {
        Svc.Store.Update(st => st.User(UserKey).Collections.FirstOrDefault(c => c.Id == id)
            ?.VideoKeys.RemoveAll(k => Svc.Index.GetByKey(k)?.ItemId == videoId));
        return NoContent();
    }

    /// <summary>Manual sorting: videos given here come first in this order, the rest keep their relative order after them.</summary>
    [HttpPut("Collections/{id}/Order")]
    public IActionResult Order([FromRoute] string id, [FromBody] VideoIdsBody body)
    {
        var found = false;
        var first = Resolve(body.VideoIds).Select(v => v.VideoKey).ToList();
        Svc.Store.Update(st =>
        {
            var c = st.User(UserKey).Collections.FirstOrDefault(x => x.Id == id);
            if (c is null) { return; }
            found = true;
            c.VideoKeys = first.Where(c.VideoKeys.Contains).Concat(c.VideoKeys.Where(k => !first.Contains(k))).ToList();
        });
        return found ? NoContent() : NotFound();
    }
}
