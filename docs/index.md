# RateLimitClient Documentation

Client-side rate limiting based on HTTP headers according to [draft-ietf-httpapi-ratelimit-headers](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/).

## What is RateLimitClient?

RateLimitClient is a .NET library that provides client-side rate limiting functionality based on standardized HTTP rate limit headers. It automatically tracks and respects rate limits returned by APIs, preventing your application from exceeding rate limits and handling backoff strategies.

## Installation

Install via NuGet:

```bash
dotnet add package RateLimitClient
```

## Features

- ✅ **Standards-Based** - Implements draft-ietf-httpapi-ratelimit-headers
- ✅ **Automatic Tracking** - Tracks rate limits per API endpoint
- ✅ **Smart Backoff** - Automatically delays requests when limits are approached
- ✅ **HttpClient Integration** - Seamless integration with HttpClient via DelegatingHandler
- ✅ **Multiple Rate Limit Policies** - Supports multiple concurrent rate limit windows
- ✅ **Thread-Safe** - Safe for concurrent requests
- ✅ **Type-Safe API** - Strong typing with nullable reference types
- ✅ **Multi-Target** - Supports .NET 10.0 and .NET 8.0 (LTS)

## Quick Start

### Basic Usage

```csharp
using RateLimitClient;

// Create HttpClient with rate limiting
var handler = new RateLimitHandler();
var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("https://api.example.com")
};

// Make requests - rate limiting happens automatically
var response = await httpClient.GetAsync("/api/data");

// The handler automatically:
// - Parses RateLimit-* headers from responses
// - Tracks remaining quota
// - Delays requests when limits are approached
// - Handles 429 Too Many Requests responses
```

### Advanced Configuration

```csharp
using RateLimitClient;

// Configure rate limit behavior
var options = new RateLimitOptions
{
    // Percentage of quota at which to start delaying requests (default: 0.8)
    ThrottleThreshold = 0.8,

    // Maximum time to wait before request (default: 60 seconds)
    MaxDelay = TimeSpan.FromSeconds(60),

    // Enable automatic retry on 429 (default: true)
    AutoRetryOn429 = true,

    // Maximum retry attempts (default: 3)
    MaxRetryAttempts = 3
};

var handler = new RateLimitHandler(options);
var httpClient = new HttpClient(handler);
```

### Manual Rate Limit Checking

```csharp
using RateLimitClient;

var handler = new RateLimitHandler();
var httpClient = new HttpClient(handler);

// Make a request
await httpClient.GetAsync("/api/data");

// Check current rate limit status
var tracker = handler.GetTracker(new Uri("https://api.example.com/api/data"));
if (tracker != null)
{
    Console.WriteLine($"Remaining: {tracker.Remaining}");
    Console.WriteLine($"Limit: {tracker.Limit}");
    Console.WriteLine($"Reset: {tracker.ResetTime}");
}
```

## Use Cases

### API Client Libraries

Build robust API clients that respect rate limits:

```csharp
public class MyApiClient
{
    private readonly HttpClient _httpClient;

    public MyApiClient()
    {
        var handler = new RateLimitHandler();
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com")
        };
    }

    public async Task<Data> GetDataAsync()
    {
        // Rate limiting handled automatically
        var response = await _httpClient.GetAsync("/api/data");
        return await response.Content.ReadFromJsonAsync<Data>();
    }
}
```

### Bulk Operations

Handle bulk operations without hitting rate limits:

```csharp
var handler = new RateLimitHandler(new RateLimitOptions
{
    ThrottleThreshold = 0.9, // Use 90% of quota before throttling
    AutoRetryOn429 = true
});

var httpClient = new HttpClient(handler);

// Process many items - rate limiting prevents 429 errors
foreach (var item in items)
{
    await httpClient.PostAsJsonAsync("/api/items", item);
    // Automatically delays when approaching rate limit
}
```

### Multi-Tenant Applications

Track rate limits per tenant:

```csharp
// Each tenant gets their own rate-limited client
public class TenantClientFactory
{
    private readonly Dictionary<string, HttpClient> _clients = new();

    public HttpClient GetClient(string tenantId)
    {
        if (!_clients.ContainsKey(tenantId))
        {
            var handler = new RateLimitHandler();
            _clients[tenantId] = new HttpClient(handler);
        }

        return _clients[tenantId];
    }
}
```

## API Reference

- [API Documentation](api/RateLimitClient.html) - Complete API reference

## Specification Compliance

This library implements [draft-ietf-httpapi-ratelimit-headers - RateLimit header fields for HTTP](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/).

### Supported Headers

| Header | Description | Status |
|--------|-------------|--------|
| RateLimit-Limit | Maximum requests in window | ✅ Supported |
| RateLimit-Remaining | Remaining requests in window | ✅ Supported |
| RateLimit-Reset | Time until window resets | ✅ Supported |
| RateLimit-Policy | Rate limit policy details | ✅ Supported |
| Retry-After | Time to wait before retry | ✅ Supported |

### Header Formats

The library supports both standard formats:

```http
RateLimit-Limit: 100
RateLimit-Remaining: 50
RateLimit-Reset: 60
```

And structured field format:

```http
RateLimit: limit=100, remaining=50, reset=60
```

## How It Works

### Rate Limit Tracking

1. **Header Parsing**: Automatically parses rate limit headers from API responses
2. **Quota Tracking**: Maintains current quota state per endpoint
3. **Threshold Detection**: Monitors when quota falls below threshold
4. **Automatic Delays**: Calculates and applies appropriate delays
5. **Reset Handling**: Automatically resets quotas when windows expire

### Backoff Strategy

When approaching rate limits:

1. Calculate time until reset
2. Calculate delay based on remaining quota and threshold
3. Delay request if necessary
4. Retry with exponential backoff on 429 responses

## Requirements

- .NET 10.0 or .NET 8.0 (LTS)
- C# 10.0 or later

## Source Code

View the source code on [GitHub](https://github.com/spec-works/RateLimiter).

## Contributing

Contributions welcome! See the [repository](https://github.com/spec-works/RateLimiter) for:
- Issue tracking
- Pull request guidelines
- Architecture Decision Records (ADRs)

## License

MIT License - see [LICENSE](https://github.com/spec-works/RateLimiter/blob/main/LICENSE) for details.

## Links

- **GitHub Repository**: [github.com/spec-works/RateLimiter](https://github.com/spec-works/RateLimiter)
- **Specification**: [draft-ietf-httpapi-ratelimit-headers](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/)
- **SpecWorks Factory**: [spec-works.github.io](https://spec-works.github.io)
