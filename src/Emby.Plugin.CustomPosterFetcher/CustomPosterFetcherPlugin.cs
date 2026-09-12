namespace Emby.Plugin.CustomPosterFetcher
{
    using System;
    using System.IO;

    using Emby.Plugin.CustomPosterFetcher.Model;

    using MediaBrowser.Common.Plugins;
    using MediaBrowser.Controller;
    using MediaBrowser.Controller.Plugins;
    using MediaBrowser.Model.Drawing;
    using MediaBrowser.Model.Plugins;

    public class CustomPosterFetcherPlugin : BasePluginSimpleUI<PluginOptions>, IHasThumbImage
    {
        public const string PluginName = "Custom Poster Fetcher";

        public const string PluginDescription =
            "Fetches poster images for movies and series from a URL template you configure, "
            + "filled in from the metadata ids Emby holds for each item.";

        /// <summary>Never change this — Emby identifies the plugin (and its stored config) by it.</summary>
        public static readonly Guid PluginId = new Guid("6E1B4F2C-9B0A-4F6D-9E2B-2C1A7F3D8A54");

        public CustomPosterFetcherPlugin(IServerApplicationHost appHost)
            : base(appHost)
        {
            Instance = this;
        }

        /// <summary>
        /// The image provider is constructed by Emby's DI container and has no way to reach the
        /// plugin instance, so it reads the options through here.
        /// </summary>
        public static CustomPosterFetcherPlugin Instance { get; private set; }

        public override Guid Id => PluginId;

        public override string Name => PluginName;

        public override string Description => PluginDescription;

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public PluginOptions GetPluginOptions()
        {
            return this.GetOptions();
        }

        public Stream GetThumbImage()
        {
            var type = this.GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.png");
        }

        /// <summary>
        /// Refreshes the placeholder help text from the catalog every time the page is opened, so an
        /// upgrade that adds a placeholder shows it without the user having to reset their settings.
        /// </summary>
        protected override PluginOptions OnBeforeShowUI(PluginOptions options)
        {
            options.AvailablePlaceholders.Text = PlaceholderCatalog.BuildHelpText();

            return base.OnBeforeShowUI(options);
        }

        /// <summary>
        /// Runs <see cref="PluginOptions.Validate"/> and surfaces any problem as an error on the
        /// settings page instead of saving a URL that cannot work.
        /// </summary>
        protected override bool OnOptionsSaving(PluginOptions options)
        {
            options.ValidateOrThrow();

            return base.OnOptionsSaving(options);
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
