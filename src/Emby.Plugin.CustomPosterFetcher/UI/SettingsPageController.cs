namespace Emby.Plugin.CustomPosterFetcher.UI
{
    using System.Threading;
    using System.Threading.Tasks;

    using MediaBrowser.Model.Plugins;
    using MediaBrowser.Model.Plugins.UI;
    using MediaBrowser.Model.Plugins.UI.Views;

    /// <summary>
    /// The single page this plugin contributes. It replaces the one BasePluginSimpleUI builds for
    /// itself, whose view answers every button press with null — see <see cref="SettingsPageView"/>.
    /// Emby ships no base class for <see cref="IPluginUIPageController"/>, only the interface, but
    /// it is three members, so there is nothing to inherit.
    /// </summary>
    public class SettingsPageController : IPluginUIPageController
    {
        private readonly CustomPosterFetcherPlugin plugin;

        public SettingsPageController(CustomPosterFetcherPlugin plugin, PluginPageInfo pageInfo)
        {
            this.plugin = plugin;
            this.PageInfo = pageInfo;
        }

        public PluginPageInfo PageInfo { get; }

        public Task Initialize(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public Task<IPluginUIView> CreateDefaultPageView()
        {
            return Task.FromResult((IPluginUIView)new SettingsPageView(this.plugin));
        }
    }
}
