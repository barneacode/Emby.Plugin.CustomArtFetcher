namespace Emby.Plugin.CustomArtFetcher
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Emby.Plugin.CustomArtFetcher.Model;
    using Emby.Plugin.CustomArtFetcher.UI;

    using MediaBrowser.Common.Net;

    using MediaBrowser.Common.Plugins;
    using MediaBrowser.Controller;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Plugins;
    using MediaBrowser.Model.Drawing;
    using MediaBrowser.Model.Plugins;
    using MediaBrowser.Model.Plugins.UI;

    /// <summary>
    /// Re-declaring IHasUIPages is deliberate. BasePluginSimpleUI implements it explicitly, and an
    /// explicit implementation on a derived class takes over the interface mapping — so Emby asks
    /// this class for its pages and gets the one below, while the base class's options store stays
    /// available through the protected GetOptions/SaveOptions it also provides. The base's own page
    /// is left unused: its view answers every button press with null.
    /// </summary>
    public class CustomArtFetcherPlugin : BasePluginSimpleUI<PluginOptions>, IHasUIPages, IHasThumbImage
    {
        public const string PluginName = "Custom Art Fetcher";

        public const string PluginDescription =
            "Fetches posters, backdrops, thumbs and logos for movies and series from URL templates "
            + "you configure, filled in from the metadata ids Emby holds for each item.";

        /// <summary>Never change this — Emby identifies the plugin (and its stored config) by it.</summary>
        public static readonly Guid PluginId = new Guid("55CF4BD6-1FB9-4DBD-BAD6-44D67B6AE7D9");

        private readonly IServerApplicationHost appHost;
        private readonly SettingsPageController controller;
        private readonly object urlTesterLock = new object();

        private UrlTester urlTester;

        public CustomArtFetcherPlugin(IServerApplicationHost appHost)
            : base(appHost)
        {
            Instance = this;
            this.appHost = appHost;

            var pageInfo = new PluginPageInfo
            {
                Name = "Settings",
                DisplayName = PluginName,
                EnableInMainMenu = true,
                MenuIcon = "image",
                IsMainConfigPage = true,
            };

            this.controller = new SettingsPageController(this, pageInfo);
        }

        IReadOnlyCollection<IPluginUIPageController> IHasUIPages.UIPageControllers
        {
            get { return new IPluginUIPageController[] { this.controller }; }
        }

        /// <summary>
        /// The image provider is constructed by Emby's DI container and has no way to reach the
        /// plugin instance, so it reads the options through here.
        /// </summary>
        public static CustomArtFetcherPlugin Instance { get; private set; }

        public override Guid Id => PluginId;

        public override string Name => PluginName;

        public override string Description => PluginDescription;

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public PluginOptions GetPluginOptions()
        {
            return this.GetOptions();
        }

        /// <summary>Reads the stored settings for the page. Wraps the base class's protected store.</summary>
        internal PluginOptions LoadOptions()
        {
            return this.GetOptions();
        }

        /// <summary>Writes the settings back from the page. Wraps the base class's protected store.</summary>
        internal void StoreOptions(PluginOptions options)
        {
            this.SaveOptions(options);
        }

        /// <summary>
        /// The Test buttons' helper, built on first use rather than in the constructor, which runs
        /// while the server is still assembling its services. Kept afterwards because it remembers
        /// which items recent tests used, which is what makes each click pick a different one.
        /// </summary>
        internal UrlTester GetUrlTester()
        {
            lock (this.urlTesterLock)
            {
                if (this.urlTester == null)
                {
                    this.urlTester = new UrlTester(
                        this.appHost.Resolve<ILibraryManager>(),
                        this.appHost.Resolve<IHttpClient>());
                }

                return this.urlTester;
            }
        }

        public Stream GetThumbImage()
        {
            var type = this.GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.png");
        }

        /// <summary>
        /// Puts the settings page in the dashboard menu. BasePluginSimpleUI creates the page with
        /// EnableInMainMenu off, which leaves it reachable only through Dashboard -> Plugins;
        /// turning it on gives the plugin an entry of its own under Advanced, next to Plugins.
        /// </summary>
        /// <remarks>
        /// MenuSection is deliberately left unset: it moves the entry into one of the named
        /// dashboard groups (setting it to "server" lands it under Emby Server), and the default
        /// null is what puts a plugin under Advanced, where the other plugins live.
        /// Setting <c>pageInfo.Name</c> here has no effect either — the base class resets it to
        /// "Settings" as soon as this returns. <c>DisplayName</c> is the label that shows.
        /// </remarks>
        protected override void OnCreatePageInfo(PluginPageInfo pageInfo)
        {
            pageInfo.EnableInMainMenu = true;
            pageInfo.MenuIcon = "image";
            pageInfo.DisplayName = PluginName;

            base.OnCreatePageInfo(pageInfo);
        }
    }
}
