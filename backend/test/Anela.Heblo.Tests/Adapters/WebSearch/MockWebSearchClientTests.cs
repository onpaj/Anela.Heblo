using Anela.Heblo.Adapters.WebSearch;
using Anela.Heblo.Application.Shared.WebSearch;
using FluentAssertions;

namespace Anela.Heblo.Tests.Adapters.WebSearch;

public sealed class MockWebSearchClientTests
{
    [Fact]
    public async Task SearchAsync_AlwaysReturnsTwoHits()
    {
        // Arrange
        var sut = new MockWebSearchClient();

        // Act
        var result = await sut.SearchAsync("cosmetics", new WebSearchOptions());

        // Assert
        result.Hits.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_SetsQueryOnResult()
    {
        // Arrange
        var sut = new MockWebSearchClient();
        const string query = "vitamin C serum";

        // Act
        var result = await sut.SearchAsync(query, new WebSearchOptions());

        // Assert
        result.Query.Should().Be(query);
    }
}
