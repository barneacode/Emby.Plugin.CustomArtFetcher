namespace Emby.Plugin.CustomArtFetcher.Model
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Model.Entities;

    /// <summary>
    /// The single source of truth for the placeholders that may appear in the URL template.
    /// Both the settings page help text and the URL resolver are built from this list, so the
    /// documented tokens and the supported tokens can never drift apart.
    /// </summary>
    public static class PlaceholderCatalog
    {
        private const string RawPrefix = "raw:";
        private const string ProviderPrefix = "provider:";

        private static readonly Regex TokenPattern = new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

        private static readonly Placeholder[] Placeholders =
        {
            new Placeholder("imdb_id", "IMDb id of the item, e.g. tt0133093", item => item.GetProviderId(MetadataProviders.Imdb)),
            new Placeholder("tmdb_id", "TheMovieDb id of the item", item => item.GetProviderId(MetadataProviders.Tmdb)),
            new Placeholder("tvdb_id", "TheTVDB id of the item", item => item.GetProviderId(MetadataProviders.Tvdb)),
            new Placeholder("tvmaze_id", "TVmaze id of the item", item => item.GetProviderId(MetadataProviders.TvMaze)),
            new Placeholder("tvrage_id", "TVRage id of the item", item => item.GetProviderId(MetadataProviders.TvRage)),
            new Placeholder("item_type", "Movie or Series", item => item.GetType().Name),
            new Placeholder("name", "Title as Emby knows it", item => item.Name),
            new Placeholder("original_title", "Original-language title, when known", item => item.OriginalTitle),
            // Convert.ToString handles both int and int? here, yielding string.Empty when unset.
            new Placeholder("year", "Production year", item => Convert.ToString(item.ProductionYear, CultureInfo.InvariantCulture)),
        };

        private static readonly Dictionary<string, Placeholder> Lookup =
            Placeholders.ToDictionary(p => p.Token, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Substitutes every placeholder in <paramref name="template"/> with the matching value
        /// from <paramref name="item"/>.
        /// </summary>
        /// <returns>
        /// <c>true</c> when every placeholder resolved to a non-empty value. Otherwise <c>false</c>,
        /// with <paramref name="unresolvedToken"/> naming the first token that could not be filled
        /// in — the caller should then offer no image rather than request a malformed URL.
        /// </returns>
        public static bool TryResolve(string template, BaseItem item, out string url, out string unresolvedToken)
        {
            url = null;
            unresolvedToken = null;

            if (string.IsNullOrWhiteSpace(template))
            {
                return false;
            }

            string missing = null;

            var resolved = TokenPattern.Replace(template.Trim(), match =>
            {
                if (missing != null)
                {
                    return string.Empty;
                }

                var token = match.Groups[1].Value.Trim();

                var raw = token.StartsWith(RawPrefix, StringComparison.OrdinalIgnoreCase);
                if (raw)
                {
                    token = token.Substring(RawPrefix.Length).Trim();
                }

                var value = ResolveToken(token, item);

                if (string.IsNullOrEmpty(value))
                {
                    missing = match.Groups[1].Value.Trim();
                    return string.Empty;
                }

                return raw ? value : Uri.EscapeDataString(value);
            });

            if (missing != null)
            {
                unresolvedToken = missing;
                return false;
            }

            url = resolved;
            return true;
        }

        /// <summary>
        /// Renders the placeholder list shown on the plugin's settings page.
        /// </summary>
        public static string BuildHelpText()
        {
            var builder = new StringBuilder();

            foreach (var placeholder in Placeholders)
            {
                builder.Append('{').Append(placeholder.Token).Append("}  —  ").AppendLine(placeholder.Description);
            }

            builder.AppendLine("{provider:<name>}  —  any other provider id Emby holds for the item, by name");
            builder.AppendLine("{raw:<token>}  —  the same value without URL-encoding, e.g. {raw:name}");
            builder.Append("Placeholders are case-insensitive. If any placeholder in the URL is empty for an item ")
                   .Append("(for example {tmdb_id} on a movie that has no TMDb id), the item is skipped and its ")
                   .Append("existing image is left alone.");

            return builder.ToString();
        }

        /// <summary>
        /// Replaces every placeholder with a dummy value, so a template can be validated as a URL
        /// on the settings page without needing a real library item.
        /// </summary>
        public static string SubstituteSampleValues(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return template;
            }

            return TokenPattern.Replace(template.Trim(), _ => "sample");
        }

        public static bool ContainsPlaceholder(string template)
        {
            return !string.IsNullOrWhiteSpace(template) && TokenPattern.IsMatch(template);
        }

        private static string ResolveToken(string token, BaseItem item)
        {
            if (token.StartsWith(ProviderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var providerName = token.Substring(ProviderPrefix.Length).Trim();
                return string.IsNullOrEmpty(providerName) ? null : item.GetProviderId(providerName);
            }

            Placeholder placeholder;
            return Lookup.TryGetValue(token, out placeholder) ? placeholder.Resolve(item) : null;
        }

        private sealed class Placeholder
        {
            public Placeholder(string token, string description, Func<BaseItem, string> resolve)
            {
                this.Token = token;
                this.Description = description;
                this.Resolve = resolve;
            }

            public string Token { get; }

            public string Description { get; }

            public Func<BaseItem, string> Resolve { get; }
        }
    }
}
