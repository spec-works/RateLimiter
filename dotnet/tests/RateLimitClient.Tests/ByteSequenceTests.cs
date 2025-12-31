using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace RateLimitClient.Tests
{
    /// <summary>
    /// Tests to verify that partition keys are properly handled as byte sequences per RFC 9651.
    /// </summary>
    public class ByteSequenceTests
    {
        [Fact]
        public async Task ByteSequence_PartitionKey_SerializedCorrectly()
        {
            // Arrange
            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(new RateLimitHandlerOptions(), mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            var token = JwtTokenHelper.CreateSampleToken("user-12345", "Test User", "test@example.com");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Act
            var response = await client.GetAsync("https://api.example.com/data");

            // Assert - Check that the response contains a RateLimit-Policy header with proper format
            Assert.True(response.Headers.Contains("RateLimit-Policy"));
            var policyHeader = string.Join(", ", response.Headers.GetValues("RateLimit-Policy"));

            // Verify byte sequence format: pk=:base64:
            Assert.Contains("pk=:", policyHeader);
            Assert.True(policyHeader.IndexOf("pk=:") < policyHeader.LastIndexOf(":"),
                "Partition key should be wrapped in colons");
        }

        [Fact]
        public async Task ByteSequence_ParsedPartitionKey_MatchesOriginalValue()
        {
            // Arrange
            var expectedUserId = "user-test-123";
            var mockBackend = new MockRateLimitHandler();

            var callbackInvoked = false;
            string parsedPartitionKey = null;

            var options = new RateLimitHandlerOptions
            {
                OnRateLimitHeadersReceived = (uri, headers) =>
                {
                    callbackInvoked = true;
                    if (headers.Limits.Count > 0 && !string.IsNullOrEmpty(headers.Limits[0].PartitionKey))
                    {
                        parsedPartitionKey = headers.Limits[0].PartitionKey;
                    }
                }
            };

            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            var token = JwtTokenHelper.CreateSampleToken(expectedUserId, "Test User", "test@example.com");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Act
            await client.GetAsync("https://api.example.com/data");

            // Assert
            Assert.True(callbackInvoked, "Callback should have been invoked");
            Assert.Equal(expectedUserId, parsedPartitionKey);
        }

        [Fact]
        public void ByteSequence_Base64Encoding_RoundTrips()
        {
            // Arrange
            var originalValue = "user-12345";

            // Act - Encode as byte sequence
            var bytes = System.Text.Encoding.UTF8.GetBytes(originalValue);
            var base64 = Convert.ToBase64String(bytes);
            var serialized = $":{base64}:";

            // Decode byte sequence
            var extracted = serialized.Substring(1, serialized.Length - 2);
            var decodedBytes = Convert.FromBase64String(extracted);
            var decodedValue = System.Text.Encoding.UTF8.GetString(decodedBytes);

            // Assert
            Assert.Equal(originalValue, decodedValue);
            Assert.StartsWith(":", serialized);
            Assert.EndsWith(":", serialized);
        }

        [Fact]
        public async Task ByteSequence_SpecialCharacters_EncodedCorrectly()
        {
            // Arrange - Test with special characters that need proper UTF-8 encoding
            var specialUserId = "user-αβγ-123";
            var mockBackend = new MockRateLimitHandler();

            string parsedPartitionKey = null;
            var options = new RateLimitHandlerOptions
            {
                OnRateLimitHeadersReceived = (uri, headers) =>
                {
                    if (headers.Limits.Count > 0)
                    {
                        parsedPartitionKey = headers.Limits[0].PartitionKey;
                    }
                }
            };

            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            var token = JwtTokenHelper.CreateSampleToken(specialUserId, "Test User", "test@example.com");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Act
            await client.GetAsync("https://api.example.com/data");

            // Assert
            Assert.Equal(specialUserId, parsedPartitionKey);
        }

        [Fact]
        public async Task ByteSequence_EmptyPartitionKey_HandledGracefully()
        {
            // Arrange
            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(new RateLimitHandlerOptions(), mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Don't set authorization header - anonymous user

            // Act
            var response = await client.GetAsync("https://api.example.com/data");

            // Assert - Should not throw exception
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var tracker = rateLimitHandler.GetTracker();
            var state = tracker.GetCurrentState();
            Assert.NotEmpty(state);
        }
    }
}
