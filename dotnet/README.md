# RateLimit Client - HTTP Traffic Shaping for C#

A C# implementation of a `HttpMessageHandler` that provides HTTP traffic shaping based on rate limit headers according to the [IETF draft-ietf-httpapi-ratelimit-headers](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/) specification.

## Features

- ✅ **Automatic Rate Limit Detection**: Parses `RateLimit` and `RateLimit-Policy` headers from HTTP responses
- ✅ **Traffic Shaping**: Automatically delays requests to stay within rate limits
- ✅ **Partition Key Support**: Track separate rate limits per user/tenant using partition keys (pk parameter)
- ✅ **JWT Token Integration**: Extract user IDs from JWT tokens (oid claim) for per-user rate limiting
- ✅ **Proactive Throttling**: Gradually slows down requests when approaching quota limits
- ✅ **Retry-After Support**: Respects `Retry-After` headers (takes precedence per spec)
- ✅ **429 Auto-Retry**: Optionally retry requests that receive 429 Too Many Requests
- ✅ **Multiple Policies**: Supports multiple rate limit policies per endpoint
- ✅ **Thread-Safe**: Concurrent request handling with proper synchronization
- ✅ **Flexible Configuration**: Extensive options for customizing behavior
- ✅ **Diagnostics**: Callbacks and state inspection for monitoring

## Quick Start

### Basic Usage

```csharp
using RateLimitClient;

// Create handler with default options
var rateLimitHandler = new RateLimitHandler();

// Use with HttpClient
using var client = new HttpClient(rateLimitHandler);

// Make requests - rate limiting applied automatically
var response = await client.GetAsync("https://api.example.com/data");
```

### Advanced Configuration

```csharp
var options = new RateLimitHandlerOptions
{
    // Wait before sending request (prevents 429 errors)
    WaitMode = RateLimitWaitMode.BeforeRequest,

    // Enable proactive throttling at 80% quota consumption
    EnableProactiveThrottling = true,
    ProactiveThrottleThreshold = 0.8,

    // Maximum delay to prevent DoS attacks
    MaxDelayThreshold = TimeSpan.FromMinutes(2),

    // Automatically retry 429 responses
    AutoRetryOn429 = true,

    // Callback when rate limits are detected
    OnRateLimitHeadersReceived = (uri, headers) =>
    {
        Console.WriteLine($"Rate limit info from {uri.Host}:");
        foreach (var limit in headers.Limits)
        {
            Console.WriteLine($"  {limit.PolicyName}: {limit.Remaining} remaining");
        }
    },

    // Callback when throttling occurs
    OnDelayCalculated = (uri, delay) =>
    {
        Console.WriteLine($"Waiting {delay.TotalSeconds:F1}s before next request");
    }
};

var handler = new RateLimitHandler(options);
using var client = new HttpClient(handler);
```

### Per-User Rate Limiting with Partition Keys

Partition keys allow tracking separate rate limits for different users or tenants. This is useful when each user has their own quota:

```csharp
// Extract user ID from JWT token
var token = "Bearer eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0...";
var userId = JwtTokenHelper.ExtractOid(token);

// Configure handler
var handler = new RateLimitHandler(new RateLimitHandlerOptions
{
    OnRateLimitHeadersReceived = (uri, headers) =>
    {
        foreach (var limit in headers.Limits)
        {
            Console.WriteLine($"User {limit.PartitionKey}: {limit.Remaining} requests remaining");
        }
    }
});

using var client = new HttpClient(handler);

// Add bearer token to request
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", token);

// Server returns headers like:
// RateLimit-Policy: "premium";q=100;w=60;pk="user-12345"
// RateLimit: "premium";r=87;t=45;pk="user-12345"

var response = await client.GetAsync("https://api.example.com/data");

// Check delay for specific user
var tracker = handler.GetTracker();
var delay = tracker.CalculateDelay(
    new Uri("https://api.example.com/data"),
    userId);
```

