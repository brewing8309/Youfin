using System.IO;
using Jellyfin.Plugin.Youfin.Data;
using Jellyfin.Plugin.Youfin.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Youfin;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton(sp => new UserDataStore(Plugin.Instance!.DataFolderPath));
        services.AddSingleton<LibraryScanner>();
        services.AddSingleton<IndexService>();
        services.AddHostedService(sp => sp.GetRequiredService<IndexService>());
    }
}
