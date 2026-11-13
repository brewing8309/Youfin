using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Youfin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Youfin;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Youfin";

    public override Guid Id => Guid.Parse("6b1f3c5e-8a47-4d2b-9c0e-3f5a7d9e1b24");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "Youfin",
            EmbeddedResourcePath = GetType().Namespace + ".Web.index.html",
            EnableInMainMenu = true,
            MenuIcon = "smart_display"
        };
    }
}
