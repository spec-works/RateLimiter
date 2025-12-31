using System.Linq;
using Xunit;
using RateLimitClient.StructuredFields;

namespace RateLimitClient.Tests
{
    /// <summary>
    /// Tests for the StructuredFieldParser implementation.
    /// These tests verify RFC 9651 compliance for structured field parsing.
    /// </summary>
    public class StructuredFieldParserTests
    {
        private readonly IStructuredFieldParser _parser = new StructuredFieldParser();

        [Fact]
        public void ParseList_SingleItem_ParsesCorrectly()
        {
            // Arrange
            var input = "\"test\"";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Single(items);
            Assert.Equal("test", items[0].Value);
        }

        [Fact]
        public void ParseList_MultipleItems_ParsesAll()
        {
            // Arrange
            var input = "\"item1\",\"item2\",\"item3\"";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Equal(3, items.Count);
            Assert.Equal("item1", items[0].Value);
            Assert.Equal("item2", items[1].Value);
            Assert.Equal("item3", items[2].Value);
        }

        [Fact]
        public void ParseList_WithParameters_ParsesCorrectly()
        {
            // Arrange
            var input = "\"item\";param1=value1;param2=value2";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Single(items);
            Assert.Equal("item", items[0].Value);
            Assert.Equal("value1", items[0].GetParameter("param1"));
            Assert.Equal("value2", items[0].GetParameter("param2"));
        }

        [Fact]
        public void ParseList_WithNumericParameters_ParsesCorrectly()
        {
            // Arrange
            var input = "\"policy\";q=100;w=60";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Single(items);
            Assert.True(items[0].TryGetParameterAsLong("q", out var quota));
            Assert.Equal(100, quota);
            Assert.True(items[0].TryGetParameterAsInt("w", out var window));
            Assert.Equal(60, window);
        }

        [Fact]
        public void ParseList_MultipleItemsWithParameters_ParsesAll()
        {
            // Arrange
            var input = "\"burst\";q=100;w=60,\"hourly\";q=1000;w=3600";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Equal(2, items.Count);

            Assert.Equal("burst", items[0].Value);
            Assert.True(items[0].TryGetParameterAsLong("q", out var quota1));
            Assert.Equal(100, quota1);

            Assert.Equal("hourly", items[1].Value);
            Assert.True(items[1].TryGetParameterAsLong("q", out var quota2));
            Assert.Equal(1000, quota2);
        }

        [Fact]
        public void ParseList_WithCommaInQuotes_DoesNotSplit()
        {
            // Arrange
            var input = "\"item,with,commas\";param=value";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Single(items);
            Assert.Equal("item,with,commas", items[0].Value);
        }

        [Fact]
        public void ParseString_QuotedString_RemovesQuotes()
        {
            // Arrange
            var input = "\"test\"";

            // Act
            var result = _parser.ParseString(input);

            // Assert
            Assert.Equal("test", result);
        }

        [Fact]
        public void ParseString_UnquotedString_ReturnsAsIs()
        {
            // Arrange
            var input = "test";

            // Act
            var result = _parser.ParseString(input);

            // Assert
            Assert.Equal("test", result);
        }

        [Fact]
        public void ParseString_EmptyString_ReturnsEmpty()
        {
            // Arrange
            var input = "";

            // Act
            var result = _parser.ParseString(input);

            // Assert
            Assert.Equal("", result);
        }

        [Fact]
        public void ParseByteSequence_ValidBase64_DecodesCorrectly()
        {
            // Arrange
            var input = ":dXNlci0xMjM0NQ==:"; // "user-12345" in base64

            // Act
            var result = _parser.ParseByteSequence(input);

            // Assert
            Assert.Equal("user-12345", result);
        }

        [Fact]
        public void ParseByteSequence_NonByteSequence_ReturnsAsIs()
        {
            // Arrange
            var input = "not-a-byte-sequence";

            // Act
            var result = _parser.ParseByteSequence(input);

            // Assert
            Assert.Equal("not-a-byte-sequence", result);
        }

        [Fact]
        public void SerializeByteSequence_ValidString_EncodesCorrectly()
        {
            // Arrange
            var input = "user-12345";

            // Act
            var result = _parser.SerializeByteSequence(input);

            // Assert
            Assert.StartsWith(":", result);
            Assert.EndsWith(":", result);

            // Verify it can be decoded back
            var decoded = _parser.ParseByteSequence(result);
            Assert.Equal(input, decoded);
        }

        [Fact]
        public void SerializeByteSequence_SpecialCharacters_EncodesCorrectly()
        {
            // Arrange
            var input = "user-αβγ-123";

            // Act
            var result = _parser.SerializeByteSequence(input);

            // Assert
            var decoded = _parser.ParseByteSequence(result);
            Assert.Equal(input, decoded);
        }

        [Fact]
        public void ParseList_RateLimitPolicyFormat_ParsesCorrectly()
        {
            // Arrange - Example from IETF draft
            var input = "\"peruser\";q=100;w=60;pk=:cHsdsRa894==:";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Single(items);
            Assert.Equal("peruser", items[0].Value);
            Assert.True(items[0].TryGetParameterAsLong("q", out var quota));
            Assert.Equal(100, quota);
            Assert.True(items[0].TryGetParameterAsInt("w", out var window));
            Assert.Equal(60, window);
            Assert.True(items[0].HasParameter("pk"));
        }

        [Fact]
        public void ParseList_RateLimitFormat_ParsesCorrectly()
        {
            // Arrange - Example from IETF draft
            var input = "\"default\";r=999;pk=:dHJpYWwxMjEzMjM=:";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Single(items);
            Assert.Equal("default", items[0].Value);
            Assert.True(items[0].TryGetParameterAsLong("r", out var remaining));
            Assert.Equal(999, remaining);
            Assert.True(items[0].HasParameter("pk"));
        }

        [Fact]
        public void StructuredFieldItem_HasParameter_ReturnsTrueWhenExists()
        {
            // Arrange
            var item = new StructuredFieldItem
            {
                Value = "test"
            };
            item.Parameters["key"] = "value";

            // Act & Assert
            Assert.True(item.HasParameter("key"));
            Assert.False(item.HasParameter("nonexistent"));
        }

        [Fact]
        public void StructuredFieldItem_GetParameter_ReturnsNullWhenNotFound()
        {
            // Arrange
            var item = new StructuredFieldItem
            {
                Value = "test"
            };

            // Act
            var result = item.GetParameter("nonexistent");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void StructuredFieldItem_TryGetParameterAsLong_ReturnsFalseForInvalidNumber()
        {
            // Arrange
            var item = new StructuredFieldItem
            {
                Value = "test"
            };
            item.Parameters["invalid"] = "not-a-number";

            // Act
            var success = item.TryGetParameterAsLong("invalid", out var value);

            // Assert
            Assert.False(success);
            Assert.Equal(0, value);
        }

        [Fact]
        public void ParseList_EmptyString_ReturnsEmptyList()
        {
            // Arrange
            var input = "";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Empty(items);
        }

        [Fact]
        public void ParseList_WhitespaceOnly_ReturnsEmptyList()
        {
            // Arrange
            var input = "   ";

            // Act
            var items = _parser.ParseList(input);

            // Assert
            Assert.Empty(items);
        }
    }
}
