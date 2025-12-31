using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RateLimitClient.StructuredFields;

namespace RateLimitClient
{
    /// <summary>
    /// Mock HTTP handler that simulates a server returning rate limit headers with partition keys.
    /// Used for testing and demonstration purposes.
    /// </summary>
    public class MockRateLimitHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, UserQuota> _userQuotas = new ConcurrentDictionary<string, UserQuota>();
        private readonly Dictionary<string, long> _defaultQuotas;
        private readonly IStructuredFieldParser _parser = new StructuredFieldParser();

        public MockRateLimitHandler()
        {
            // Default quotas for different user types
            _defaultQuotas = new Dictionary<string, long>
            {
                ["free-user"] = 10,      // Free users: 10 requests per minute
                ["premium-user"] = 100,  // Premium users: 100 requests per minute
                ["enterprise-user"] = 1000 // Enterprise users: 1000 requests per minute
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Extract JWT token from Authorization header
            string userId = null;
            string userType = "free-user"; // Default

            if (request.Headers.Authorization != null && request.Headers.Authorization.Scheme == "Bearer")
            {
                var token = request.Headers.Authorization.Parameter;
                userId = JwtTokenHelper.ExtractOid(token);

                // For demo purposes, determine user type from the user ID
                if (userId != null)
                {
                    if (userId.StartsWith("premium-"))
                        userType = "premium-user";
                    else if (userId.StartsWith("enterprise-"))
                        userType = "enterprise-user";
                }
            }

            // If no user ID, use anonymous
            if (userId == null)
            {
                userId = "anonymous";
                userType = "free-user";
            }

            // Get or create quota state for this user
            var quota = _userQuotas.GetOrAdd(userId, _ => new UserQuota
            {
                PolicyName = userType,
                Quota = _defaultQuotas[userType],
                WindowSeconds = 60,
                Remaining = _defaultQuotas[userType],
                ResetTime = DateTimeOffset.UtcNow.AddSeconds(60)
            });

            // Check if quota needs to be reset
            if (DateTimeOffset.UtcNow >= quota.ResetTime)
            {
                quota.Remaining = quota.Quota;
                quota.ResetTime = DateTimeOffset.UtcNow.AddSeconds(quota.WindowSeconds);
            }

            var response = new HttpResponseMessage();

            // Check if user has exceeded quota
            if (quota.Remaining <= 0)
            {
                response.StatusCode = HttpStatusCode.TooManyRequests;
                var resetSeconds = (int)(quota.ResetTime - DateTimeOffset.UtcNow).TotalSeconds;
                response.Headers.Add("Retry-After", resetSeconds.ToString());
            }
            else
            {
                response.StatusCode = HttpStatusCode.OK;
                response.Content = new StringContent($"{{\"message\": \"Success\", \"userId\": \"{userId}\"}}");
                quota.Remaining--;
            }

            // Add rate limit headers with partition key
            var resetSecondsRemaining = (int)(quota.ResetTime - DateTimeOffset.UtcNow).TotalSeconds;
            var partitionKey = _parser.SerializeByteSequence(userId);

            // RateLimit-Policy header
            response.Headers.Add("RateLimit-Policy",
                $"\"{quota.PolicyName}\";q={quota.Quota};w={quota.WindowSeconds};pk={partitionKey}");

            // RateLimit header with current state
            response.Headers.Add("RateLimit",
                $"\"{quota.PolicyName}\";r={quota.Remaining};t={resetSecondsRemaining};pk={partitionKey}");

            return Task.FromResult(response);
        }

        private class UserQuota
        {
            public string PolicyName { get; set; }
            public long Quota { get; set; }
            public int WindowSeconds { get; set; }
            public long Remaining { get; set; }
            public DateTimeOffset ResetTime { get; set; }
        }

        /// <summary>
        /// Gets the current quota state for a user (for testing/diagnostics).
        /// </summary>
        public long GetRemainingQuota(string userId)
        {
            if (_userQuotas.TryGetValue(userId, out var quota))
            {
                return quota.Remaining;
            }
            return -1;
        }

        /// <summary>
        /// Resets all user quotas (for testing).
        /// </summary>
        public void ResetAllQuotas()
        {
            _userQuotas.Clear();
        }
    }
}
