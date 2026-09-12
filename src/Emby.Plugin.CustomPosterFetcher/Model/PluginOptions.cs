namespace Emby.Plugin.CustomPosterFetcher.Model
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
        public override string EditorTitle => "Custom Poster Fetcher";

        public override string EditorDescription =>
            "Fetches images for movies and series from URLs of your own.\n"
            + "Write the URL with placeholders in curly braces — they are filled in per item from the "
            + "metadata ids Emby has for it.\n"
            + "After saving, enable \"Custom Poster Fetcher\" under Library → Advanced → Image Fetchers and drag it "
            + "to the position you want it to have relative to the other fetchers.\n"
            + "An image type also has to be enabled for the library itself, under "
            + "Library \u2192 Advanced \u2192 Images.";

        /// <summary>
        /// The image types this plugin can offer. Emby's ImageType enum lists more, but only these
        /// four are fetchable for movies and series *and* on by default in a library — Art, Disc and
        /// Banner default to a limit of 0, so offering them would do nothing until the user raised
        /// that limit in the library's own settings.
        /// </summary>
        [DisplayName("Fetch posters")]
        [Description("The main cover image.")]
        public bool EnablePoster { get; set; } = true;

        [DisplayName("Poster URL")]
        [Description("For example: https://art.example.com/posters/{tmdb_id}.jpg\n"
                     + "See the list of available placeholders below.")]
        [EditMultiline(2)]
        [EnabledCondition(nameof(EnablePoster), SimpleCondition.IsTrue)]
        public string UrlTemplate { get; set; }

        [DisplayName("Fetch backdrops")]
        [Description("The wide background art shown behind an item. Emby ignores backdrops narrower "
                     + "than 1280 px.")]
        public bool EnableBackdrop { get; set; } = false;

        [DisplayName("Backdrop URL")]
        [EditMultiline(2)]
        [EnabledCondition(nameof(EnableBackdrop), SimpleCondition.IsTrue)]
        public string BackdropUrlTemplate { get; set; }

        [DisplayName("Fetch thumbs")]
        [Description("The wide thumbnail used in some list and resume views.")]
        public bool EnableThumb { get; set; } = false;

        [DisplayName("Thumb URL")]
        [EditMultiline(2)]
        [EnabledCondition(nameof(EnableThumb), SimpleCondition.IsTrue)]
        public string ThumbUrlTemplate { get; set; }

        [DisplayName("Fetch logos")]
        [Description("The title treatment overlaid on the backdrop, usually a transparent PNG.")]
        public bool EnableLogo { get; set; } = false;

        [DisplayName("Logo URL")]
        [EditMultiline(2)]
        [EnabledCondition(nameof(EnableLogo), SimpleCondition.IsTrue)]
        public string LogoUrlTemplate { get; set; }

        public SpacerItem Spacer1 { get; set; } = new SpacerItem();

        public CaptionItem PlaceholderCaption { get; set; } = new CaptionItem("Available placeholders");

        /// <summary>
        /// Read-only help text. Refreshed from <see cref="PlaceholderCatalog"/> every time the page
        /// is shown, so it always describes the tokens this build actually supports.
        /// </summary>
        public LabelItem AvailablePlaceholders { get; set; } = new LabelItem(PlaceholderCatalog.BuildHelpText());

        public SpacerItem Spacer2 { get; set; } = new SpacerItem();

        public CaptionItem BehaviorCaption { get; set; } = new CaptionItem("Behavior");

        [DisplayName("Use for movies")]
        public bool EnableForMovies { get; set; } = true;

        [DisplayName("Use for series")]
        public bool EnableForSeries { get; set; } = true;

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
                     + "sends and the response it got back, under the \"Custom Poster Fetcher\" logger. "
                     + "Leave this off for normal use — a full library refresh logs one line per item. "
                     + "These messages are always written at Debug level, so turning on Emby's debug "
                     + "logging shows them too.")]
        public bool LogRequests { get; set; } = false;

        protected override void Validate(ValidationContext context)
        {
            base.Validate(context);

            // Each URL is only required once its own toggle is on, so an unfinished URL left behind
            // a switched-off type never blocks saving.
            if (!this.EnablePoster && !this.EnableBackdrop && !this.EnableThumb && !this.EnableLogo)
            {
                context.AddValidationError(
                    nameof(this.EnablePoster),
                    "No image types are switched on, so the plugin would fetch nothing. Switch on at least one.");
            }

            this.UrlTemplate = ValidateTemplate(context, nameof(this.UrlTemplate), this.UrlTemplate, this.EnablePoster, "poster");
            this.BackdropUrlTemplate = ValidateTemplate(context, nameof(this.BackdropUrlTemplate), this.BackdropUrlTemplate, this.EnableBackdrop, "backdrop");
            this.ThumbUrlTemplate = ValidateTemplate(context, nameof(this.ThumbUrlTemplate), this.ThumbUrlTemplate, this.EnableThumb, "thumb");
            this.LogoUrlTemplate = ValidateTemplate(context, nameof(this.LogoUrlTemplate), this.LogoUrlTemplate, this.EnableLogo, "logo");
        }

        /// <summary>
        /// Trims one URL template and, when its image type is switched on, holds it to the same
        /// three rules as the poster URL: present, absolute http/https once the placeholders are
        /// filled in, and carrying at least one placeholder.
        /// </summary>
        /// <returns>The trimmed template, to be stored back on the property.</returns>
        private static string ValidateTemplate(
            ValidationContext context,
            string propertyName,
            string template,
            bool required,
            string label)
        {
            template = (template ?? string.Empty).Trim();

            if (!required)
            {
                return template;
            }

            if (template.Length == 0)
            {
                context.AddValidationError(propertyName, "Please enter a " + label + " URL.");
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

            if (!PlaceholderCatalog.ContainsPlaceholder(template))
            {
                context.AddValidationError(
                    propertyName,
                    "The " + label + " URL contains no placeholders, so every movie and series would get the same image. "
                    + "Add at least one placeholder, for example {tmdb_id}.");
            }

            return template;
        }
    }
}
