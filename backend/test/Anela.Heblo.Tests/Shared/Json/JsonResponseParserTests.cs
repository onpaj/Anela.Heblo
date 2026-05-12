using Anela.Heblo.Application.Shared.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Shared.Json;

public class JsonResponseParserTests
{
    private sealed record TestOutput(string? Value);

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
}
