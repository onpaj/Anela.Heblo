using System.Net;
using System.Text.Json;
using Anela.Heblo.Adapters.Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Anthropic;

public class AnthropicChatClientTests
{
    private static AnthropicOptions DefaultOptions => new()
    {
        ApiKey = "test-api-key",
        Model = "claude-sonnet-4-6",
        MaxTokens = 1024,
        MessagesUrl = "https://api.anthropic.com/v1/messages",
        HttpTimeoutSeconds = 60
    };

    private static AnthropicChatClient CreateClient(
        AnthropicOptions? options = null,
        HttpMessageHandler? handler = null)
    {
        var opts = options ?? DefaultOptions;

        var mockHandler = handler ?? new Mock<HttpMessageHandler>().Object;
        var httpClient = new HttpClient(mockHandler)
        {
            Timeout = TimeSpan.FromSeconds(opts.HttpTimeoutSeconds)
        };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient("Anthropic"))
            .Returns(httpClient);

        return new AnthropicChatClient(
            Options.Create(opts),
            factoryMock.Object,
            NullLogger<AnthropicChatClient>.Instance);
    }

    private static Mock<HttpMessageHandler> CreateHandlerMock(
        HttpStatusCode statusCode,
        string responseJson)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson)
            });
        return handlerMock;
    }

    private static string BuildSuccessJson(string text) =>
        JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text } }
        });

    [Fact]
    public async Task GetResponseAsync_SuccessResponse_ReturnsAssistantText()
    {
        const string expectedText = "Hello from Claude";
        var handlerMock = CreateHandlerMock(HttpStatusCode.OK, BuildSuccessJson(expectedText));
        var client = CreateClient(handler: handlerMock.Object);

        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };
        var response = await client.GetResponseAsync(messages);

        Assert.Equal(expectedText, response.Text);
    }

    [Fact]
    public async Task GetResponseAsync_ApiKeyNotConfigured_ThrowsInvalidOperationException()
    {
        var options = DefaultOptions;
        options.ApiKey = "";
        var client = CreateClient(options);

        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetResponseAsync(messages));
    }

    [Fact]
    public async Task GetResponseAsync_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        var handlerMock = CreateHandlerMock(
            HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit exceeded"}}""");

        var client = CreateClient(handler: handlerMock.Object);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        // Polly will retry 3 times then propagate
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync(messages));
    }

    [Fact]
    public void GetStreamingResponseAsync_ThrowsNotSupportedException()
    {
        var client = CreateClient();
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        Assert.Throws<NotSupportedException>(
            () => client.GetStreamingResponseAsync(messages));
    }

    [Fact]
    public async Task GetResponseAsync_TimeoutException_IsRetriedByPolly()
    {
        int callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((_, _) =>
            {
                callCount++;
                if (callCount < 4)
                    throw new TimeoutException("Simulated timeout");

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildSuccessJson("ok after retries"))
                });
            });

        var client = CreateClient(handler: handlerMock.Object);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        var response = await client.GetResponseAsync(messages);

        Assert.Equal("ok after retries", response.Text);
        Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task GetResponseAsync_TaskCanceledException_IsRetriedByPolly()
    {
        int callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((_, _) =>
            {
                callCount++;
                if (callCount < 4)
                    throw new TaskCanceledException("Simulated task cancellation");

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildSuccessJson("ok after retries"))
                });
            });

        var client = CreateClient(handler: handlerMock.Object);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        var response = await client.GetResponseAsync(messages);

        Assert.Equal("ok after retries", response.Text);
        Assert.Equal(4, callCount);
    }

    [Fact]
    public void AnthropicOptions_DefaultHttpTimeoutSeconds_Is60()
    {
        var options = new AnthropicOptions();

        Assert.Equal(60, options.HttpTimeoutSeconds);
    }
}
