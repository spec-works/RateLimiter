using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace RateLimitClient.Tests
{
    public class ManualControlTests
    {
        [Fact]
        public async Task ManualControl_WithNeverWaitMode_DoesNotApplyAutomaticDelays()
        {
            // Arrange
            var delayCalculated = false;
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.Never,
                OnDelayCalculated = (uri, delay) =>
                {
                    delayCalculated = true;
                }
            };

            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act
            await client.GetAsync("https://api.example.com/data");

            // Assert
            Assert.False(delayCalculated, "WaitMode.Never should not calculate automatic delays");
        }

        [Fact]
        public async Task ManualControl_CalculateDelay_ReturnsNonNegativeDelay()
        {
            // Arrange
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.Never
            };

            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act - First make request to establish rate limit state
            await client.GetAsync("https://api.example.com/data");

            var tracker = rateLimitHandler.GetTracker();
            var delay = tracker.CalculateDelay(new Uri("https://api.example.com/data"));

            // Assert
            Assert.True(delay >= TimeSpan.Zero, "Delay should be non-negative");
        }

        [Fact]
        public async Task ManualControl_CalculateDelayMultipleTimes_ReturnsConsistentResults()
        {
            // Arrange
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.Never
            };

            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act
            await client.GetAsync("https://api.example.com/data");

            var tracker = rateLimitHandler.GetTracker();
            var uri = new Uri("https://api.example.com/data");
            var delay1 = tracker.CalculateDelay(uri);
            await Task.Delay(100);
            var delay2 = tracker.CalculateDelay(uri);

            // Assert
            Assert.True(delay1 >= TimeSpan.Zero);
            Assert.True(delay2 >= TimeSpan.Zero);
            // delay2 should be less than or equal to delay1 since time has passed
            Assert.True(delay2 <= delay1);
        }

        [Fact]
        public async Task ManualControl_OnRateLimitHeadersReceived_ProvidesHeadersForManualHandling()
        {
            // Arrange
            var headersReceived = false;
            var hasLimits = false;
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.Never,
                OnRateLimitHeadersReceived = (uri, headers) =>
                {
                    headersReceived = true;
                    hasLimits = headers.Limits.Count > 0;
                }
            };

            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act
            await client.GetAsync("https://api.example.com/data");

            // Assert
            Assert.True(headersReceived, "Headers callback should be invoked");
            Assert.True(hasLimits, "Headers should contain rate limit information");
        }

        [Fact]
        public async Task ManualControl_WithManualDelay_CanControlRequestTiming()
        {
            // Arrange
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.Never
            };

            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);
            var tracker = rateLimitHandler.GetTracker();

            // Act
            for (int i = 0; i < 3; i++)
            {
                // Manually check and apply delay
                var delay = tracker.CalculateDelay(new Uri("https://api.example.com/data"));
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay);
                }

                var response = await client.GetAsync("https://api.example.com/data");

                // Assert
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [Fact]
        public async Task ManualControl_GetTracker_ReturnsValidTracker()
        {
            // Arrange
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.Never
            };

            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(options, mockBackend);

            // Act
            var tracker = rateLimitHandler.GetTracker();

            // Assert
            Assert.NotNull(tracker);
        }

        [Fact]
        public async Task ManualControl_GetCurrentState_ShowsTrackedLimits()
        {
            // Arrange
            var options = new RateLimitHandlerOptions
            {
                WaitMode = RateLimitWaitMode.Never
            };

            var mockBackend = new MockRateLimitHandler();
            var rateLimitHandler = new RateLimitHandler(options, mockBackend);
            using var client = new HttpClient(rateLimitHandler);

            // Act
            await client.GetAsync("https://api.example.com/data");
            await client.GetAsync("https://api.example.com/data");

            var tracker = rateLimitHandler.GetTracker();
            var state = tracker.GetCurrentState();

            // Assert
            Assert.NotEmpty(state);
        }
    }
}
