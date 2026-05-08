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
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppApiClientTests
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

    [Fact]
    public async Task SearchConversationsAsync_ReturnsItems_WhenApiResponds()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            total = 2,
            after = (string?)null,
            items = new[]
            {
                new
                {
                    id = "conv-1",
                    status = "open",
                    unread = true,
                    createdAt = "2026-05-01T10:00:00Z",
                    updatedAt = "2026-05-01T11:00:00Z",
                    contact = new { name = "Jan Novák", email = "jan@test.cz", avatarUrl = (string?)null },
                    lastMessage = new { text = "Dobrý den", createdAt = "2026-05-01T11:00:00Z" }
                }
            }
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.SearchConversationsAsync(null, null, 50, CancellationToken.None);

        // Assert
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be("conv-1");
        result.Items[0].ContactName.Should().Be("Jan Novák");
        result.Items[0].Unread.Should().BeTrue();
    }

    [Fact]
    public async Task SearchConversationsAsync_ThrowsHttpRequestException_On429()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        // Pass ResiliencePipeline.Empty so no retries — test doesn't sleep
        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        // Act
        var act = () => client.SearchConversationsAsync(null, null, 50, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task GetConversationMessagesAsync_ReturnsMessages_WhenApiResponds()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    id = "msg-1",
                    createdAt = "2026-05-01T10:05:00Z",
                    author = new { type = "visitor", name = "Jan Novák" },
                    content = new { text = "Dobrý den, potřebuji pomoc." }
                },
                new
                {
                    id = "msg-2",
                    createdAt = "2026-05-01T10:06:00Z",
                    author = new { type = "agent", name = "Anela podpora" },
                    content = new { text = "Jak vám mohu pomoct?" }
                }
            }
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.GetConversationMessagesAsync("conv-1", CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("msg-1");
        result[0].AuthorType.Should().Be("visitor");
        result[0].Content.Should().Be("Dobrý den, potřebuji pomoc.");
        result[1].AuthorType.Should().Be("agent");
    }

    [Fact]
    public async Task GetConversationMessagesAsync_ThrowsHttpRequestException_OnErrorResponse()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        // Act
        var act = () => client.GetConversationMessagesAsync("conv-missing", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.NotFound);
    }
}
