using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using RateLimitClient.StructuredFields;

namespace RateLimitClient
{
    /// <summary>
    /// Parses RateLimit-Policy and RateLimit headers according to the structured field list format.
    /// Uses IStructuredFieldParser abstraction for parsing, allowing easy replacement with third-party libraries.
    /// </summary>
    public static class RateLimitHeaderParser
    {
        private static readonly IStructuredFieldParser _parser = new StructuredFieldParser();

        /// <summary>
        /// Parses RateLimit-Policy and RateLimit headers from HTTP response headers.
        /// </summary>
        public static RateLimitHeaders ParseHeaders(HttpResponseHeaders headers)
        {
            var result = new RateLimitHeaders
            {
                Timestamp = DateTimeOffset.UtcNow
            };

            // Parse RateLimit-Policy header
            if (headers.TryGetValues("RateLimit-Policy", out var policyValues))
            {
                foreach (var value in policyValues)
                {
                    try
                    {
                        var policies = ParseRateLimitPolicy(value);
                        result.Policies.AddRange(policies);
                    }
                    catch
                    {
                        // Ignore malformed headers per spec
                    }
                }
            }

            // Parse RateLimit header
            if (headers.TryGetValues("RateLimit", out var limitValues))
            {
                foreach (var value in limitValues)
                {
                    try
                    {
                        var limits = ParseRateLimit(value, result.Timestamp);
                        result.Limits.AddRange(limits);
                    }
                    catch
                    {
                        // Ignore malformed headers per spec
                    }
                }
            }

            // Parse Retry-After header (takes precedence)
            if (headers.RetryAfter != null)
            {
                if (headers.RetryAfter.Delta.HasValue)
                {
                    result.RetryAfterSeconds = (int)headers.RetryAfter.Delta.Value.TotalSeconds;
                }
                else if (headers.RetryAfter.Date.HasValue)
                {
                    var delta = headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                    result.RetryAfterSeconds = Math.Max(0, (int)delta.TotalSeconds);
                }
            }

            return result;
        }

        /// <summary>
        /// Parses a RateLimit-Policy header value.
        /// Format: "name";q=100;w=60,"name2";q=200;w=120
        /// </summary>
        private static List<RateLimitPolicy> ParseRateLimitPolicy(string headerValue)
        {
            var policies = new List<RateLimitPolicy>();
            var items = _parser.ParseList(headerValue);

            foreach (var item in items)
            {
                var policy = new RateLimitPolicy
                {
                    Name = item.Value
                };

                // Parse parameters
                if (item.TryGetParameterAsLong("q", out var quota))
                {
                    policy.Quota = quota;
                }

                if (item.TryGetParameterAsInt("w", out var window))
                {
                    policy.WindowSeconds = window;
                }

                if (item.HasParameter("qu"))
                {
                    policy.QuotaUnit = _parser.ParseString(item.GetParameter("qu")!);
                }

                if (item.HasParameter("pk"))
                {
                    policy.PartitionKey = _parser.ParseByteSequence(item.GetParameter("pk")!);
                }

                // Quota (q) is required per spec
                if (policy.Quota > 0)
                {
                    policies.Add(policy);
                }
            }

            return policies;
        }

        /// <summary>
        /// Parses a RateLimit header value.
        /// Format: "name";r=50;t=30,"name2";r=100;t=60
        /// </summary>
        private static List<RateLimitInfo> ParseRateLimit(string headerValue, DateTimeOffset timestamp)
        {
            var limits = new List<RateLimitInfo>();
            var items = _parser.ParseList(headerValue);

            foreach (var item in items)
            {
                var limit = new RateLimitInfo
                {
                    PolicyName = item.Value,
                    Timestamp = timestamp
                };

                // Parse parameters
                if (item.TryGetParameterAsLong("r", out var remaining))
                {
                    limit.Remaining = remaining;
                }

                if (item.TryGetParameterAsInt("t", out var reset))
                {
                    limit.ResetSeconds = reset;
                }

                if (item.HasParameter("pk"))
                {
                    limit.PartitionKey = _parser.ParseByteSequence(item.GetParameter("pk")!);
                }

                limits.Add(limit);
            }

            return limits;
        }
    }
}