**Key Benefits:**
- Each user has independent rate limits
- One user hitting their limit doesn't affect others
- Supports different tiers (free, premium, enterprise)
- Partition key automatically tracked from response headers

See PartitionKeyTests.cs in the tests project for complete working demonstrations.

## Header Specification

### RateLimit-Policy Header

Defines the quota policies available:

```
RateLimit-Policy: "burst";q=100;w=60, "hourly";q=1000;w=3600
```

Parameters:
- **q** (required): Quota allocated in quota units
- **w** (optional): Time window in seconds
- **qu** (optional): Quota unit type (`requests`, `content-bytes`, `concurrent-requests`)
- **pk** (optional): Partition key (byte sequence)

### RateLimit Header

Provides current rate limit status:

```
RateLimit: "burst";r=42;t=15
```

Parameters:
- **r** (required): Remaining quota units
- **t** (optional): Seconds until quota restoration
- **pk** (optional): Partition key (byte sequence)

### Partition Key Format

Partition keys are **byte sequences** per RFC 9651. They are formatted as `:base64data:`:

```
RateLimit-Policy: "premium";q=100;w=60;pk=:dXNlci0xMjM0NQ==:
RateLimit: "premium";r=87;t=45;pk=:dXNlci0xMjM0NQ==:
```

The library automatically:
- **Parses** byte sequences by decoding base64 between colons
- **Serializes** partition keys as base64-encoded UTF-8 bytes wrapped in colons

Example partition key encoding:
- Value: `user-12345`
- UTF-8 bytes: `75 73 65 72 2d 31 32 33 34 35`
- Base64: `dXNlci0xMjM0NQ==`
- Serialized: `:dXNlci0xMjM0NQ==:`

### Retry-After Header

Standard HTTP header that takes precedence over rate limit headers:

```
Retry-After: 120
```

## Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `WaitMode` | `BeforeRequest` | When to apply delays: `BeforeRequest`, `AfterResponse`, or `Never` |
| `MaxDelayThreshold` | 5 minutes | Maximum delay to prevent DoS attacks |
| `EnableProactiveThrottling` | `true` | Gradually slow requests when approaching limits |
| `ProactiveThrottleThreshold` | 0.8 | Threshold (0-1) for triggering proactive throttling |
| `StateExpirationTime` | 1 hour | How long rate limit state remains valid |
| `AutoRetryOn429` | `false` | Automatically retry 429 Too Many Requests |
| `GetLimitKey` | Host-based | Custom function to determine tracking key |

## Callbacks

### OnRateLimitHeadersReceived
Invoked when rate limit headers are parsed:
```csharp
OnRateLimitHeadersReceived = (uri, headers) =>
{
    // Access parsed headers
    foreach (var policy in headers.Policies)
    {
        Console.WriteLine($"Policy: {policy.Name}, Quota: {policy.Quota}");
    }
}
```

### OnDelayCalculated
Invoked when a delay is calculated:
```csharp
OnDelayCalculated = (uri, delay) =>
{
    Console.WriteLine($"Throttling for {delay.TotalSeconds}s");
}
```

### OnTooManyRequests
Invoked when a 429 response is received:
```csharp
OnTooManyRequests = (request, response) =>
{
    Console.WriteLine($"Rate limited: {request.RequestUri}");
}
```

### OnParsingError
Invoked when header parsing fails:
```csharp
OnParsingError = (uri, exception) =>
{
    Console.WriteLine($"Parse error: {exception.Message}");
}
```

## Traffic Shaping Strategies

### 1. Prevent 429 Errors (Default)
```csharp
WaitMode = RateLimitWaitMode.BeforeRequest
```
Waits before sending requests to prevent hitting rate limits.

### 2. Learn and Adapt
```csharp
WaitMode = RateLimitWaitMode.AfterResponse
```
Sends request first, then waits before the next one based on response.

### 3. Manual Control
```csharp
WaitMode = RateLimitWaitMode.Never
```
No automatic delays. Use callbacks and `GetTracker()` for manual control:

