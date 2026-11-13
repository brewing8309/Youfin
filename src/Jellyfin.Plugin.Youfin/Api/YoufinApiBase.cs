using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Jellyfin.Plugin.Youfin.Core;
using Jellyfin.Plugin.Youfin.Data;
using Jellyfin.Plugin.Youfin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Youfin.Api;

[ApiController]
[Authorize]
[Route("Youfin")]
[Produces("application/json")]
public abstract class YoufinApiBase : ControllerBase
{
    protected const string AdminPolicy = "RequiresElevation";
    public const string WatchLaterId = "watchlater";

    protected YoufinApiBase(IndexService svc) => Svc = svc;

    protected IndexService Svc { get; }

    protected Guid UserId => Guid.TryParse(User.FindFirstValue("Jellyfin-UserId"), out var g) ? g : Guid.Empty;

    protected string UserKey => UserId.ToString("N");

    protected List<CollectionData> MyCollections() => Svc.Store.State.Users.GetValueOrDefault(UserKey)?.Collections ?? new();

    /// <summary>Finds a collection of the current user; "watchlater" is virtual until the first video is added.</summary>
    protected CollectionData? FindCollection(string id) => MyCollections().FirstOrDefault(c => c.Id == id)
        ?? (id == WatchLaterId ? new CollectionData { Id = WatchLaterId, Name = "Später ansehen", Kind = "watchLater" } : null);

    /// <summary>Resolves requested video ids to stable keys; unknown ids are ignored.</summary>
    protected List<VideoRecord> Resolve(IEnumerable<Guid>? ids) =>
        (ids ?? []).Distinct().Select(Svc.Index.Get).OfType<VideoRecord>().ToList();
}

public sealed record VideoIdsBody(List<Guid>? VideoIds);
