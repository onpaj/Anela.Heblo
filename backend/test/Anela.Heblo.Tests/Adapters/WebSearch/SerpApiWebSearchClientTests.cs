using System.Net;
using System.Text;
using Anela.Heblo.Adapters.WebSearch;
using Anela.Heblo.Application.Shared.WebSearch;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Anela.Heblo.Tests.Adapters.WebSearch;

public sealed class SerpApiWebSearchClientTests
{
    private static IOptions<WebSearchAdapterOptions> CreateOptions(string apiKey = "test-api-key") =>
        Options.Create(new WebSearchAdapterOptions
        {
            ApiKey = apiKey,
            Endpoint = "https://serpapi.com/search.json",
            DefaultLocale = "cs",
            DefaultGeo = "cz"
        });

    private static IHttpClientFactory CreateHttpClientFactory(HttpResponseMessage response)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("SerpApi")).Returns(client);
        return factoryMock.Object;
    }

    [Fact]
    public async Task SearchAsync_ReturnsHits_WhenSerpApiReturnsValidOrganicResults()
    {
        // Arrange
        const string json = """
            {
                "organic_results": [
                    { "title": "Result One", "link": "https://example.com/1", "snippet": "First snippet." },
                    { "title": "Result Two", "link": "https://example.com/2", "snippet": "Second snippet." }
                ]
            }
            """;

        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var sut = new SerpApiWebSearchClient(
            CreateOptions(),
            CreateHttpClientFactory(responseMessage),
            NullLogger<SerpApiWebSearchClient>.Instance);

        // Act
        var result = await sut.SearchAsync("test query", new WebSearchOptions());

        // Assert
        result.Query.Should().Be("test query");
        result.Hits.Should().HaveCount(2);
        result.Hits[0].Title.Should().Be("Result One");
        result.Hits[0].Url.Should().Be("https://example.com/1");
        result.Hits[0].Snippet.Should().Be("First snippet.");
        result.Hits[1].Title.Should().Be("Result Two");
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyHits_WhenOrganicResultsIsMissing()
    {
        // Arrange
        const string json = """{ "search_metadata": { "status": "Success" } }""";

        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var sut = new SerpApiWebSearchClient(
            CreateOptions(),
            CreateHttpClientFactory(responseMessage),
            NullLogger<SerpApiWebSearchClient>.Instance);

        // Act
        var result = await sut.SearchAsync("no results query", new WebSearchOptions());

        // Assert
        result.Query.Should().Be("no results query");
        result.Hits.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_Throws_WhenApiKeyIsEmpty()
    {
        // Arrange
        var sut = new SerpApiWebSearchClient(
            CreateOptions(apiKey: ""),
            Mock.Of<IHttpClientFactory>(),
            NullLogger<SerpApiWebSearchClient>.Instance);

        // Act
        var act = async () => await sut.SearchAsync("query", new WebSearchOptions());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WebSearch:ApiKey*");
    }
}
