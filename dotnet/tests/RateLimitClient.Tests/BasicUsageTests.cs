using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace RateLimitClient.Tests
{
    public class BasicUsageTests
    {
        [Fact]
        public async Task BasicUsage_WithDefaultOptions_SendsRequestSuccessfully()
        {
            // Arrange
            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(new RateLimitHandlerOptions(), mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act
            var response = await client.GetAsync("https://api.example.com/data");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task BasicUsage_WithMultipleRequests_AllSucceed()
        {
            // Arrange
            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(new RateLimitHandlerOptions(), mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act & Assert
            for (int i = 0; i < 5; i++)
            {
                var response = await client.GetAsync("https://api.example.com/data");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [Fact]
        public async Task BasicUsage_WithMultipleRequests_TracksRateLimitState()
        {
            // Arrange
            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(new RateLimitHandlerOptions(), mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act
            for (int i = 0; i < 3; i++)
            {
                await client.GetAsync("https://api.example.com/data");
            }

            // Assert
            var tracker = rateLimitHandler.GetTracker();
            var state = tracker.GetCurrentState();
            Assert.NotEmpty(state);
        }

        [Fact]
        public async Task BasicUsage_WithDefaultHandler_DoesNotThrowException()
        {
            // Arrange
            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(new RateLimitHandlerOptions(), mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act
            var exception = await Record.ExceptionAsync(async () =>
            {
                await client.GetAsync("https://api.example.com/data");
            });

            // Assert
            Assert.Null(exception);
        }
    }
}
