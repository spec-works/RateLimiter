using System.Collections.Generic;

namespace RateLimitClient.StructuredFields
{
    /// <summary>
    /// Interface for parsing HTTP structured fields per RFC 9651.
    /// This abstraction allows the parsing implementation to be swapped
    /// with a third-party library if needed.
    /// </summary>
    public interface IStructuredFieldParser
    {
        /// <summary>
        /// Parses a structured field list value into individual items with their parameters.
        /// Example: "item1";param1=value1,"item2";param2=value2
        /// </summary>
        /// <param name="headerValue">The header value to parse.</param>
        /// <returns>A list of parsed structured field items.</returns>
        IList<StructuredFieldItem> ParseList(string headerValue);

        /// <summary>
        /// Parses a quoted string value, removing surrounding quotes.
        /// Per RFC 9651, strings are delimited with double quotes.
        /// </summary>
        /// <param name="value">The value to unquote.</param>
        /// <returns>The unquoted string value.</returns>
        string ParseString(string value);

        /// <summary>
        /// Parses a byte sequence value from structured field format.
        /// Per RFC 9651, byte sequences are formatted as :base64data:
        /// </summary>
        /// <param name="value">The value to parse.</param>
        /// <returns>The decoded string value.</returns>
        string ParseByteSequence(string value);

        /// <summary>
        /// Serializes a string value as a byte sequence for structured field format.
        /// Per RFC 9651, byte sequences are formatted as :base64data:
        /// </summary>
        /// <param name="value">The string value to serialize.</param>
        /// <returns>The serialized byte sequence.</returns>
        string SerializeByteSequence(string value);
    }
}
