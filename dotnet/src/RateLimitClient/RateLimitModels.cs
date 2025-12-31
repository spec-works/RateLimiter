using System;
using System.Collections.Generic;

namespace RateLimitClient
{
    /// <summary>
    /// Represents a quota policy from the RateLimit-Policy header.
    /// </summary>
    public class RateLimitPolicy
    {
        /// <summary>
        /// Policy name/identifier.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Quota allocated in quota units (q parameter).
        /// </summary>
        public long Quota { get; set; }

        /// <summary>
        /// Time window in seconds (w parameter).
        /// </summary>
        public int? WindowSeconds { get; set; }

        /// <summary>
        /// Quota unit type (qu parameter): "requests", "content-bytes", or "concurrent-requests".
        /// </summary>
        public string QuotaUnit { get; set; } = "requests";

        /// <summary>
        /// Partition key (pk parameter).
        /// </summary>
        public string PartitionKey { get; set; }
    }

    /// <summary>
    /// Represents current rate limit information from the RateLimit header.
    /// </summary>
    public class RateLimitInfo
    {
        /// <summary>
        /// Policy name this limit applies to.
        /// </summary>
        public string PolicyName { get; set; }

        /// <summary>
        /// Remaining quota units (r parameter).
        /// </summary>
        public long Remaining { get; set; }

        /// <summary>
        /// Seconds until quota restoration (t parameter).
        /// </summary>
        public int? ResetSeconds { get; set; }

        /// <summary>
        /// Partition key (pk parameter).
        /// </summary>
        public string PartitionKey { get; set; }

        /// <summary>
        /// Timestamp when this rate limit was received.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// Calculates when the quota will reset based on timestamp and reset seconds.
        /// </summary>
        public DateTimeOffset? GetResetTime()
        {
            if (ResetSeconds.HasValue)
            {
                return Timestamp.AddSeconds(ResetSeconds.Value);
            }
            return null;
        }

        /// <summary>
        /// Gets the time remaining until reset, accounting for elapsed time since the header was received.
        /// </summary>
        public TimeSpan? GetTimeUntilReset()
        {
            var resetTime = GetResetTime();
            if (resetTime.HasValue)
            {
                var remaining = resetTime.Value - DateTimeOffset.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
            return null;
        }
    }

    /// <summary>
    /// Contains parsed rate limit headers from a response.
    /// </summary>
    public class RateLimitHeaders
    {
        /// <summary>
        /// Policies from RateLimit-Policy header.
        /// </summary>
        public List<RateLimitPolicy> Policies { get; set; } = new List<RateLimitPolicy>();

        /// <summary>
        /// Current limits from RateLimit header.
        /// </summary>
        public List<RateLimitInfo> Limits { get; set; } = new List<RateLimitInfo>();

        /// <summary>
        /// Retry-After value if present (takes precedence over reset parameter).
        /// </summary>
        public int? RetryAfterSeconds { get; set; }

        /// <summary>
        /// Timestamp when these headers were received.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }
}
