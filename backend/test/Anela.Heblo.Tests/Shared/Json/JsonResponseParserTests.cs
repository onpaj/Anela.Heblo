using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Shared.Json;

public class JsonResponseParserTests
{
    private sealed record TestOutput(string? Value);

    // Simulates the System.Text.Json behaviour seen in production where a string token
    // is encountered where a number is expected — the internal reader throws
    // InvalidOperationException directly rather than the more common JsonException.
    private sealed class ThrowsInvalidOperationConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new InvalidOperationException("Cannot get the value of a token type 'String' as a number.");

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }

    private sealed class TestOutputWithStringId
    {
        [JsonPropertyName("id")]
        [JsonConverter(typeof(ThrowsInvalidOperationConverter))]
        public int Id { get; set; }
    }

    private static TestOutput? Run(string raw)
        => JsonResponseParser.ParseOrFallback<TestOutput>(raw, null!, NullLogger.Instance);

    [Fact]
    public void ParseOrFallback_NoFences_ValidJson_Returns_Deserialized()
    {
        var result = Run("""{"Value":"hello"}""");
        result!.Value.Should().Be("hello");
    }

    [Fact]
    public void ParseOrFallback_WellFormedFenceBlock_Returns_Deserialized()
    {
        var result = Run("```json\n{\"Value\":\"hello\"}\n```");
        result!.Value.Should().Be("hello");
    }

    [Fact]
    public void ParseOrFallback_MissingClosingFence_ValidJsonBody_Returns_Deserialized()
    {
        // Opening fence present, closing fence absent — truncated response simulation
        var result = Run("```json\n{\"Value\":\"hello\"}");
        result!.Value.Should().Be("hello");
    }

    [Fact]
    public void ParseOrFallback_InlineFenceNoNewline_Returns_Deserialized()
    {
        // ```json immediately followed by JSON with no newline separator
        var result = Run("```json{\"Value\":\"hello\"}");
        result!.Value.Should().Be("hello");
    }

    [Fact]
    public void ParseOrFallback_UntypedFence_Returns_Deserialized()
    {
        var result = Run("```\n{\"Value\":\"hello\"}\n```");
        result!.Value.Should().Be("hello");
    }

    [Fact]
    public void ParseOrFallback_TruncatedJsonWithFence_Returns_Fallback_NotRawFencedText()
    {
        // Truncated mid-JSON — parse must fail cleanly, returning the fallback
        var fallback = new TestOutput("FALLBACK");
        var raw = "```json\n{\"Value\":\"trun";   // truncated — no closing quote, brace or fence
        var result = JsonResponseParser.ParseOrFallback(raw, fallback, NullLogger.Instance);

        result.Should().BeSameAs(fallback);   // fallback returned, not the raw blob
    }

    [Fact]
    public void ParseOrFallback_EmptyString_Returns_Fallback()
    {
        var fallback = new TestOutput("FALLBACK");
        var result = JsonResponseParser.ParseOrFallback("", fallback, NullLogger.Instance);
        result.Should().BeSameAs(fallback);
    }

    [Fact]
    public void TryParse_ValidJson_ReturnsTrue_AndPopulatesResult()
    {
        var success = JsonResponseParser.TryParse<TestOutput>(
            """{"Value":"parsed"}""", out var result, NullLogger.Instance);

        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Value.Should().Be("parsed");
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse_AndResultIsNull()
    {
        var success = JsonResponseParser.TryParse<TestOutput>(
            "", out var result, NullLogger.Instance);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsFalse_AndResultIsNull()
    {
        var success = JsonResponseParser.TryParse<TestOutput>(
            "not json at all", out var result, NullLogger.Instance);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void ParseOrFallback_InvalidOperationExceptionFromConverter_Returns_Fallback()
    {
        var fallback = new TestOutputWithStringId { Id = -1 };
        var result = JsonResponseParser.ParseOrFallback(
            """{"id":42}""", fallback, NullLogger.Instance);

        result.Should().BeSameAs(fallback);
    }

    [Fact]
    public void TryParse_InvalidOperationExceptionFromConverter_ReturnsFalse_AndResultIsNull()
    {
        var success = JsonResponseParser.TryParse<TestOutputWithStringId>(
            """{"id":42}""", out var result, NullLogger.Instance);

        success.Should().BeFalse();
        result.Should().BeNull();
    }
}
