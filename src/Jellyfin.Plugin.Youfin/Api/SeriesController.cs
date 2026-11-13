using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Youfin.Core;
using Jellyfin.Plugin.Youfin.Data;
using Jellyfin.Plugin.Youfin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Youfin.Api;

/// <summary>Series = curated, ordered, exclusive groups (library structure, so changes need admin rights).</summary>
public class SeriesController : YoufinApiBase
{
    private static readonly string[] Sorts = ["published", "manual", "title"];

    public SeriesController(IndexService svc) : base(svc)
    {
    }

    [HttpGet("Series")]
    public ActionResult<Page<SeriesSummary>> List([FromQuery] string? search, [FromQuery] int startIndex = 0, [FromQuery] int limit = 50) =>
        Svc.Index.SeriesList(search, startIndex, limit);

    [HttpGet("Series/{id}")]
    public ActionResult<SeriesSummary> Get([FromRoute] string id) =>
        Svc.Index.SeriesList(null, 0, 200).Items.FirstOrDefault(s => s.Id == id) is { } s ? s : NotFound();

    /// <summary>Creates a series. With autoChannelKey, all current and future videos of that channel join it.</summary>
    [HttpPost("Series")]
    [Authorize(Policy = AdminPolicy)]
    public ActionResult<SeriesSummary> Create([FromBody] SeriesBody body)
    {
        var name = body.Name?.Trim();
        if (string.IsNullOrEmpty(name) && body.AutoChannelKey is not null)
        {
            name = Svc.Index.ChannelName(body.AutoChannelKey);
        }

        if (string.IsNullOrEmpty(name))
        {
            return BadRequest("Name required");
        }

        if (body.Sort is not null && !Sorts.Contains(body.Sort))
        {
            return BadRequest("Unknown sort");
        }

        if (body.AutoChannelKey is not null && Svc.Store.State.Series.Values.Any(x => x.AutoChannelKey == body.AutoChannelKey))
        {
            return Conflict("This channel already has a series");
        }

        var id = Guid.NewGuid().ToString("N");
        Svc.Store.Update(st =>
        {
            st.Series[id] = new SeriesData { Name = name, Sort = body.Sort ?? "published", AutoChannelKey = body.AutoChannelKey };
            Assign(st, id, Resolve(body.VideoIds));
        });
        Svc.Reapply();
        return Get(id);
    }

    [HttpPut("Series/{id}")]
    [Authorize(Policy = AdminPolicy)]
    public IActionResult Update([FromRoute] string id, [FromBody] SeriesBody body)
    {
        if (!Svc.Store.State.Series.ContainsKey(id))
        {
            return NotFound();
        }

        if (body.Sort is not null && !Sorts.Contains(body.Sort))
        {
            return BadRequest("Unknown sort");
        }

        Svc.Store.Update(st =>
        {
            var s = st.Series[id];
            if (!string.IsNullOrWhiteSpace(body.Name)) { s.Name = body.Name.Trim(); }
            if (body.Sort is not null) { s.Sort = body.Sort; }
            if (body.AutoChannelKey is not null) { s.AutoChannelKey = body.AutoChannelKey == "" ? null : body.AutoChannelKey; }
        });
        Svc.Reapply();
        return NoContent();
    }

    /// <summary>Deletes the series only; its videos become standalone again (or follow another rule).</summary>
    [HttpDelete("Series/{id}")]
    [Authorize(Policy = AdminPolicy)]
    public IActionResult Delete([FromRoute] string id)
    {
        Svc.Store.Update(st =>
        {
            st.Series.Remove(id);
            foreach (var o in st.Overrides.Values.Where(o => o.SeriesId == id))
            {
                o.SeriesId = null;
            }
        });
        Svc.Reapply();
        return NoContent();
    }

    /// <summary>Assigns videos to the series (moves them if they were in another one).</summary>
    [HttpPost("Series/{id}/Videos")]
    [Authorize(Policy = AdminPolicy)]
    public IActionResult AddVideos([FromRoute] string id, [FromBody] VideoIdsBody body)
    {
        if (!Svc.Store.State.Series.ContainsKey(id))
        {
            return NotFound();
        }

        var vids = Resolve(body.VideoIds);
        Svc.Store.Update(st => Assign(st, id, vids));
        Svc.Reapply();
        return NoContent();
    }

    /// <summary>Series order: sets the series to manual sorting with exactly this sequence.</summary>
    [HttpPut("Series/{id}/Order")]
    [Authorize(Policy = AdminPolicy)]
    public IActionResult Order([FromRoute] string id, [FromBody] VideoIdsBody body)
    {
        if (!Svc.Store.State.Series.ContainsKey(id))
        {
            return NotFound();
        }

        var vids = Resolve(body.VideoIds).Where(v => Svc.Index.SeriesOf(v.ItemId) == id).ToList();
        Svc.Store.Update(st =>
        {
            st.Series[id].Sort = "manual";
            for (var i = 0; i < vids.Count; i++)
            {
                var o = st.Overrides.GetValueOrDefault(vids[i].VideoKey) ?? (st.Overrides[vids[i].VideoKey] = new VideoOverride());
                o.SeriesId = id;
                o.Detached = false;
                o.Order = i;
            }
        });
        Svc.Reapply();
        return NoContent();
    }

    /// <summary>Marks videos as standalone: removes them from any series, even one with a channel rule.</summary>
    [HttpPost("Videos/Single")]
    [Authorize(Policy = AdminPolicy)]
    public IActionResult MakeSingle([FromBody] VideoIdsBody body)
    {
        var vids = Resolve(body.VideoIds);
        Svc.Store.Update(st =>
        {
            foreach (var v in vids)
            {
                var o = st.Overrides.GetValueOrDefault(v.VideoKey) ?? (st.Overrides[v.VideoKey] = new VideoOverride());
                o.SeriesId = null;
                o.Order = null;
                o.Detached = true;
            }
        });
        Svc.Reapply();
        return NoContent();
    }

    /// <summary>Removes manual placement so the default (channel rule or standalone) applies again.</summary>
    [HttpPost("Videos/ResetPlacement")]
    [Authorize(Policy = AdminPolicy)]
    public IActionResult Reset([FromBody] VideoIdsBody body)
    {
        var vids = Resolve(body.VideoIds);
        Svc.Store.Update(st =>
        {
            foreach (var v in vids)
            {
                st.Overrides.Remove(v.VideoKey);
                st.Overrides.Remove("item:" + v.ItemId.ToString("N"));
            }
        });
        Svc.Reapply();
        return NoContent();
    }

    private static void Assign(YoufinState st, string seriesId, IEnumerable<VideoRecord> vids)
    {
        foreach (var v in vids)
        {
            var o = st.Overrides.GetValueOrDefault(v.VideoKey) ?? (st.Overrides[v.VideoKey] = new VideoOverride());
            o.SeriesId = seriesId;
            o.Detached = false;
            o.Order = null;
        }
    }
}

public sealed record SeriesBody(string? Name, string? Sort, string? AutoChannelKey, List<Guid>? VideoIds);
