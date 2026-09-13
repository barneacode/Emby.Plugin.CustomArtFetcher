namespace Emby.Plugin.CustomArtFetcher.Providers
{
    using System;
    using System.Diagnostics;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using MediaBrowser.Common.Net;
    using MediaBrowser.Model.Net;

    /// <summary>
    /// Sends the HEAD request that decides whether a URL actually serves an image, and classifies
    /// the answer. Shared by the image fetcher, which caches misses and logs, and by the settings
    /// page's Test buttons, which must not do either — a test that reports a cached miss instead of
    /// asking the server is worse than no test at all. Neither policy lives here.
    /// </summary>
    public class ImageUrlProbe
    {
        private readonly IHttpClient httpClient;

        public ImageUrlProbe(IHttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        /// <summary>
        /// HEADs <paramref name="url"/> and reports what came back. Never throws for an ordinary
        /// failure — a refused connection or a 404 is a result, not an error. Cancellation is
        /// still propagated.
        /// </summary>
        public async Task<ImageProbeResult> Probe(string url, int timeoutSeconds, CancellationToken cancellationToken)
        {
            var requestOptions = new HttpRequestOptions
            {
                Url = url,
                CancellationToken = cancellationToken,
                TimeoutMs = Math.Max(1, timeoutSeconds) * 1000,
                BufferContent = false,

                // A missing image is an ordinary outcome here, not something to log as an error.
                LogErrors = false,
            };

            var timer = Stopwatch.StartNew();

            try
            {
                var response = await this.httpClient.SendAsync(requestOptions, "HEAD").ConfigureAwait(false);

                // A HEAD response carries no body, but release the stream if one came back anyway.
                response.Content?.Dispose();

                var contentType = response.ContentType ?? string.Empty;

                // Servers that answer HEAD without a content type still count as a hit; only an
                // explicitly non-image type rules the URL out.
                var isImage = contentType.Length == 0
                              || contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

                return new ImageProbeResult(
                    isImage ? ImageProbeOutcome.Image : ImageProbeOutcome.NotAnImage,
                    (int)response.StatusCode,
                    contentType,
                    timer.ElapsedMilliseconds,
                    null);
            }
            catch (HttpException ex) when (IsMethodNotSupported(ex))
            {
                // Some servers reject HEAD outright. That says nothing about the image either way.
                return new ImageProbeResult(
                    ImageProbeOutcome.HeadNotSupported,
                    (int)ex.StatusCode,
                    null,
                    timer.ElapsedMilliseconds,
                    null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ImageProbeResult(
                    ImageProbeOutcome.Failed,
                    ex is HttpException ? (int)((HttpException)ex).StatusCode : 0,
                    null,
                    timer.ElapsedMilliseconds,
                    ex.Message);
            }
        }

        private static bool IsMethodNotSupported(HttpException ex)
        {
            return ex.StatusCode == HttpStatusCode.MethodNotAllowed
                   || ex.StatusCode == HttpStatusCode.NotImplemented;
        }
    }

    public enum ImageProbeOutcome
    {
        /// <summary>The URL answered, and nothing about the answer says it is not an image.</summary>
        Image,

        /// <summary>The URL answered with a content type that is explicitly not an image.</summary>
        NotAnImage,

        /// <summary>The server refuses HEAD, so the URL could not be checked either way.</summary>
        HeadNotSupported,

        /// <summary>No usable answer: a 404, a refused connection, a timeout.</summary>
        Failed,
    }

    public sealed class ImageProbeResult
    {
        public ImageProbeResult(ImageProbeOutcome outcome, int statusCode, string contentType, long elapsedMs, string error)
        {
            this.Outcome = outcome;
            this.StatusCode = statusCode;
            this.ContentType = contentType;
            this.ElapsedMs = elapsedMs;
            this.Error = error;
        }

        public ImageProbeOutcome Outcome { get; }

        /// <summary>The HTTP status, or 0 when the request produced no response at all.</summary>
        public int StatusCode { get; }

        public string ContentType { get; }

        public long ElapsedMs { get; }

        /// <summary>The exception message, when <see cref="Outcome"/> is Failed.</summary>
        public string Error { get; }

        /// <summary>"200 image/jpeg (7 ms)" — the part of the outcome worth showing either way.</summary>
        public string Describe()
        {
            var status = this.StatusCode > 0
                ? this.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "no response";

            var type = string.IsNullOrEmpty(this.ContentType) ? "no content type" : this.ContentType;

            return status + " " + type + " (" + this.ElapsedMs + " ms)";
        }
    }
}
