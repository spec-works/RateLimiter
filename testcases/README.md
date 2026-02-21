# RateLimiter Test Cases

Shared, language-independent test cases for the RateLimiter component.

## Format

Each test case is a JSON file containing HTTP header inputs and expected
parsed results per
[draft-ietf-httpapi-ratelimit-headers](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/)
and [RFC 9651](https://www.rfc-editor.org/rfc/rfc9651.html) (Structured Field Values).

### Tests

| File | Description |
|------|-------------|
| `ratelimit-headers.json` | RateLimit response header parsing |
| `ratelimit-policy-headers.json` | RateLimit-Policy header parsing |
| `structured-fields.json` | RFC 9651 structured field value parsing |
