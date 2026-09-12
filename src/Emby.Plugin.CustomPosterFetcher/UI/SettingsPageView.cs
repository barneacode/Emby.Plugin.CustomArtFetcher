namespace Emby.Plugin.CustomPosterFetcher.UI
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    using Emby.Plugin.CustomPosterFetcher.Model;
    using Emby.Web.GenericEdit.Elements;

    using MediaBrowser.Model.Dto;
    using MediaBrowser.Model.Events;
    using MediaBrowser.Model.GenericEdit;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Plugins.UI.Views;

    /// <summary>
    /// The settings page. This exists because BasePluginSimpleUI's own view answers
    /// <see cref="RunCommand"/> with null, so a button on the form would render and do nothing;
    /// everything else about that view is reproduced here.
    /// </summary>
    /// <remarks>
    /// Taking the page over also takes over the two hooks the base class routed through its own
    /// view — refreshing the placeholder help before the page is shown, and validating before a
    /// save. They are done here instead; the plugin's OnBeforeShowUI/OnOptionsSaving overrides are
    /// no longer reached.
    /// </remarks>
    public class SettingsPageView : IPluginPageView
    {
        /// <summary>
        /// A ceiling on how long a click can block, whatever the configured request timeout is —
        /// that one may be up to 60 s, which is far too long to sit on a button press.
        /// </summary>
        private const int TestTimeoutCeilingSeconds = 15;

        private readonly CustomPosterFetcherPlugin plugin;

        public SettingsPageView(CustomPosterFetcherPlugin plugin)
        {
            this.plugin = plugin;

            var options = plugin.LoadOptions();

            // Refreshed on every show, so a build that adds a placeholder documents it without the
            // user having to reset their settings.
            options.AvailablePlaceholders.Text = PlaceholderCatalog.BuildHelpText();

            // Test results are stored with the settings, so clear them rather than present a result
            // from a previous session as though it described the URL now in the box.
            options.ResetTestStatuses();

            this.ContentData = options;
            this.PluginId = CustomPosterFetcherPlugin.PluginId.ToString();
        }

        public event EventHandler<GenericEventArgs<IPluginUIView>> UIViewInfoChanged;

        public string Caption => this.ContentData.EditorTitle;

        public string SubCaption => this.ContentData.EditorDescription;

        public string PluginId { get; }

        public PluginOptions ContentData { get; set; }

        public UserDto User { get; set; }

        public string RedirectViewUrl { get; set; }

        public bool ShowSave { get; set; } = true;

        public bool ShowBack { get; set; }

        public bool AllowSave { get; set; } = true;

        public bool AllowBack { get; set; } = true;

        IEditableObject IPluginUIView.ContentData
        {
            get { return this.ContentData; }
            set { this.ContentData = value as PluginOptions; }
        }

        public bool IsCommandAllowed(string commandKey)
        {
            return true;
        }

        /// <summary>
        /// Handles the page's own buttons. The command id is the button's Data1 value.
        /// </summary>
        public async Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            var options = this.ContentData;

            switch (commandId)
            {
                case "TestPrimary":
                    await this.Test(ImageType.Primary, options.UrlTemplate, options.PosterStatus).ConfigureAwait(false);
                    return this;

                case "TestBackdrop":
                    await this.Test(ImageType.Backdrop, options.BackdropUrlTemplate, options.BackdropStatus).ConfigureAwait(false);
                    return this;

                case "TestThumb":
                    await this.Test(ImageType.Thumb, options.ThumbUrlTemplate, options.ThumbStatus).ConfigureAwait(false);
                    return this;

                case "TestLogo":
                    await this.Test(ImageType.Logo, options.LogoUrlTemplate, options.LogoStatus).ConfigureAwait(false);
                    return this;
            }

            // Anything unrecognised is one of the host's own commands (PageSave, PageBack).
            // Swallowing those here would break the page's Save button.
            return null;
        }

        /// <summary>
        /// Runs one Test button. The request is made here, in the server process, against a real
        /// item from the library — never from the browser showing this page.
        /// </summary>
        /// <remarks>
        /// The probe is awaited rather than pushed through UIViewInfoChanged: it is a single HEAD
        /// request under the user's own timeout, so the click simply takes that long, and the page
        /// re-renders with the result when this returns.
        /// </remarks>
        private async Task Test(ImageType type, string template, StatusItem status)
        {
            status.Status = ItemStatus.InProgress;
            status.StatusText = "Testing\u2026";

            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TestTimeoutCeilingSeconds)))
                {
                    await this.plugin.GetUrlTester()
                        .Test(this.ContentData, type, template, status, timeout.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                status.Status = ItemStatus.Failed;
                status.StatusText = "The test took longer than " + TestTimeoutCeilingSeconds + " s and was given up on.";
            }
            catch (Exception ex)
            {
                status.Status = ItemStatus.Failed;
                status.StatusText = "The test could not be run: " + ex.Message;
            }
        }

        public Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            this.ContentData.ValidateOrThrow();
            this.plugin.StoreOptions(this.ContentData);

            return Task.FromResult((IPluginUIView)this);
        }

        public Task Cancel()
        {
            return Task.CompletedTask;
        }

        public void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
        {
        }

        /// <summary>
        /// Pushes a re-render to the open page, for work that finishes after RunCommand returns.
        /// </summary>
        protected void NotifyViewChanged()
        {
            var handler = this.UIViewInfoChanged;
            if (handler != null)
            {
                handler(this, new GenericEventArgs<IPluginUIView>(this));
            }
        }
    }
}
