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
            "Fetches poster (primary) images for movies and series from a URL of your own.\n"
            + "Write the URL with placeholders in curly braces — they are filled in per item from the "
            + "metadata ids Emby has for it.\n"
            + "After saving, enable \"Custom Poster Fetcher\" under Library → Advanced → Image Fetchers and drag it "
            + "to the position you want it to have relative to the other fetchers.";

        [DisplayName("Poster URL")]
        [Description("For example: https://art.example.com/posters/{tmdb_id}.jpg\n"
                     + "See the list of available placeholders below.")]
        [Required]
        [EditMultiline(2)]
        public string UrlTemplate { get; set; }

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
        [Description("Sends a HEAD request before offering the poster to Emby, and skips the item if the "
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

            var template = (this.UrlTemplate ?? string.Empty).Trim();
            this.UrlTemplate = template;

            if (template.Length == 0)
            {
                context.AddValidationError(nameof(this.UrlTemplate), "Please enter a poster URL.");
                return;
            }

            // The template itself ("https://host/{tmdb_id}.jpg") is not necessarily a well-formed URI,
            // so check the shape with the placeholders filled in with dummy values.
            Uri parsed;
            if (!Uri.TryCreate(PlaceholderCatalog.SubstituteSampleValues(template), UriKind.Absolute, out parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                context.AddValidationError(
                    nameof(this.UrlTemplate),
                    "The poster URL must be an absolute http:// or https:// address once its placeholders are filled in.");
                return;
            }

            if (!PlaceholderCatalog.ContainsPlaceholder(template))
            {
                context.AddValidationError(
                    nameof(this.UrlTemplate),
                    "The poster URL contains no placeholders, so every movie and series would get the same image. "
                    + "Add at least one placeholder, for example {tmdb_id}.");
            }
        }
    }
}
