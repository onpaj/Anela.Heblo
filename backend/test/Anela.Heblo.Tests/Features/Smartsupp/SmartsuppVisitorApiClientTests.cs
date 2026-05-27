using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Smartsupp;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Polly;
using Polly.Retry;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppVisitorApiClientTests
{
    private static SmartsuppApiClient CreateClient(HttpMessageHandler handler, ResiliencePipeline? pipeline = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.smartsupp.com/v2/") };
        factory.Setup(f => f.CreateClient("Smartsupp")).Returns(httpClient);
        var options = Options.Create(new SmartsuppOptions
        {
            ApiToken = "test-token",
            BaseUrl = "https://api.smartsupp.com/v2/",
        });
        return new SmartsuppApiClient(options, factory.Object, NullLogger<SmartsuppApiClient>.Instance, pipeline);
    }

    private static Mock<HttpMessageHandler> RespondWith(int statusCode, string? body = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = body is null
                    ? new StringContent("")
                    : new StringContent(body, Encoding.UTF8, "application/json")
            });
        return handler;
    }

    [Fact]
    public async Task GetVisitorAsync_ReturnsVisitor_WhenApiResponds()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            id = "vitABC",
            user_agent = "Mozilla/5.0 (Macintosh) Chrome/148",
            os = "OS X",
            browser = "Chrome",
            browser_version = "148.0.0.0",
            visits = 321,
        });
        var client = CreateClient(RespondWith(200, json).Object);

        // Act
        var result = await client.GetVisitorAsync("vitABC", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("vitABC");
        result.Os.Should().Be("OS X");
        result.Browser.Should().Be("Chrome");
        result.BrowserVersion.Should().Be("148.0.0.0");
        result.VisitsCount.Should().Be(321);
        result.UserAgent.Should().Contain("Chrome");
    }

    [Fact]
    public async Task GetVisitorAsync_ReturnsNull_When404()
    {
        // Arrange
        var client = CreateClient(RespondWith(404).Object);

        // Act
        var result = await client.GetVisitorAsync("unknown", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetVisitorAsync_Retries_On429()
    {
        // Arrange
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 2)
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { id = "v1", os = "OS X", browser = "Safari", browser_version = "26.4", visits = 1 }),
                        Encoding.UTF8, "application/json")
                };
            });

        var immediateRetryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests),
            })
            .Build();

        var client = CreateClient(handler.Object, immediateRetryPipeline);

        // Act
        var result = await client.GetVisitorAsync("v1", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        callCount.Should().Be(2);
    }
}
