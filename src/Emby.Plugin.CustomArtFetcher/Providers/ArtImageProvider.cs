namespace Emby.Plugin.CustomArtFetcher.Providers
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using Emby.Plugin.CustomArtFetcher.Model;

    using MediaBrowser.Common.Net;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Entities.TV;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Configuration;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Logging;
    using MediaBrowser.Model.Net;
    using MediaBrowser.Model.Providers;

    /// <summary>
    /// Offers Emby images built from the user's URL templates. Emby discovers this by interface —
    /// there is no registration step. Its position relative to the other fetchers is set per library
    /// under Library → Advanced → Image Fetchers.
    /// </summary>
    public class ArtImageProvider : IRemoteImageProvider
    {
        /// <summary>
        /// Shown in the image fetcher list and stored in each library's configuration, so it must
        /// stay stable across releases.
        /// </summary>
        public const string ProviderName = "Custom Art Fetcher";

        private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);
        private static readonly ImageType[] SupportedImages = { ImageType.Primary };

        /// <summary>
        /// Remembers URLs that just failed their existence check, so a full library refresh does not
        /// hammer an endpoint with requests that are already known to fail.
        /// </summary>
        private readonly ConcurrentDictionary<string, DateTime> recentFailures =
            new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        /// <summary>
        /// Fetcher configurations already reported, so the ranking is logged once per library setup
        /// rather than once per item.
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> loggedFetcherConfigurations =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        private readonly IHttpClient httpClient;
        private readonly ILogger logger;
        private readonly ImageUrlProbe probe;

        public ArtImageProvider(IHttpClient httpClient, ILogManager logManager)
        {
            this.httpClient = httpClient;
            this.logger = logManager.GetLogger(ProviderName);
            this.probe = new ImageUrlProbe(httpClient);
        }

        public string Name => ProviderName;

        public bool Supports(BaseItem item)
        {
            var options = GetOptions();
            if (options == null)
            {
                return false;
            }

            if (item is Movie)
            {
                return options.EnableForMovies;
            }

            if (item is Series)
            {
                return options.EnableForSeries;
            }

            return false;
        }

        /// <summary>
        /// Emby uses this both to decide what to ask for during a refresh and to build the
        /// "Change image" dialog, so it has to follow the tick boxes rather than report a fixed set.
        /// </summary>
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            var options = GetOptions();
            if (options == null)
            {
                return SupportedImages;
            }

            return GetConfiguredImages(options).Select(image => image.Type).ToArray();
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(
            BaseItem item,
            LibraryOptions libraryOptions,
            CancellationToken cancellationToken)
        {
            var none = Enumerable.Empty<RemoteImageInfo>();

            var options = GetOptions();
            if (options == null)
            {
                return none;
            }

            var configured = GetConfiguredImages(options);
            if (configured.Count == 0)
            {
                return none;
            }

            this.LogFetcherRanking(item, libraryOptions, configured);

            var rank = DescribeRank(item, libraryOptions);
            var typeOptions = libraryOptions?.GetTypeOptions(item.GetType().Name);

            var offered = new List<RemoteImageInfo>(configured.Count);

            foreach (var image in configured)
            {
                // Emby discards a candidate of a type the library has switched off, so there is no
                // point resolving the URL, let alone spending a request checking it.
                if (typeOptions != null && !typeOptions.IsEnabled(image.Type))
                {
                    continue;
                }

                string url;
                string unresolvedToken;
                if (!PlaceholderCatalog.TryResolve(image.Template, item, out url, out unresolvedToken))
                {
                    this.Log(
                        options,
                        rank,
                        "Skipping the {0} for \"{1}\": no value for placeholder {{{2}}}.",
                        image.Type,
                        item.Name,
                        unresolvedToken);
                    continue;
                }

                this.Log(options, rank, "Resolved {0} URL for \"{1}\": {2}", image.Type, item.Name, url);

                if (options.VerifyBeforeOffering && !await this.ImageExists(url, options, rank, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                this.Log(options, rank, "Offering the {0} for \"{1}\": {2}", image.Type, item.Name, url);

                offered.Add(new RemoteImageInfo
                {
                    ProviderName = ProviderName,
                    Url = url,
                    Type = image.Type,
                });
            }

            return offered;
        }

        /// <summary>
        /// Called by Emby to download the bytes, both during a metadata refresh and when a user picks
        /// the image from the "Change image" dialog.
        /// </summary>
        public async Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var options = GetOptions();
            var timer = Stopwatch.StartNew();

            this.Log(options, null, "GET {0}", url);

            try
            {
                var response = await this.httpClient.GetResponse(new HttpRequestOptions
                {
                    Url = url,
                    CancellationToken = cancellationToken,
                    BufferContent = false,
                }).ConfigureAwait(false);

                this.Log(
                    options,
                    null,
                    "GET {0} → {1} {2} ({3} ms)",
                    url,
                    (int)response.StatusCode,
                    response.ContentType ?? "no content type",
                    timer.ElapsedMilliseconds);

                return response;
            }
            catch (Exception ex)
            {
                this.Log(options, null, "GET {0} failed after {1} ms: {2}", url, timer.ElapsedMilliseconds, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// The image types to offer right now: each type whose tick box is on and whose URL is
        /// filled in, poster first. A type with an empty URL is treated as switched off, so a
        /// half-finished setting never produces requests.
        /// </summary>
        private static List<ConfiguredImage> GetConfiguredImages(PluginOptions options)
        {
            var configured = new List<ConfiguredImage>(4);

            AddConfiguredImage(configured, ImageType.Primary, options.EnablePoster, options.UrlTemplate);
            AddConfiguredImage(configured, ImageType.Backdrop, options.EnableBackdrop, options.BackdropUrlTemplate);
            AddConfiguredImage(configured, ImageType.Thumb, options.EnableThumb, options.ThumbUrlTemplate);
            AddConfiguredImage(configured, ImageType.Logo, options.EnableLogo, options.LogoUrlTemplate);

            return configured;
        }

        private static void AddConfiguredImage(List<ConfiguredImage> configured, ImageType type, bool enabled, string template)
        {
            if (enabled && !string.IsNullOrWhiteSpace(template))
            {
                configured.Add(new ConfiguredImage(type, template));
            }
        }

        private static PluginOptions GetOptions()
        {
            var plugin = CustomArtFetcherPlugin.Instance;
            return plugin?.GetPluginOptions();
        }

        /// <summary>
        /// Whether the URL is worth offering. The HTTP work itself lives in <see cref="ImageUrlProbe"/>;
        /// what stays here is the policy the fetcher needs and the settings page must not have — the
        /// short memory of recent misses, and a log line for each decision.
        /// </summary>
        private async Task<bool> ImageExists(string url, PluginOptions options, string rank, CancellationToken cancellationToken)
        {
            DateTime failedAt;
            if (this.recentFailures.TryGetValue(url, out failedAt))
            {
                if (DateTime.UtcNow - failedAt < FailureCacheDuration)
                {
                    this.Log(
                        options,
                        rank,
                        "Not requesting {0}: it failed {1:0} s ago and is cached as a miss for {2:0} s.",
                        url,
                        (DateTime.UtcNow - failedAt).TotalSeconds,
                        FailureCacheDuration.TotalSeconds);
                    return false;
                }

                this.recentFailures.TryRemove(url, out failedAt);
            }

            this.Log(options, rank, "HEAD {0} (timeout {1} s)", url, Math.Max(1, options.TimeoutSeconds));

            var result = await this.probe.Probe(url, options.TimeoutSeconds, cancellationToken).ConfigureAwait(false);

            switch (result.Outcome)
            {
                case ImageProbeOutcome.Image:
                    this.Log(options, rank, "HEAD {0} \u2192 {1}", url, result.Describe());
                    return true;

                case ImageProbeOutcome.NotAnImage:
                    this.Log(options, rank, "HEAD {0} \u2192 {1}", url, result.Describe());
                    this.Log(options, rank, "Not offering {0}: response was {1}, not an image.", url, result.ContentType);
                    this.RememberFailure(url);
                    return false;

                case ImageProbeOutcome.HeadNotSupported:
                    // Give the server the benefit of the doubt — Emby will simply get nothing when
                    // it goes on to GET the image.
                    this.Log(
                        options,
                        rank,
                        "HEAD {0} \u2192 {1} after {2} ms; the server does not support HEAD, offering the image unchecked.",
                        url,
                        result.StatusCode,
                        result.ElapsedMs);
                    return true;

                default:
                    this.Log(
                        options,
                        rank,
                        "Not offering {0}: request failed after {1} ms: {2}",
                        url,
                        result.ElapsedMs,
                        result.Error);
                    this.RememberFailure(url);
                    return false;
            }
        }

        /// <summary>
        /// The fetcher list a library actually ranks by — the explicit order when one is set, and
        /// otherwise the enabled list — together with this plugin's index in it (-1 when absent).
        /// </summary>
        private static void GetRanking(string[] order, string[] enabled, out string[] ranking, out int position)
        {
            ranking = order.Length > 0 ? order : enabled;
            position = Array.FindIndex(
                ranking,
                name => string.Equals(name, ProviderName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A short "#2/5 outranked" tag for the per-item log lines. <see cref="LogFetcherRanking"/>
        /// explains the ranking in full, but only once per library configuration, so during a
        /// library-wide refresh it scrolls out of sight long before the lines someone is actually
        /// reading. Repeating the position on every line keeps the usual cause of "the plugin logs
        /// that it offered an image but the image never changes" visible where the problem is seen.
        /// </summary>
        private static string DescribeRank(BaseItem item, LibraryOptions libraryOptions)
        {
            var typeOptions = libraryOptions?.GetTypeOptions(item.GetType().Name);

            string[] ranking;
            int position;
            GetRanking(
                typeOptions?.ImageFetcherOrder ?? new string[0],
                typeOptions?.ImageFetchers ?? new string[0],
                out ranking,
                out position);

            if (ranking.Length == 0)
            {
                return "default fetcher order";
            }

            if (position < 0)
            {
                return "unranked, so last of " + ranking.Length;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "#{0}/{1} {2}",
                position + 1,
                ranking.Length,
                position == 0 ? "wins" : "outranked");
        }

        /// <summary>
        /// Reports where this plugin sits in the library's image fetcher ranking. Emby asks every
        /// enabled fetcher for candidates and then saves the image from the highest-ranked one, so a
        /// plugin that is offering images but being ignored is nearly always ranked below TMDb —
        /// which is invisible from this plugin's own log lines. Logged once per distinct
        /// configuration, at Info, because it is the first thing to check when nothing changes.
        /// </summary>
        private void LogFetcherRanking(BaseItem item, LibraryOptions libraryOptions, List<ConfiguredImage> configured)
        {
            var typeName = item.GetType().Name;
            var typeOptions = libraryOptions?.GetTypeOptions(typeName);

            var order = typeOptions?.ImageFetcherOrder ?? new string[0];
            var enabled = typeOptions?.ImageFetchers ?? new string[0];

            // Only report a given library configuration once per server run.
            var signature = typeName + "|" + string.Join(">", order) + "|" + string.Join(",", enabled)
                            + "|" + string.Join(",", configured.Select(image => image.Type.ToString()));
            if (!this.loggedFetcherConfigurations.TryAdd(signature, 0))
            {
                return;
            }

            string[] ranking;
            int position;
            GetRanking(order, enabled, out ranking, out position);

            if (ranking.Length == 0)
            {
                this.logger.Info(
                    "Image fetchers for {0} in this library are at their defaults, so the ranking is Emby's own. "
                    + "If images offered here are ignored, set the order under Library → Advanced → Image Fetchers.",
                    typeName);
            }
            else if (position < 0)
            {
                this.logger.Info(
                    "Image fetchers for {0}: {1}. \"{2}\" is not in that list, so Emby ranks it last and a "
                    + "higher fetcher's image wins. Tick it and drag it to the top under "
                    + "Library → Advanced → Image Fetchers. (A fetcher is listed under the name it had when "
                    + "you configured it, so a renamed plugin has to be re-ticked.)",
                    typeName,
                    string.Join(", ", ranking),
                    ProviderName);
            }
            else if (position > 0)
            {
                this.logger.Info(
                    "Image fetchers for {0}: {1}. \"{2}\" is #{3} of {4}, so the images it offers are only used "
                    + "when every fetcher above it returns none. Drag it to the top under "
                    + "Library → Advanced → Image Fetchers to have it win.",
                    typeName,
                    string.Join(", ", ranking),
                    ProviderName,
                    position + 1,
                    ranking.Length);
            }
            else
            {
                this.logger.Info(
                    "Image fetchers for {0}: {1}. \"{2}\" is first, so its images win.",
                    typeName,
                    string.Join(", ", ranking),
                    ProviderName);
            }

            if (typeOptions == null)
            {
                return;
            }

            // A type switched on here but off for the library produces nothing, and the settings page
            // cannot see the library's configuration to warn about it — so say so once, here.
            var disabled = configured
                .Where(image => !typeOptions.IsEnabled(image.Type))
                .Select(image => image.Type.ToString())
                .ToArray();

            if (disabled.Length > 0)
            {
                this.logger.Info(
                    "These image types are switched on in the plugin but turned off for {0} in this library, so "
                    + "nothing will be saved for them no matter which fetcher offers one: {1}. Turn them on under "
                    + "Library \u2192 Advanced \u2192 Images.",
                    typeName,
                    string.Join(", ", disabled));
            }
        }

        /// <summary>
        /// Writes one line about a request or a decision. Always at Debug, so Emby's debug logging
        /// shows everything; also at Info when the user has ticked "Log every request", so the
        /// requests can be followed in the normal log without turning debug logging on server-wide.
        /// </summary>
        /// <param name="rank">
        /// Where this plugin sits in the library's image fetcher ranking, prefixed to the line.
        /// Null for lines with no library context, such as the image download itself.
        /// </param>
        private void Log(PluginOptions options, string rank, string message, params object[] args)
        {
            // Prefixed rather than passed as an argument: the caller's message is a format string
            // and the rank tag is already formatted, and it carries no braces of its own.
            var line = rank == null ? message : "[" + rank + "] " + message;

            this.logger.Debug(line, args);

            if (options != null && options.LogRequests)
            {
                this.logger.Info(line, args);
            }
        }

        private static bool IsMethodNotSupported(HttpException ex)
        {
            return ex.StatusCode == HttpStatusCode.MethodNotAllowed
                   || ex.StatusCode == HttpStatusCode.NotImplemented;
        }

        private void RememberFailure(string url)
        {
            // Keep the cache from growing without bound on a large library of misses.
            if (this.recentFailures.Count > 1000)
            {
                this.recentFailures.Clear();
            }

            this.recentFailures[url] = DateTime.UtcNow;
        }

        /// <summary>One image type the user has switched on, paired with the URL to build for it.</summary>
        private sealed class ConfiguredImage
        {
            public ConfiguredImage(ImageType type, string template)
            {
                this.Type = type;
                this.Template = template;
            }

            public ImageType Type { get; }

            public string Template { get; }
        }
    }
}
