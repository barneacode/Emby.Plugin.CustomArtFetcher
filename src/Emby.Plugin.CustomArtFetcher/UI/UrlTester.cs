namespace Emby.Plugin.CustomArtFetcher.UI
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    using Emby.Plugin.CustomArtFetcher.Model;
    using Emby.Plugin.CustomArtFetcher.Providers;
    using Emby.Web.GenericEdit.Elements;

    using MediaBrowser.Common.Net;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Querying;

    /// <summary>
    /// Runs one Test button: takes a real item out of the library, builds the URL the fetcher would
    /// build for it, asks the server for it, and describes what came back.
    /// </summary>
    /// <remarks>
    /// Real items rather than invented sample ids, because the question a user is really asking is
    /// "does this work for my library" — a made-up id would 404 against an art server that only
    /// hosts what the user owns, reporting a fault that is not there.
    /// </remarks>
    public class UrlTester
    {
        /// <summary>
        /// How many items to look at before giving up. A template can reference an id that most of
        /// the library lacks, so the first item is not necessarily testable.
        /// </summary>
        private const int MaxCandidates = 50;

        /// <summary>
        /// How many recently tested items to keep out of the next draw, per image type. Enough that
        /// repeated clicks work through a variety of items, small enough that a modest library does
        /// not run out of things to offer.
        /// </summary>
        private const int RecentMemory = 10;

        private readonly object recentLock = new object();

        private readonly Dictionary<ImageType, List<long>> recentlyTested =
            new Dictionary<ImageType, List<long>>();

        private readonly ILibraryManager libraryManager;
        private readonly ImageUrlProbe probe;

        public UrlTester(ILibraryManager libraryManager, IHttpClient httpClient)
        {
            this.libraryManager = libraryManager;
            this.probe = new ImageUrlProbe(httpClient);
        }

        /// <summary>
        /// Tests the template configured for <paramref name="type"/> and writes the verdict into
        /// <paramref name="status"/>.
        /// </summary>
        public async Task Test(PluginOptions options, ImageType type, string template, StatusItem status, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                Set(status, ItemStatus.Warning, "Enter a URL first.");
                return;
            }

            var itemTypes = new List<string>();
            if (options.EnableForMovies)
            {
                itemTypes.Add("Movie");
            }

            if (options.EnableForSeries)
            {
                itemTypes.Add("Series");
            }

            if (itemTypes.Count == 0)
            {
                Set(status, ItemStatus.Warning, "Turn on movies or series under Behavior first.");
                return;
            }

            // Draw at random and skip what the last few clicks already used, so pressing Test
            // repeatedly works through the library instead of asking about one item over and over.
            var pick = this.PickItem(itemTypes.ToArray(), template, this.GetRecent(type));

            if (pick.Item == null && pick.Examined == 0)
            {
                // Nothing left once the recent ones were excluded. Forget them and draw again, so a
                // small library cycles rather than running out.
                this.ForgetRecent(type);
                pick = this.PickItem(itemTypes.ToArray(), template, new long[0]);
            }

            if (pick.Examined == 0)
            {
                Set(status, ItemStatus.Unavailable, "No " + string.Join(" or ", itemTypes.ToArray()).ToLowerInvariant() + " found in the library to test with.");
                return;
            }

            if (pick.Item == null)
            {
                Set(
                    status,
                    ItemStatus.Warning,
                    "Checked " + pick.Examined + " item(s) and none had a value for {" + pick.MissingToken
                    + "}, so the URL could not be built. Nothing is wrong with the URL itself.");
                return;
            }

            this.Remember(type, pick.Item.InternalId);

            var url = pick.Url;
            var label = Describe(pick.Item);

            var result = await this.probe.Probe(url, options.TimeoutSeconds, cancellationToken).ConfigureAwait(false);

            switch (result.Outcome)
            {
                case ImageProbeOutcome.Image:
                    Set(status, ItemStatus.Succeeded, "OK — " + result.Describe() + " for " + label + ": " + url);
                    break;

                case ImageProbeOutcome.NotAnImage:
                    Set(
                        status,
                        ItemStatus.Failed,
                        "The URL answered with " + result.ContentType + ", not an image — " + label + ": " + url);
                    break;

                case ImageProbeOutcome.HeadNotSupported:
                    Set(
                        status,
                        ItemStatus.Warning,
                        "The server refused a HEAD request (" + result.StatusCode + "), so the URL could not be checked. "
                        + "The fetcher will offer it unchecked — " + label + ": " + url);
                    break;

                default:
                    Set(
                        status,
                        ItemStatus.Failed,
                        "No image — " + (result.StatusCode > 0 ? result.StatusCode + ": " : string.Empty)
                        + result.Error + " — " + label + ": " + url);
                    break;
            }
        }

        /// <summary>
        /// Draws up to <see cref="MaxCandidates"/> items at random, skipping <paramref name="exclude"/>,
        /// and returns the first whose placeholders all resolve.
        /// </summary>
        private Pick PickItem(string[] itemTypes, string template, long[] exclude)
        {
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = itemTypes,
                Recursive = true,
                IsVirtualItem = false,
                Limit = MaxCandidates,
                OrderBy = new[] { new ValueTuple<string, SortOrder>(ItemSortBy.Random, SortOrder.Ascending) },
            };

            if (exclude.Length > 0)
            {
                query.ExcludeItemIds = exclude;
            }

            var candidates = this.libraryManager.GetItemList(query) ?? new BaseItem[0];

            var pick = new Pick { Examined = candidates.Length };

            foreach (var candidate in candidates)
            {
                string resolved;
                string missing;
                if (PlaceholderCatalog.TryResolve(template, candidate, out resolved, out missing))
                {
                    pick.Item = candidate;
                    pick.Url = resolved;
                    return pick;
                }

                pick.MissingToken = missing;
            }

            return pick;
        }

        private long[] GetRecent(ImageType type)
        {
            lock (this.recentLock)
            {
                List<long> ids;
                return this.recentlyTested.TryGetValue(type, out ids) ? ids.ToArray() : new long[0];
            }
        }

        private void Remember(ImageType type, long itemId)
        {
            lock (this.recentLock)
            {
                List<long> ids;
                if (!this.recentlyTested.TryGetValue(type, out ids))
                {
                    ids = new List<long>(RecentMemory);
                    this.recentlyTested[type] = ids;
                }

                ids.Remove(itemId);
                ids.Add(itemId);

                while (ids.Count > RecentMemory)
                {
                    ids.RemoveAt(0);
                }
            }
        }

        private void ForgetRecent(ImageType type)
        {
            lock (this.recentLock)
            {
                this.recentlyTested.Remove(type);
            }
        }

        private static string Describe(BaseItem item)
        {
            return item.ProductionYear.HasValue
                ? "\"" + item.Name + "\" (" + item.ProductionYear.Value + ")"
                : "\"" + item.Name + "\"";
        }

        private static void Set(StatusItem status, ItemStatus state, string text)
        {
            status.Status = state;
            status.StatusText = text;
        }

        /// <summary>The item a test settled on, or what stopped it from settling on one.</summary>
        private sealed class Pick
        {
            public BaseItem Item { get; set; }

            public string Url { get; set; }

            /// <summary>The placeholder that could not be filled in for the last item tried.</summary>
            public string MissingToken { get; set; }

            /// <summary>How many items the draw returned, regardless of whether any resolved.</summary>
            public int Examined { get; set; }
        }
    }
}
