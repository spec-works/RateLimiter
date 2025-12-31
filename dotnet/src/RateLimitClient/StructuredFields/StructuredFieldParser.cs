using System;
using System.Collections.Generic;
using System.Text;

namespace RateLimitClient.StructuredFields
{
    /// <summary>
    /// Default implementation of structured field parsing per RFC 9651.
    /// This implementation can be replaced with a third-party library if needed.
    /// </summary>
    public class StructuredFieldParser : IStructuredFieldParser
    {
        /// <summary>
        /// Parses a structured field list value into individual items with their parameters.
        /// Format: "item1";param1=value1;param2=value2,"item2";param3=value3
        /// </summary>
        public IList<StructuredFieldItem> ParseList(string headerValue)
        {
            var items = new List<StructuredFieldItem>();

            if (string.IsNullOrWhiteSpace(headerValue))
                return items;

            var rawItems = SplitListItems(headerValue);

            foreach (var rawItem in rawItems)
            {
                var item = ParseItem(rawItem);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        /// <summary>
        /// Parses a quoted string value, removing surrounding quotes.
        /// Per RFC 9651, strings are delimited with double quotes.
        /// </summary>
        public string ParseString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }

        /// <summary>
        /// Parses a byte sequence value from structured field format.
        /// Per RFC 9651, byte sequences are formatted as :base64data:
        /// Returns the decoded UTF-8 string value.
        /// </summary>
        public string ParseByteSequence(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            value = value.Trim();

            // Byte sequences are wrapped in colons: :base64data:
            if (value.Length >= 2 && value[0] == ':' && value[value.Length - 1] == ':')
            {
                var base64Content = value.Substring(1, value.Length - 2);

                try
                {
                    // Decode base64 to bytes
                    var bytes = Convert.FromBase64String(base64Content);

                    // Convert bytes to UTF-8 string
                    return Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    // If base64 decoding fails, return the original value
                    return value;
                }
            }

            return value;
        }

        /// <summary>
        /// Serializes a string value as a byte sequence for structured field format.
        /// Per RFC 9651, byte sequences are formatted as :base64data:
        /// </summary>
        public string SerializeByteSequence(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // Convert string to UTF-8 bytes
            var bytes = Encoding.UTF8.GetBytes(value);

            // Encode as base64
            var base64 = Convert.ToBase64String(bytes);

            // Wrap in colons per RFC 9651
            return $":{base64}:";
        }

        /// <summary>
        /// Parses a single item with its parameters from a raw string.
        /// Format: "item";param1=value1;param2=value2
        /// </summary>
        private StructuredFieldItem? ParseItem(string rawItem)
        {
            if (string.IsNullOrWhiteSpace(rawItem))
                return null;

            var item = new StructuredFieldItem();
            var parts = rawItem.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return null;

            // First part is the item value (may be quoted)
            item.Value = ParseString(parts[0].Trim());

            // Parse parameters
            for (int i = 1; i < parts.Length; i++)
            {
                var param = parts[i].Trim();
                var kvp = param.Split(new[] { '=' }, 2);

                if (kvp.Length == 2)
                {
                    var key = kvp[0].Trim();
                    var value = kvp[1].Trim();
                    item.Parameters[key] = value;
                }
                else if (kvp.Length == 1)
                {
                    // Boolean parameter (no value)
                    var key = kvp[0].Trim();
                    item.Parameters[key] = "true";
                }
            }

            return item;
        }

        /// <summary>
        /// Splits a structured field list by commas, respecting quoted strings.
        /// </summary>
        private List<string> SplitListItems(string value)
        {
            var items = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            var escapeNext = false;

            foreach (var ch in value)
            {
                if (escapeNext)
                {
                    current.Append(ch);
                    escapeNext = false;
                    continue;
                }

                if (ch == '\\')
                {
                    current.Append(ch);
                    escapeNext = true;
                    continue;
                }

                if (ch == '"')
                {
                    current.Append(ch);
                    inQuotes = !inQuotes;
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        items.Add(current.ToString().Trim());
                    }
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
            {
                items.Add(current.ToString().Trim());
            }

            return items;
        }
    }
}
