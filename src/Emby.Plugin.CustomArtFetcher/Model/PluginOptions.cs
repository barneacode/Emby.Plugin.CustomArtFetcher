namespace Emby.Plugin.CustomArtFetcher.Model
{
    using System;
    using System.ComponentModel;

    using Emby.Web.GenericEdit;
    using Emby.Web.GenericEdit.Elements;
    using Emby.Web.GenericEdit.Validation;

    using MediaBrowser.Model.Attributes;

    /// <summary>
    /// The plugin's settings. Emby renders the form on the plugin page directly from this class —
    /// property types pick the controls, the attributes below control the presentation.
    /// </summary>
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => "Custom Art Fetcher";

        public override string EditorDescription =>
            "Fetches images for movies and series from URLs of your own, filled in per item from the "
            + "metadata ids Emby holds for it.\n"
            + "Each library also has to enable \"Custom Art Fetcher\" under Advanced \u2192 Image Fetchers "
            + "(ranked above the others), and the image types under Advanced \u2192 Images.";

        /// <summary>
        /// The image types this plugin can offer. Emby's ImageType enum lists more, but only these
        /// four are fetchable for movies and series *and* on by default in a library — Art, Disc and
        /// Banner default to a limit of 0, so offering them would do nothing until the user raised
        /// that limit in the library's own settings.
        /// </summary>
        /// <summary>Separates the page description above from the first toggle.</summary>
        public SpacerItem SpacerTop { get; set; } = new SpacerItem();

        [DisplayName("Fetch Posters")]
        public bool EnablePoster { get; set; } = true;

        [DisplayName("Poster URL")]
        [VisibleCondition(nameof(EnablePoster), SimpleCondition.IsTrue)]
        public string UrlTemplate { get; set; }

        [VisibleCondition(nameof(EnablePoster), SimpleCondition.IsTrue)]
        public ButtonItem TestPosterButton { get; set; } =
            new ButtonItem("Test poster URL") { Icon = IconNames.image_search, Data1 = "TestPrimary" };

        [VisibleCondition(nameof(EnablePoster), SimpleCondition.IsTrue)]
        public StatusItem PosterStatus { get; set; } =
            new StatusItem("Poster URL", "Not tested yet.", ItemStatus.Unknown);

        [DisplayName("Fetch Backdrops")]
        public bool EnableBackdrop { get; set; } = false;

        [DisplayName("Backdrop URL")]
        [VisibleCondition(nameof(EnableBackdrop), SimpleCondition.IsTrue)]
        public string BackdropUrlTemplate { get; set; }

        [VisibleCondition(nameof(EnableBackdrop), SimpleCondition.IsTrue)]
        public ButtonItem TestBackdropButton { get; set; } =
            new ButtonItem("Test backdrop URL") { Icon = IconNames.image_search, Data1 = "TestBackdrop" };

        [VisibleCondition(nameof(EnableBackdrop), SimpleCondition.IsTrue)]
        public StatusItem BackdropStatus { get; set; } =
            new StatusItem("Backdrop URL", "Not tested yet.", ItemStatus.Unknown);

        [DisplayName("Fetch Thumbnails")]
        public bool EnableThumb { get; set; } = false;

        [DisplayName("Thumb URL")]
        [VisibleCondition(nameof(EnableThumb), SimpleCondition.IsTrue)]
        public string ThumbUrlTemplate { get; set; }

        [VisibleCondition(nameof(EnableThumb), SimpleCondition.IsTrue)]
        public ButtonItem TestThumbButton { get; set; } =
            new ButtonItem("Test thumb URL") { Icon = IconNames.image_search, Data1 = "TestThumb" };

        [VisibleCondition(nameof(EnableThumb), SimpleCondition.IsTrue)]
        public StatusItem ThumbStatus { get; set; } =
            new StatusItem("Thumb URL", "Not tested yet.", ItemStatus.Unknown);

        [DisplayName("Fetch Logos")]
        public bool EnableLogo { get; set; } = false;

        [DisplayName("Logo URL")]
        [VisibleCondition(nameof(EnableLogo), SimpleCondition.IsTrue)]
        public string LogoUrlTemplate { get; set; }

        [VisibleCondition(nameof(EnableLogo), SimpleCondition.IsTrue)]
        public ButtonItem TestLogoButton { get; set; } =
            new ButtonItem("Test logo URL") { Icon = IconNames.image_search, Data1 = "TestLogo" };

        [VisibleCondition(nameof(EnableLogo), SimpleCondition.IsTrue)]
        public StatusItem LogoStatus { get; set; } =
            new StatusItem("Logo URL", "Not tested yet.", ItemStatus.Unknown);

        public SpacerItem Spacer1 { get; set; } = new SpacerItem();

        public CaptionItem PlaceholderCaption { get; set; } = new CaptionItem("Available placeholders");

        /// <summary>
        /// Read-only help text. Refreshed from <see cref="PlaceholderCatalog"/> every time the page
        /// is shown, so it always describes the tokens this build actually supports.
        /// </summary>
        public LabelItem AvailablePlaceholders { get; set; } = new LabelItem(PlaceholderCatalog.BuildHelpText());

        public SpacerItem Spacer2 { get; set; } = new SpacerItem();

        public CaptionItem BehaviorCaption { get; set; } = new CaptionItem("Behavior");

        [DisplayName("Check that the image exists first")]
        [Description("Sends a HEAD request before offering an image to Emby, and skips it if the "
                     + "URL does not return an image. Turn this off if your server is slow or does not "
                     + "handle HEAD requests.")]
        public bool VerifyBeforeOffering { get; set; } = true;

        [DisplayName("Request timeout (seconds)")]
        [Description("How long to wait for that check before giving up.")]
        [MinValue(1)]
        [MaxValue(60)]
        [EnabledCondition(nameof(VerifyBeforeOffering), SimpleCondition.IsTrue)]
        public int TimeoutSeconds { get; set; } = 10;

        [DisplayName("Log every request")]
        [Description("Writes a line to the Emby log for each URL this plugin builds, each request it "
                     + "sends and the response it got back, under the \"Custom Art Fetcher\" logger. "
                     + "Leave this off for normal use — a full library refresh logs one line per item. "
                     + "These messages are always written at Debug level, so turning on Emby's debug "
                     + "logging shows them too.")]
        public bool LogRequests { get; set; } = false;

        /// <summary>
        /// Puts the four Test results back to their unrun state, and is called every time the page
        /// is opened.
        /// </summary>
        /// <remarks>
        /// The status items are stored alongside the real settings, so without this the page would
        /// present a result from an earlier session as though it were current. It also repairs
        /// settings written before these items carried a caption, which the web UI renders as
        /// "undefined".
        /// </remarks>
        public void ResetTestStatuses()
        {
            ResetStatus(this.PosterStatus, "Poster URL");
            ResetStatus(this.BackdropStatus, "Backdrop URL");
            ResetStatus(this.ThumbStatus, "Thumbnail URL");
            ResetStatus(this.LogoStatus, "Logo URL");
        }

        protected override void Validate(ValidationContext context)
        {
            base.Validate(context);

            // A blank URL never blocks saving: the page has to be storable half-finished, and an
            // image type left switched on with no URL simply fetches nothing (the provider skips
            // empty templates). Only a URL that has actually been typed is held to the format rules.
            this.UrlTemplate = ValidateTemplate(context, nameof(this.UrlTemplate), this.UrlTemplate, "poster");
            this.BackdropUrlTemplate = ValidateTemplate(context, nameof(this.BackdropUrlTemplate), this.BackdropUrlTemplate, "backdrop");
            this.ThumbUrlTemplate = ValidateTemplate(context, nameof(this.ThumbUrlTemplate), this.ThumbUrlTemplate, "thumb");
            this.LogoUrlTemplate = ValidateTemplate(context, nameof(this.LogoUrlTemplate), this.LogoUrlTemplate, "logo");
        }

        private static void ResetStatus(StatusItem status, string caption)
        {
            status.Caption = caption;
            status.StatusText = "Not tested yet.";
            status.Status = ItemStatus.Unknown;
        }

        /// <summary>
        /// Trims one URL template and, if anything was typed, checks it is an absolute http/https
        /// address once the placeholders are filled in. An empty template is left alone.
        /// </summary>
        /// <returns>The trimmed template, to be stored back on the property.</returns>
        private static string ValidateTemplate(
            ValidationContext context,
            string propertyName,
            string template,
            string label)
        {
            template = (template ?? string.Empty).Trim();

            if (template.Length == 0)
            {
                return template;
            }

            // The template itself ("https://host/{tmdb_id}.jpg") is not necessarily a well-formed URI,
            // so check the shape with the placeholders filled in with dummy values.
            Uri parsed;
            if (!Uri.TryCreate(PlaceholderCatalog.SubstituteSampleValues(template), UriKind.Absolute, out parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                context.AddValidationError(
                    propertyName,
                    "The " + label + " URL must be an absolute http:// or https:// address once its placeholders are filled in.");
                return template;
            }

            // A URL with no placeholders is allowed: serving one fixed image to everything is a
            // legitimate thing to want, so it is not this plugin's business to refuse it.
            return template;
        }
    }
}
