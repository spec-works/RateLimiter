using System.Collections.Generic;

namespace RateLimitClient.StructuredFields
{
    /// <summary>
    /// Represents a single item from a structured field list with its parameters.
    /// Per RFC 9651, an item consists of a bare item and optional parameters.
    /// </summary>
    public class StructuredFieldItem
    {
        /// <summary>
        /// The primary value of this item (typically a string or token).
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Parameters associated with this item as key-value pairs.
        /// Per RFC 9651, parameters are an ordered map of key-value pairs.
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets a parameter value by key, or null if not found.
        /// </summary>
        public string? GetParameter(string key)
        {
            return Parameters.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// Tries to get a parameter value and parse it as a long integer.
        /// </summary>
        public bool TryGetParameterAsLong(string key, out long value)
        {
            value = 0;
            var paramValue = GetParameter(key);
            return paramValue != null && long.TryParse(paramValue, out value);
        }

        /// <summary>
        /// Tries to get a parameter value and parse it as an integer.
        /// </summary>
        public bool TryGetParameterAsInt(string key, out int value)
        {
            value = 0;
            var paramValue = GetParameter(key);
            return paramValue != null && int.TryParse(paramValue, out value);
        }

        /// <summary>
        /// Checks if a parameter exists.
        /// </summary>
        public bool HasParameter(string key)
        {
            return Parameters.ContainsKey(key);
        }
    }
}