```csharp
var tracker = handler.GetTracker();
var delay = tracker.CalculateDelay(uri);
if (delay > TimeSpan.Zero)
{
    await Task.Delay(delay);
}
```

## Proactive Throttling

When enabled, the handler gradually slows requests as quota is consumed:

- **Below threshold**: No throttling
- **Above threshold**: Spreads remaining requests evenly over remaining time
- **Example**: With 10 requests remaining in 100 seconds, each request waits ~10 seconds

This prevents sudden quota exhaustion and provides smoother traffic patterns.

## State Management

### Inspect Current State
```csharp
var tracker = handler.GetTracker();
var state = tracker.GetCurrentState();

foreach (var kvp in state)
{
    Console.WriteLine($"{kvp.Key}: {kvp.Value.Remaining} remaining");
}
```

### Clear State
```csharp
handler.ClearState();
```

## Best Practices

1. **Use per-host tracking** (default) for most scenarios
2. **Enable proactive throttling** to avoid sudden quota exhaustion
3. **Set reasonable MaxDelayThreshold** to prevent excessive waits
4. **Use callbacks** for logging and monitoring
5. **Handle AutoRetryOn429 carefully** - it can cause long delays
6. **Test with real APIs** to tune ProactiveThrottleThreshold

## Thread Safety

All components are thread-safe and support concurrent requests. Rate limit state is tracked using thread-safe collections with proper locking.

## Spec Compliance

This implementation follows the IETF draft specification:
- ✅ Parses structured field list format
- ✅ Handles multiple policies and limits
- ✅ Respects Retry-After precedence
- ✅ Ignores malformed headers per spec
- ✅ Uses delay-seconds (not timestamps) for reset
- ✅ Implements client-side delay/throttling strategies
- ✅ Accounts for network latency
- ✅ Applies maximum delay thresholds

## Project Structure

```
RateLimitClient/
├── src/
│   └── RateLimitClient/              # Core library
│       ├── RateLimitModels.cs         # Data models
│       ├── RateLimitHeaderParser.cs   # Header parser
│       ├── RateLimitTracker.cs        # State tracking
│       ├── RateLimitHandler.cs        # Main handler
│       ├── JwtTokenHelper.cs          # JWT utilities
│       └── StructuredFields/          # RFC 9651 parsing (decoupled)
│           ├── IStructuredFieldParser.cs
│           ├── StructuredFieldParser.cs
│           └── StructuredFieldItem.cs
└── tests/
    └── RateLimitClient.Tests/         # Test suite
        ├── BasicUsageTests.cs          # Basic usage tests
        ├── AdvancedUsageTests.cs       # Advanced options tests
        ├── ManualControlTests.cs       # Manual control tests
        ├── PartitionKeyTests.cs        # Per-user rate limiting tests
        ├── ByteSequenceTests.cs        # Byte sequence format tests
        ├── StructuredFieldParserTests.cs  # RFC 9651 parser tests
        └── MockRateLimitHandler.cs     # Mock for testing
```

### Structured Fields Abstraction

The structured fields parsing logic (RFC 9651) has been decoupled into a separate namespace with a clean interface. This allows easy replacement with third-party libraries if needed. See [StructuredFields-README.md](StructuredFields-README.md) for details on swapping implementations.

## Building

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test

# Build library only
dotnet build src/RateLimitClient/RateLimitClient.csproj
```

## Testing

The test suite includes 51+ tests covering:
- Basic usage scenarios
- Advanced configuration options
- Manual rate limit control
- Per-user rate limiting with partition keys
- JWT token integration
- Callback functionality
- State management
- Byte sequence encoding/decoding (RFC 9651)
- Structured field parsing (RFC 9651)

Run tests with:
```bash
dotnet test
```

Or run specific test classes:
```bash
dotnet test --filter "FullyQualifiedName~BasicUsageTests"
dotnet test --filter "FullyQualifiedName~PartitionKeyTests"
dotnet test --filter "FullyQualifiedName~StructuredFieldParserTests"
```

## License

This implementation is provided as-is for use in your projects.
