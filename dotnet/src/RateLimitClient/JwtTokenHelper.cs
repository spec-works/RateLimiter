using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RateLimitClient
{
    /// <summary>
    /// Helper class for parsing JWT tokens and extracting claims.
    /// This is a simple implementation without full JWT validation.
    /// </summary>
    public static class JwtTokenHelper
    {
        /// <summary>
        /// Extracts a claim value from a JWT token without validating the signature.
        /// </summary>
        /// <param name="token">The JWT token (Bearer token)</param>
        /// <param name="claimName">The name of the claim to extract (e.g., "oid", "sub", "email")</param>
        /// <returns>The claim value, or null if not found</returns>
        public static string ExtractClaim(string token, string claimName)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            try
            {
                // Remove "Bearer " prefix if present
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = token.Substring(7);
                }

                // JWT format: header.payload.signature
                var parts = token.Split('.');
                if (parts.Length != 3)
                    return null;

                // Decode the payload (second part)
                var payload = parts[1];

                // Add padding if needed for base64 decoding
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                // Convert from base64url to base64
                payload = payload.Replace('-', '+').Replace('_', '/');

                // Decode base64
                var payloadBytes = Convert.FromBase64String(payload);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);

                // Parse JSON and extract claim
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty(claimName, out var claimElement))
                {
                    return claimElement.GetString();
                }
            }
            catch
            {
                // Return null on any parsing error
                return null;
            }

            return null;
        }

        /// <summary>
        /// Extracts the Object ID (oid) claim from a JWT token.
        /// This is commonly used in Azure AD / Microsoft Identity Platform tokens.
        /// </summary>
        /// <param name="token">The JWT token</param>
        /// <returns>The oid claim value, or null if not found</returns>
        public static string ExtractOid(string token)
        {
            return ExtractClaim(token, "oid");
        }

        /// <summary>
        /// Extracts the Subject (sub) claim from a JWT token.
        /// This is a standard claim representing the user identifier.
        /// </summary>
        /// <param name="token">The JWT token</param>
        /// <returns>The sub claim value, or null if not found</returns>
        public static string ExtractSub(string token)
        {
            return ExtractClaim(token, "sub");
        }

        /// <summary>
        /// Extracts all claims from a JWT token.
        /// </summary>
        /// <param name="token">The JWT token</param>
        /// <returns>Dictionary of claim names to values, or empty dictionary on error</returns>
        public static Dictionary<string, string> ExtractAllClaims(string token)
        {
            var claims = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(token))
                return claims;

            try
            {
                // Remove "Bearer " prefix if present
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = token.Substring(7);
                }

                var parts = token.Split('.');
                if (parts.Length != 3)
                    return claims;

                var payload = parts[1];

                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                payload = payload.Replace('-', '+').Replace('_', '/');
                var payloadBytes = Convert.FromBase64String(payload);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);

                using var doc = JsonDocument.Parse(payloadJson);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    claims[property.Name] = property.Value.ToString();
                }
            }
            catch
            {
                // Return empty dictionary on error
            }

            return claims;
        }

        /// <summary>
        /// Creates a sample JWT token for testing purposes.
        /// Note: This creates an unsigned token for demonstration only.
        /// </summary>
        public static string CreateSampleToken(string oid, string name = null, string email = null)
        {
            var header = new
            {
                alg = "none",
                typ = "JWT"
            };

            var payload = new Dictionary<string, object>
            {
                ["oid"] = oid,
                ["sub"] = oid,
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            };

            if (!string.IsNullOrEmpty(name))
                payload["name"] = name;

            if (!string.IsNullOrEmpty(email))
                payload["email"] = email;

            var headerJson = JsonSerializer.Serialize(header);
            var payloadJson = JsonSerializer.Serialize(payload);

            var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
            var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

            return $"{headerBase64}.{payloadBase64}.";
        }

        private static string Base64UrlEncode(byte[] input)
        {
            var base64 = Convert.ToBase64String(input);
            return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
