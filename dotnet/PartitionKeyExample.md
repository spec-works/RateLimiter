# Per-User Rate Limiting with Partition Keys

This document explains how to use partition keys for per-user rate limiting with JWT tokens.

## Overview

Partition keys (the `pk` parameter in rate limit headers) allow APIs to return different rate limits for different users or tenants. This is essential for tiered service offerings where:

- Free users get 10 requests per minute
- Premium users get 100 requests per minute
- Enterprise users get 1000 requests per minute

The rate limits are completely independent - one user exhausting their quota doesn't affect other users.

## How It Works

### 1. Server Side (API)

The API extracts the user identity from the JWT token and returns rate limit headers with the user ID as the partition key:

```http
Authorization: Bearer eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJvaWQiOiJ1c2VyLTEyMzQ1In0.

HTTP/1.1 200 OK
RateLimit-Policy: "free-user";q=10;w=60;pk="user-12345"
RateLimit: "free-user";r=7;t=45;pk="user-12345"
```

The `pk` parameter identifies which user's quota this applies to.

### 2. Client Side (This Implementation)

The `RateLimitHandler` automatically:

1. **Parses partition keys** from response headers
2. **Tracks limits per user** - each partition key gets its own rate limit state
3. **Applies delays per user** - delays are calculated based on the user's specific quota

The tracking key format is: `{uri}:{policyName}:{partitionKey}`

For example:
- `https://api.example.com:free-user:user-12345`
- `https://api.example.com:premium-user:premium-67890`

## Usage Example

### Basic Usage - Automatic Tracking

```csharp
// Create JWT token for a user
var token = JwtTokenHelper.CreateSampleToken("user-12345", "John Doe");

// Configure handler
var handler = new RateLimitHandler(new RateLimitHandlerOptions
{
    WaitMode = RateLimitWaitMode.BeforeRequest,
    EnableProactiveThrottling = true,

    OnRateLimitHeadersReceived = (uri, headers) =>
    {
        foreach (var limit in headers.Limits)
        {
            Console.WriteLine($"User {limit.PartitionKey}: {limit.Remaining} remaining");
        }
    }
});

using var client = new HttpClient(handler);

// Add token to request
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", token);

// Make requests - rate limiting applied automatically per user
var response = await client.GetAsync("https://api.example.com/data");
```

### Manual Delay Calculation per User

```csharp
// Extract user ID from token
var userId = JwtTokenHelper.ExtractOid(token);

// Get tracker from handler
var tracker = handler.GetTracker();

// Calculate delay for specific user
var delay = tracker.CalculateDelay(
    new Uri("https://api.example.com/data"),
    userId  // Partition key
);

if (delay > TimeSpan.Zero)
{
    Console.WriteLine($"User {userId} must wait {delay.TotalSeconds}s");
    await Task.Delay(delay);
}
```

## JWT Token Helper

The `JwtTokenHelper` class provides utilities for working with JWT tokens:

```csharp
// Extract oid (Object ID) claim - common in Azure AD tokens
var userId = JwtTokenHelper.ExtractOid(token);

// Extract sub (Subject) claim - standard JWT claim
var subject = JwtTokenHelper.ExtractSub(token);

// Extract any claim by name
var email = JwtTokenHelper.ExtractClaim(token, "email");

// Get all claims
var claims = JwtTokenHelper.ExtractAllClaims(token);

// Create test tokens (for demos/testing only - not signed)
var testToken = JwtTokenHelper.CreateSampleToken(
    "user-12345",
    "John Doe",
    "john@example.com"
);
```

## Mock Server Example

The `MockRateLimitHandler` simulates an API that returns per-user rate limits:

```csharp
// Create mock server
var mockServer = new MockRateLimitHandler();

// Wrap with rate limit handler
var rateLimitHandler = new RateLimitHandler(options, mockServer);
using var client = new HttpClient(rateLimitHandler);

// Different tokens = different quotas
var freeToken = JwtTokenHelper.CreateSampleToken("user-001");
var premiumToken = JwtTokenHelper.CreateSampleToken("premium-002");

// Free user (10 req/min)
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", freeToken);
await client.GetAsync("https://api.example.com/data");

// Premium user (100 req/min) - independent quota
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", premiumToken);
await client.GetAsync("https://api.example.com/data");
```

## Complete Example

See `PerUserRateLimitingExample()` in Program.cs for a complete working demonstration that shows:

1. Creating tokens for different user types
2. Making requests as different users
3. Each user having independent quotas
4. Proactive throttling per user
5. Inspecting rate limit state per user
6. Manual delay calculation per user

## Real-World Scenarios

### Multi-Tenant SaaS

```csharp
// Extract tenant ID from JWT token
var tenantId = JwtTokenHelper.ExtractClaim(token, "tid");

// API returns per-tenant limits
// RateLimit: "standard";r=50;t=30;pk="tenant-abc-123"
```

### User + Endpoint Combination

```csharp
// API might return different limits for different endpoints
// GET /api/data:  RateLimit: "reads";r=100;t=60;pk="user-123"
// POST /api/data: RateLimit: "writes";r=10;t=60;pk="user-123"
```

### Geographic Partitioning

```csharp
// API might partition by region
// RateLimit: "us-west";r=100;t=60;pk="user-123:us-west"
// RateLimit: "eu-central";r=50;t=60;pk="user-123:eu-central"
```

## Implementation Details

### State Tracking

Rate limit state is tracked using a `ConcurrentDictionary` with keys in the format:
```
{uri}:{policyName}:{partitionKey}
```

This ensures:
- Thread-safe concurrent access
- Independent tracking per user/tenant
- Efficient lookups

### Delay Calculation

When calculating delays with a partition key:

```csharp
public TimeSpan CalculateDelay(Uri requestUri, string partitionKey)
{
    // Only check states matching this URI and partition key
    var states = _limitStates.Where(s =>
        s.Key.StartsWith(uriPrefix) &&
        s.Key.EndsWith($":{partitionKey}"));

    // Calculate delay based on user's specific limits
    // ...
}
```

### Automatic Parsing

The parser automatically extracts partition keys from headers:

```csharp
RateLimit: "policy";r=50;t=30;pk="user-id"
           └── Parsed into RateLimitInfo.PartitionKey
```

## Best Practices

1. **Always use HTTPS** - JWT tokens contain sensitive information
2. **Validate tokens server-side** - don't trust client claims
3. **Use standard claims** - oid (Azure AD), sub (standard JWT)
4. **Handle missing tokens** - provide default/anonymous limits
5. **Log partition key usage** - helps debug rate limit issues
6. **Test with multiple users** - ensure quotas are independent

## Security Notes

- The `JwtTokenHelper` does **NOT** validate token signatures
- It's for client-side claim extraction only
- Server must validate tokens properly before returning rate limits
- Partition keys are informational - don't use for authentication
