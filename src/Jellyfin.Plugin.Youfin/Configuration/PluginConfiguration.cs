using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Youfin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Create/maintain Jellyfin collections per channel for native apps.</summary>
    public bool SyncCollections { get; set; }
}
