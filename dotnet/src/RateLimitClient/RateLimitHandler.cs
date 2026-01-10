using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RateLimitClient
{
    /// <summary>
    /// HttpMessageHandler that implements HTTP traffic shaping based on rate limit headers
    /// according to the IETF draft-ietf-httpapi-ratelimit-headers specification.
    /// </summary>
    public class RateLimitHandler : DelegatingHandler
    {
        private readonly RateLimitTracker _tracker;
        private readonly RateLimitHandlerOptions _options;

        /// <summary>
        /// Creates a new RateLimitHandler with default options.
        /// </summary>
        public RateLimitHandler() : this(new RateLimitHandlerOptions())
        {
        }

        /// <summary>
        /// Creates a new RateLimitHandler with the specified options.
        /// </summary>
        public RateLimitHandler(RateLimitHandlerOptions options) : base()
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _tracker = new RateLimitTracker(_options);
            InnerHandler = new HttpClientHandler();
        }

        /// <summary>
        /// Creates a new RateLimitHandler with the specified options and inner handler.
        /// </summary>
        public RateLimitHandler(RateLimitHandlerOptions options, HttpMessageHandler innerHandler) : base(innerHandler)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _tracker = new RateLimitTracker(_options);
        }

        /// <summary>
        /// Sends an HTTP request with rate limiting applied.
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request?.RequestUri == null)
            {
                return await base.SendAsync(request!, cancellationToken).ConfigureAwait(false);
            }

            // Wait for rate limit if necessary (before request)
            if (_options.WaitMode == RateLimitWaitMode.BeforeRequest)
            {
                await _tracker.WaitForRateLimitAsync(request.RequestUri, cancellationToken).ConfigureAwait(false);
            }

            // Send the request
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // Parse rate limit headers from response
            if (response?.Headers != null)
            {
                try
                {
                    var rateLimitHeaders = RateLimitHeaderParser.ParseHeaders(response.Headers);
                    _tracker.UpdateFromHeaders(rateLimitHeaders, request.RequestUri);

                    // Invoke callback if configured
                    _options.OnRateLimitHeadersReceived?.Invoke(request.RequestUri, rateLimitHeaders);
                }
                catch (Exception ex)
                {
                    // Don't fail the request if parsing fails
                    _options.OnParsingError?.Invoke(request.RequestUri, ex);
                }
            }

            // Wait for rate limit if necessary (after response)
            if (_options.WaitMode == RateLimitWaitMode.AfterResponse)
            {
                await _tracker.WaitForRateLimitAsync(request.RequestUri, cancellationToken).ConfigureAwait(false);
            }

            // Handle 429 status code
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _options.OnTooManyRequests?.Invoke(request, response);

                if (_options.AutoRetryOn429)
                {
                    // Calculate delay from response headers
                    var delay = _tracker.CalculateDelay(request.RequestUri);

                    if (delay > TimeSpan.Zero && delay <= _options.MaxDelayThreshold)
                    {
                        _options.OnDelayCalculated?.Invoke(request.RequestUri, delay);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                        // Retry the request
                        response.Dispose();
                        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            return response;
        }

        /// <summary>
        /// Gets the current rate limit tracker for diagnostics.
        /// </summary>
        public RateLimitTracker GetTracker() => _tracker;

        /// <summary>
        /// Clears all tracked rate limit state.
        /// </summary>
        public void ClearState() => _tracker.Clear();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tracker?.Clear();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Options for configuring the RateLimitHandler behavior.
    /// </summary>
    public class RateLimitHandlerOptions
    {
        /// <summary>
        /// When to apply rate limit delays. Default is BeforeRequest.
        /// </summary>
        public RateLimitWaitMode WaitMode { get; set; } = RateLimitWaitMode.BeforeRequest;

        /// <summary>
        /// Maximum delay threshold to prevent DoS attacks from excessively high values.
        /// Default is 5 minutes.
        /// </summary>
        public TimeSpan MaxDelayThreshold { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Enable proactive throttling when approaching rate limits.
        /// When enabled, requests will be gradually slowed as quota is consumed.
        /// Default is true.
        /// </summary>
        public bool EnableProactiveThrottling { get; set; } = true;

        /// <summary>
        /// Threshold (0.0 to 1.0) for triggering proactive throttling.
        /// Default is 0.8 (80% of quota consumed).
        /// </summary>
        public double ProactiveThrottleThreshold { get; set; } = 0.8;

        /// <summary>
        /// How long rate limit state remains valid without updates.
        /// Default is 1 hour.
        /// </summary>
        public TimeSpan StateExpirationTime { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Automatically retry requests that receive 429 Too Many Requests status.
        /// Default is false.
        /// </summary>
        public bool AutoRetryOn429 { get; set; } = false;

        /// <summary>
        /// Custom function to determine the tracking key for a URI.
        /// Default tracks by scheme and host (e.g., "https://api.example.com").
        /// </summary>
        public Func<Uri, string>? GetLimitKey { get; set; }

        /// <summary>
        /// Callback invoked when rate limit headers are received and parsed.
        /// </summary>
        public Action<Uri, RateLimitHeaders>? OnRateLimitHeadersReceived { get; set; }

        /// <summary>
        /// Callback invoked when a delay is calculated and applied.
        /// </summary>
        public Action<Uri, TimeSpan>? OnDelayCalculated { get; set; }

        /// <summary>
        /// Callback invoked when a 429 Too Many Requests response is received.
        /// </summary>
        public Action<HttpRequestMessage, HttpResponseMessage>? OnTooManyRequests { get; set; }

        /// <summary>
        /// Callback invoked when an error occurs parsing rate limit headers.
        /// </summary>
        public Action<Uri, Exception>? OnParsingError { get; set; }
    }

    /// <summary>
    /// Specifies when rate limit delays should be applied.
    /// </summary>
    public enum RateLimitWaitMode
    {
        /// <summary>
        /// Wait before sending the request. This prevents sending requests that would be rate limited.
        /// </summary>
        BeforeRequest,

        /// <summary>
        /// Wait after receiving the response. This ensures rate limit info is applied to subsequent requests.
        /// </summary>
        AfterResponse,

        /// <summary>
        /// Never wait automatically. The caller is responsible for handling rate limits.
        /// </summary>
        Never
    }
}
