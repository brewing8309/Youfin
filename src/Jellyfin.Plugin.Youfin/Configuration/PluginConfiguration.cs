using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Youfin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Mirror Youfin series as Jellyfin collections (BoxSets) so native apps can browse them.</summary>
    public bool SyncCollections { get; set; }

    /// <summary>Ids of the Jellyfin libraries Youfin should use. Empty = all libraries.</summary>
    public string[] IncludedLibraryIds { get; set; } = Array.Empty<string>();

    /// <summary>In the Jellyfin web client, show the Youfin interface when one of the chosen libraries is opened.</summary>
    public bool ReplaceLibraryView { get; set; } = true;

    /// <summary>Additionally mirror every channel with at least 2 videos as a collection.</summary>
    public bool SyncChannelCollections { get; set; }
}
