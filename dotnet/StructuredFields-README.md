# Structured Fields Abstraction

This document describes the structured fields parsing abstraction that enables easy swapping with third-party implementations.

## Overview

All RFC 9651 structured field parsing logic has been decoupled into the `RateLimitClient.StructuredFields` namespace. This allows the parsing implementation to be replaced with minimal impact on the rate limiting logic.

## Architecture

### Components

```
src/RateLimitClient/StructuredFields/
├── IStructuredFieldParser.cs      # Interface defining parsing contract
├── StructuredFieldParser.cs       # Default implementation
└── StructuredFieldItem.cs         # Data model for parsed items
```

### Interface

```csharp
public interface IStructuredFieldParser
{
    // Parse structured field list into items
    IList<StructuredFieldItem> ParseList(string headerValue);

    // Parse quoted strings
    string ParseString(string value);

    // Parse byte sequences (:base64:)
    string ParseByteSequence(string value);

    // Serialize byte sequences
    string SerializeByteSequence(string value);
}
```

### Data Model

```csharp
public class StructuredFieldItem
{
    public string Value { get; set; }
    public Dictionary<string, string> Parameters { get; set; }

    // Helper methods
    string? GetParameter(string key);
    bool TryGetParameterAsLong(string key, out long value);
    bool TryGetParameterAsInt(string key, out int value);
    bool HasParameter(string key);
}
```

## Usage in RateLimitClient

The `RateLimitHeaderParser` uses the structured field parser:

```csharp
public static class RateLimitHeaderParser
{
    private static readonly IStructuredFieldParser _parser = new StructuredFieldParser();

    private static List<RateLimitPolicy> ParseRateLimitPolicy(string headerValue)
    {
        var items = _parser.ParseList(headerValue);

        foreach (var item in items)
        {
            var policy = new RateLimitPolicy { Name = item.Value };

            if (item.TryGetParameterAsLong("q", out var quota))
                policy.Quota = quota;

            if (item.HasParameter("pk"))
                policy.PartitionKey = _parser.ParseByteSequence(item.GetParameter("pk")!);
        }
    }
}
```

## Swapping with a Third-Party Library

To use a third-party structured fields library (e.g., Redacted.StructuredFields):

### Option 1: Adapter Pattern

Create an adapter that implements `IStructuredFieldParser`:

```csharp
using ThirdParty.StructuredFields;

public class ThirdPartyStructuredFieldAdapter : IStructuredFieldParser
{
    private readonly StructuredFieldParser _thirdPartyParser = new();

    public IList<StructuredFieldItem> ParseList(string headerValue)
    {
        var thirdPartyResult = _thirdPartyParser.ParseList(headerValue);

        // Convert to our StructuredFieldItem format
        return thirdPartyResult.Select(item => new StructuredFieldItem
        {
            Value = item.BaseValue.ToString(),
            Parameters = item.Parameters.ToDictionary(
                p => p.Key,
                p => p.Value.ToString()
            )
        }).ToList();
    }

    public string ParseByteSequence(string value)
    {
        var bytes = _thirdPartyParser.ParseByteSequence(value);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    // Implement other interface methods...
}
```

### Option 2: Replace Default Implementation

Update `RateLimitHeaderParser` to use the adapter:

```csharp
public static class RateLimitHeaderParser
{
    // Change this line:
    private static readonly IStructuredFieldParser _parser = new ThirdPartyStructuredFieldAdapter();

    // Rest of the code remains unchanged...
}
```

### Option 3: Dependency Injection (Future Enhancement)

For full flexibility, inject the parser:

```csharp
public class RateLimitHandler : DelegatingHandler
{
    private readonly IStructuredFieldParser _parser;

    public RateLimitHandler(
        RateLimitHandlerOptions options,
        IStructuredFieldParser? parser = null)
    {
        _parser = parser ?? new StructuredFieldParser();
    }
}
```

## Testing

The `StructuredFieldParserTests` class provides 20 tests covering:
- List parsing with single/multiple items
- Parameter parsing (string and numeric)
- Quoted string handling
- Byte sequence encoding/decoding
- Edge cases (empty strings, special characters)
- RFC 9651 compliance examples

Run tests:
```bash
dotnet test --filter "FullyQualifiedName~StructuredFieldParserTests"
```

## Benefits

### Maintainability
- Structured field logic isolated in one namespace
- Changes to RFC 9651 spec only affect StructuredFields folder
- Rate limiting logic unaffected by parsing details

### Testability
- Structured field parsing tested independently
- Easy to mock `IStructuredFieldParser` for rate limit tests
- Clear separation of concerns

### Flexibility
- Swap implementations without touching rate limiting code
- Use optimized third-party libraries when available
- Support custom parsing logic for special requirements

## Migration Impact Analysis

If you decide to use a third-party library:

**Files to modify:** 1
- `RateLimitHeaderParser.cs` (change line 14: instantiate adapter)

**Files unaffected:** All other rate limiting logic
- `RateLimitHandler.cs`
- `RateLimitTracker.cs`
- `RateLimitModels.cs`
- All test files (except StructuredFieldParserTests if removing default implementation)

**Breaking changes:** None (interface preserved)

## RFC 9651 Compliance

The default implementation supports:
- ✅ Structured field lists with comma separation
- ✅ Quoted strings with escape sequences
- ✅ Byte sequences (`:base64:` format)
- ✅ Parameters as key-value pairs
- ✅ Multiple parameters per item
- ✅ Whitespace tolerance

The implementation does NOT support (as not needed for rate limiting):
- Inner lists
- Dictionaries
- Tokens (treated as unquoted strings)
- Decimals (treated as strings)
- Booleans (parameters without values)

## Examples

### Parsing Rate Limit Headers

```csharp
var parser = new StructuredFieldParser();

// Input: "premium";q=100;w=60;pk=:dXNlci0xMjM0NQ==:
var items = parser.ParseList(headerValue);

var item = items[0];
Console.WriteLine(item.Value);                       // "premium"
Console.WriteLine(item.GetParameter("q"));           // "100"
Console.WriteLine(item.GetParameter("w"));           // "60"

// Byte sequence automatically decoded
Console.WriteLine(item.GetParameter("pk"));          // Returns raw value
var decoded = parser.ParseByteSequence(item.GetParameter("pk")!);
Console.WriteLine(decoded);                          // "user-12345"
```

### Serializing Byte Sequences

```csharp
var parser = new StructuredFieldParser();

var userId = "user-12345";
var serialized = parser.SerializeByteSequence(userId);
Console.WriteLine(serialized);  // ":dXNlci0xMjM0NQ==:"

// Use in header
var header = $"\"premium\";q=100;w=60;pk={serialized}";
```

## Future Enhancements

Potential improvements:
1. Make `IStructuredFieldParser` injectable via constructor
2. Add configuration to select parser implementation
3. Support for additional structured field types (dictionaries, inner lists)
4. Performance benchmarking against third-party libraries
5. Streaming parser for large headers
