using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdJsonValuesTests
{
    [Theory]
    [InlineData("{\"status\":\"PAUSED\"}")]
    [InlineData("\"Paused\"")]
    [InlineData("42")]
    public void Valid_json_passes_through(string json) => AdJsonValues.Normalize(json).Should().Be(json);

    [Fact]
    public void Bare_text_becomes_a_json_string()
    {
        // Act
        var normalized = AdJsonValues.Normalize("PAUSED");

        // Assert — parse, never substring-match JSON
        JsonDocument.Parse(normalized!).RootElement.GetString().Should().Be("PAUSED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Blank_is_null(string? value) => AdJsonValues.Normalize(value).Should().BeNull();
}
