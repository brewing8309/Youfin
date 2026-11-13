using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Youfin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Mirror Youfin series as Jellyfin collections (BoxSets) so native apps can browse them.</summary>
    public bool SyncCollections { get; set; }

    /// <summary>Additionally mirror every channel with at least 2 videos as a collection.</summary>
    public bool SyncChannelCollections { get; set; }
}
