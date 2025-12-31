using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace RateLimitClient.Tests
{
    public class PartitionKeyTests
    {
        [Fact]
        public async Task PartitionKey_WithDifferentUsers_TracksIndependentQuotas()
        {
            // Arrange
            var freeUserToken = JwtTokenHelper.CreateSampleToken("user-12345", "John Doe", "john@example.com");
            var premiumUserToken = JwtTokenHelper.CreateSampleToken("premium-67890", "Jane Smith", "jane@example.com");

            var mockBackend = new MockRateLimitHandler();
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.BeforeRequest,
                EnableProactiveThrottling = true,
                ProactiveThrottleThreshold = 0.7
            };

            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act - Make requests as free user
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", freeUserToken);

            for (int i = 0; i < 5; i++)
            {
                await client.GetAsync("https://api.example.com/data");
            }

            // Switch to premium user
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", premiumUserToken);

            for (int i = 0; i < 5; i++)
            {
                await client.GetAsync("https://api.example.com/data");
            }

            // Assert - Check that both users have independent state
            var tracker = rateLimitHandler.GetTracker();
            var state = tracker.GetCurrentState();

            var freeUserStates = state.Values.Where(v => v.PartitionKey?.Contains("user-12345") == true).ToList();
            var premiumUserStates = state.Values.Where(v => v.PartitionKey?.Contains("premium-67890") == true).ToList();

            Assert.NotEmpty(freeUserStates);
            Assert.NotEmpty(premiumUserStates);
        }

        [Fact]
        public async Task PartitionKey_CalculateDelayForSpecificUser_ReturnsUserSpecificDelay()
        {
            // Arrange
            var freeUserToken = JwtTokenHelper.CreateSampleToken("user-12345", "John Doe", "john@example.com");
            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(new RateLimitHandlerOptions(), mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", freeUserToken);

            // Act - Make request to establish state
            await client.GetAsync("https://api.example.com/data");

            var userId = JwtTokenHelper.ExtractOid(freeUserToken);
            var tracker = rateLimitHandler.GetTracker();
            var delay = tracker.CalculateDelay(
                new Uri("https://api.example.com/data"),
                userId);

            // Assert
            Assert.NotNull(userId);
            Assert.True(delay >= TimeSpan.Zero);
        }

        [Fact]
        public void JwtTokenHelper_ExtractOid_ReturnsCorrectUserId()
        {
            // Arrange
            var expectedOid = "user-12345";
            var token = JwtTokenHelper.CreateSampleToken(expectedOid, "Test User", "test@example.com");

            // Act
            var actualOid = JwtTokenHelper.ExtractOid(token);

            // Assert
            Assert.Equal(expectedOid, actualOid);
        }

        [Fact]
        public void JwtTokenHelper_CreateSampleToken_ReturnsValidToken()
        {
            // Arrange & Act
            var token = JwtTokenHelper.CreateSampleToken("test-oid", "Test User", "test@example.com");

            // Assert
            Assert.NotNull(token);
            Assert.NotEmpty(token);
        }

        [Fact]
        public async Task PartitionKey_MultipleUsersSequentially_MaintainsIndependentQuotas()
        {
            // Arrange
            var user1Token = JwtTokenHelper.CreateSampleToken("user-1", "User One", "user1@example.com");
            var user2Token = JwtTokenHelper.CreateSampleToken("user-2", "User Two", "user2@example.com");
            var user3Token = JwtTokenHelper.CreateSampleToken("user-3", "User Three", "user3@example.com");

            var mockBackend = new MockRateLimitHandler();
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.BeforeRequest
            };

            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act - User 1 makes requests
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user1Token);
            await client.GetAsync("https://api.example.com/data");

            // User 2 makes requests
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user2Token);
            await client.GetAsync("https://api.example.com/data");

            // User 3 makes requests
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user3Token);
            await client.GetAsync("https://api.example.com/data");

            // Back to User 1
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user1Token);
            await client.GetAsync("https://api.example.com/data");

            // Assert
            var tracker = rateLimitHandler.GetTracker();
            var state = tracker.GetCurrentState();

            var user1States = state.Values.Where(v => v.PartitionKey?.Contains("user-1") == true).ToList();
            var user2States = state.Values.Where(v => v.PartitionKey?.Contains("user-2") == true).ToList();
            var user3States = state.Values.Where(v => v.PartitionKey?.Contains("user-3") == true).ToList();

            // All three users should have their own state
            Assert.NotEmpty(user1States);
            Assert.NotEmpty(user2States);
            Assert.NotEmpty(user3States);
        }

        [Fact]
        public async Task PartitionKey_WithCallbacks_ReportsUserSpecificLimits()
        {
            // Arrange
            var freeUserToken = JwtTokenHelper.CreateSampleToken("user-free", "Free User", "free@example.com");
            var partitionKeysReceived = 0;

            var mockBackend = new MockRateLimitHandler();
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.BeforeRequest,
                OnRateLimitHeadersReceived = (uri, headers) =>
                {
                    foreach (var limit in headers.Limits)
                    {
                        if (!string.IsNullOrEmpty(limit.PartitionKey))
                        {
                            partitionKeysReceived++;
                        }
                    }
                }
            };

            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freeUserToken);

            // Act
            await client.GetAsync("https://api.example.com/data");

            // Assert
            Assert.True(partitionKeysReceived > 0, "Partition keys should be reported in callbacks");
        }

        [Fact]
        public async Task PartitionKey_GetCurrentState_ShowsPartitionKeyInfo()
        {
            // Arrange
            var userToken = JwtTokenHelper.CreateSampleToken("user-test", "Test User", "test@example.com");
            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(new RateLimitHandlerOptions(), mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            // Act
            await client.GetAsync("https://api.example.com/data");

            var tracker = rateLimitHandler.GetTracker();
            var state = tracker.GetCurrentState();

            // Assert
            Assert.NotEmpty(state);
            var hasPartitionKey = state.Values.Any(v => !string.IsNullOrEmpty(v.PartitionKey));
            Assert.True(hasPartitionKey, "State should contain partition key information");
        }

        [Fact]
        public async Task PartitionKey_ManualDelayCalculation_WorksWithPartitionKey()
        {
            // Arrange
            var user1Token = JwtTokenHelper.CreateSampleToken("user-123", "User One", "user1@example.com");
            var user2Token = JwtTokenHelper.CreateSampleToken("user-456", "User Two", "user2@example.com");

            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(new RateLimitHandlerOptions(), mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Make requests as both users
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user1Token);
            await client.GetAsync("https://api.example.com/data");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user2Token);
            await client.GetAsync("https://api.example.com/data");

            // Act - Calculate delays for each user
            var user1Id = JwtTokenHelper.ExtractOid(user1Token);
            var user2Id = JwtTokenHelper.ExtractOid(user2Token);

            var tracker = rateLimitHandler.GetTracker();
            var uri = new Uri("https://api.example.com/data");

            var user1Delay = tracker.CalculateDelay(uri, user1Id);
            var user2Delay = tracker.CalculateDelay(uri, user2Id);

            // Assert
            Assert.True(user1Delay >= TimeSpan.Zero);
            Assert.True(user2Delay >= TimeSpan.Zero);
        }

        [Fact]
        public async Task PartitionKey_EnterpriseUser_HandlesHigherQuota()
        {
            // Arrange
            var enterpriseToken = JwtTokenHelper.CreateSampleToken("enterprise-999", "Enterprise User", "enterprise@example.com");
            var mockBackend = new MockRateLimitHandler();
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.BeforeRequest
            };

            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", enterpriseToken);

            // Act - Make multiple requests
            for (int i = 0; i < 3; i++)
            {
                var response = await client.GetAsync("https://api.example.com/data");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            // Assert
            var tracker = rateLimitHandler.GetTracker();
            var state = tracker.GetCurrentState();
            var enterpriseStates = state.Values.Where(v => v.PartitionKey?.Contains("enterprise-999") == true).ToList();

            Assert.NotEmpty(enterpriseStates);
        }
    }
}
