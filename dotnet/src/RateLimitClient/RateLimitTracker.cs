using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RateLimitClient
{
    /// <summary>
    /// Tracks rate limit state and calculates delays needed for traffic shaping.
    /// </summary>
    public class RateLimitTracker
    {
        private readonly ConcurrentDictionary<string, RateLimitState> _limitStates = new ConcurrentDictionary<string, RateLimitState>();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly RateLimitHandlerOptions _options;

        public RateLimitTracker(RateLimitHandlerOptions options)
        {
            _options = options ?? new RateLimitHandlerOptions();
        }

        /// <summary>
        /// Updates the rate limit state based on response headers.
        /// </summary>
        public void UpdateFromHeaders(RateLimitHeaders headers, Uri requestUri)
        {
            if (headers == null) return;

            var key = GetLimitKey(requestUri);

            // Update state for each limit received
            foreach (var limit in headers.Limits)
            {
                // Include partition key in state key for per-user or per-tenant tracking
                var stateKey = $"{key}:{limit.PolicyName ?? "default"}:{limit.PartitionKey ?? "default"}";
                var state = _limitStates.GetOrAdd(stateKey, _ => new RateLimitState());

                state.PolicyName = limit.PolicyName ?? "default";
                state.PartitionKey = limit.PartitionKey;
                state.Remaining = limit.Remaining;
                state.ResetTime = limit.GetResetTime();
                state.LastUpdated = headers.Timestamp;

                // Find matching policy if available
                var policy = headers.Policies.FirstOrDefault(p => p.Name == limit.PolicyName);
                if (policy != null)
                {
                    state.Quota = policy.Quota;
                    state.WindowSeconds = policy.WindowSeconds;
                }
            }

            // Handle Retry-After
            if (headers.RetryAfterSeconds.HasValue)
            {
                var stateKey = $"{key}:retry-after";
                var state = _limitStates.GetOrAdd(stateKey, _ => new RateLimitState());
                state.PolicyName = "retry-after";
                state.Remaining = 0;
                state.ResetTime = headers.Timestamp.AddSeconds(headers.RetryAfterSeconds.Value);
                state.LastUpdated = headers.Timestamp;
            }
        }

        /// <summary>
        /// Calculates the delay needed before the next request can be sent.
        /// </summary>
        public TimeSpan CalculateDelay(Uri requestUri)
        {
            var key = GetLimitKey(requestUri);
            var maxDelay = TimeSpan.Zero;

            // Check all states for this URI
            foreach (var kvp in _limitStates.Where(s => s.Key.StartsWith(key)))
            {
                var state = kvp.Value;

                // Skip if state is stale
                if (IsStateStale(state))
                {
                    continue;
                }

                // Retry-After takes precedence
                if (state.PolicyName == "retry-after" && state.ResetTime.HasValue)
                {
                    var retryDelay = state.ResetTime.Value - DateTimeOffset.UtcNow;
                    if (retryDelay > TimeSpan.Zero)
                    {
                        return retryDelay;
                    }
                    continue;
                }

                // Check if we've exhausted the quota
                if (state.Remaining <= 0 && state.ResetTime.HasValue)
                {
                    var resetDelay = state.ResetTime.Value - DateTimeOffset.UtcNow;
                    if (resetDelay > maxDelay)
                    {
                        maxDelay = resetDelay;
                    }
                }
                // Apply proactive throttling if approaching limit
                else if (_options.EnableProactiveThrottling && state.Remaining > 0)
                {
                    var throttleDelay = CalculateProactiveDelay(state);
                    if (throttleDelay > maxDelay)
                    {
                        maxDelay = throttleDelay;
                    }
                }
            }

            // Apply maximum delay threshold to prevent DoS
            if (maxDelay > _options.MaxDelayThreshold)
            {
                maxDelay = _options.MaxDelayThreshold;
            }

            return maxDelay > TimeSpan.Zero ? maxDelay : TimeSpan.Zero;
        }

        /// <summary>
        /// Calculates the delay needed before the next request can be sent for a specific partition.
        /// </summary>
        /// <param name="requestUri">The request URI</param>
        /// <param name="partitionKey">The partition key (e.g., user ID from token)</param>
        public TimeSpan CalculateDelay(Uri requestUri, string partitionKey)
        {
            var key = GetLimitKey(requestUri);
            var maxDelay = TimeSpan.Zero;

            // Build the prefix to match states for this URI and partition
            var prefix = $"{key}:";
            var partitionSuffix = $":{partitionKey ?? "default"}";

            // Check states that match this URI and partition key
            foreach (var kvp in _limitStates.Where(s => s.Key.StartsWith(prefix) && s.Key.EndsWith(partitionSuffix)))
            {
                var state = kvp.Value;

                // Skip if state is stale
                if (IsStateStale(state))
                {
                    continue;
                }

                // Retry-After takes precedence
                if (state.PolicyName == "retry-after" && state.ResetTime.HasValue)
                {
                    var retryDelay = state.ResetTime.Value - DateTimeOffset.UtcNow;
                    if (retryDelay > TimeSpan.Zero)
                    {
                        return retryDelay;
                    }
                    continue;
                }

                // Check if we've exhausted the quota
                if (state.Remaining <= 0 && state.ResetTime.HasValue)
                {
                    var resetDelay = state.ResetTime.Value - DateTimeOffset.UtcNow;
                    if (resetDelay > maxDelay)
                    {
                        maxDelay = resetDelay;
                    }
                }
                // Apply proactive throttling if approaching limit
                else if (_options.EnableProactiveThrottling && state.Remaining > 0)
                {
                    var throttleDelay = CalculateProactiveDelay(state);
                    if (throttleDelay > maxDelay)
                    {
                        maxDelay = throttleDelay;
                    }
                }
            }

            // Apply maximum delay threshold to prevent DoS
            if (maxDelay > _options.MaxDelayThreshold)
            {
                maxDelay = _options.MaxDelayThreshold;
            }

            return maxDelay > TimeSpan.Zero ? maxDelay : TimeSpan.Zero;
        }

        /// <summary>
        /// Waits for the calculated delay before allowing the request to proceed.
        /// </summary>
        public async Task WaitForRateLimitAsync(Uri requestUri, CancellationToken cancellationToken)
        {
            var delay = CalculateDelay(requestUri);

            if (delay > TimeSpan.Zero)
            {
                _options.OnDelayCalculated?.Invoke(requestUri, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Waits for the calculated delay before allowing the request to proceed for a specific partition.
        /// </summary>
        /// <param name="requestUri">The request URI</param>
        /// <param name="partitionKey">The partition key (e.g., user ID from token)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task WaitForRateLimitAsync(Uri requestUri, string partitionKey, CancellationToken cancellationToken)
        {
            var delay = CalculateDelay(requestUri, partitionKey);

            if (delay > TimeSpan.Zero)
            {
                _options.OnDelayCalculated?.Invoke(requestUri, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Calculates proactive delay when approaching rate limit.
        /// </summary>
        private TimeSpan CalculateProactiveDelay(RateLimitState state)
        {
            if (!state.Quota.HasValue || !state.WindowSeconds.HasValue || state.Quota.Value == 0)
            {
                return TimeSpan.Zero;
            }

            // Calculate utilization percentage
            var used = state.Quota.Value - state.Remaining;
            var utilizationPercent = (double)used / state.Quota.Value;

            // Apply throttling when above threshold (default 80%)
            if (utilizationPercent >= _options.ProactiveThrottleThreshold)
            {
                // Calculate desired rate to stay within limits
                var timeElapsed = DateTimeOffset.UtcNow - state.LastUpdated;
                var timeRemaining = state.ResetTime.HasValue
                    ? state.ResetTime.Value - DateTimeOffset.UtcNow
                    : TimeSpan.FromSeconds(state.WindowSeconds.Value) - timeElapsed;

                if (timeRemaining > TimeSpan.Zero && state.Remaining > 0)
                {
                    // Spread remaining requests evenly over remaining time
                    var delayPerRequest = timeRemaining.TotalSeconds / state.Remaining;
                    return TimeSpan.FromSeconds(Math.Min(delayPerRequest, _options.MaxDelayThreshold.TotalSeconds));
                }
            }

            return TimeSpan.Zero;
        }

        /// <summary>
        /// Checks if a rate limit state is stale and should be ignored.
        /// </summary>
        private bool IsStateStale(RateLimitState state)
        {
            // State is stale if reset time has passed
            if (state.ResetTime.HasValue && DateTimeOffset.UtcNow > state.ResetTime.Value)
            {
                return true;
            }

            // State is stale if not updated recently and no reset time
            if (!state.ResetTime.HasValue && DateTimeOffset.UtcNow - state.LastUpdated > _options.StateExpirationTime)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the key for tracking rate limits based on URI.
        /// </summary>
        private string GetLimitKey(Uri uri)
        {
            // Track by host by default, can be customized
            return _options.GetLimitKey?.Invoke(uri) ?? $"{uri.Scheme}://{uri.Host}";
        }

        /// <summary>
        /// Clears all tracked rate limit state.
        /// </summary>
        public void Clear()
        {
            _limitStates.Clear();
        }

        /// <summary>
        /// Gets current state for diagnostics.
        /// </summary>
        public IReadOnlyDictionary<string, RateLimitState> GetCurrentState()
        {
            return new Dictionary<string, RateLimitState>(_limitStates);
        }
    }

    /// <summary>
    /// Represents the tracked state for a rate limit policy.
    /// </summary>
    public class RateLimitState
    {
        public string PolicyName { get; set; } = string.Empty;
        public string PartitionKey { get; set; } = string.Empty;
        public long Remaining { get; set; }
        public DateTimeOffset? ResetTime { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
        public long? Quota { get; set; }
        public int? WindowSeconds { get; set; }
    }
}
