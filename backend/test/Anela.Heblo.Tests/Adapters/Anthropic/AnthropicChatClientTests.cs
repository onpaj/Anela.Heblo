using System.Net;
using System.Text.Json;
using Anela.Heblo.Adapters.Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Polly;
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

    private static readonly ResiliencePipeline InstantRetryPipeline =
        AnthropicChatClient.BuildPipeline(TimeSpan.Zero);

    private static AnthropicChatClient CreateClient(
        AnthropicOptions? options = null,
        HttpMessageHandler? handler = null,
        ResiliencePipeline? pipeline = null)
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
            NullLogger<AnthropicChatClient>.Instance,
            pipeline);
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

        var client = CreateClient(handler: handlerMock.Object, pipeline: InstantRetryPipeline);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        // Polly will retry 3 times then propagate
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync(messages));
    }

    [Fact]
    public async Task GetResponseAsync_529Response_RetriesThreeTimes_ThenThrowsWithCorrectStatusCode()
    {
        int callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage((HttpStatusCode)529)
                {
                    Content = new StringContent("""{"type":"error","error":{"type":"overloaded_error"}}""")
                };
            });

        var client = CreateClient(handler: handlerMock.Object, pipeline: InstantRetryPipeline);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync(messages));

        Assert.Equal((HttpStatusCode)529, ex.StatusCode);
        Assert.Equal(4, callCount); // 1 initial + 3 retries
    }

    [Fact]
    public async Task GetResponseAsync_400Response_DoesNotRetry()
    {
        int callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":"bad_request"}""")
                };
            });

        var client = CreateClient(handler: handlerMock.Object, pipeline: InstantRetryPipeline);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync(messages));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(1, callCount); // no retries
    }

    [Fact]
    public async Task GetResponseAsync_529WithRetryAfterHeader_StoresRetryAfterInException()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)529)
                {
                    Content = new StringContent("""{"type":"error","error":{"type":"overloaded_error"}}""")
                };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return response;
            });

        // Use a pipeline that won't retry so we can inspect the thrown exception directly
        var noRetryPipeline = ResiliencePipeline.Empty;
        var client = CreateClient(handler: handlerMock.Object, pipeline: noRetryPipeline);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync(messages));

        Assert.Equal((HttpStatusCode)529, ex.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), ex.Data["RetryAfter"]);
    }

    [Fact]
    public async Task GetResponseAsync_529SucceedsAfterRetry_ReturnsAnswer()
    {
        int callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 3)
                    return new HttpResponseMessage((HttpStatusCode)529)
                    {
                        Content = new StringContent("""{"type":"error","error":{"type":"overloaded_error"}}""")
                    };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildSuccessJson("Recovered answer"))
                };
            });

        var client = CreateClient(handler: handlerMock.Object, pipeline: InstantRetryPipeline);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        var response = await client.GetResponseAsync(messages);

        Assert.Equal("Recovered answer", response.Text);
        Assert.Equal(3, callCount);
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
    public async Task GetResponseAsync_TimeoutException_PropagatesWithoutRetry()
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
                throw new TimeoutException("Simulated timeout");
            });

        var client = CreateClient(handler: handlerMock.Object);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        await Assert.ThrowsAsync<TimeoutException>(() => client.GetResponseAsync(messages));
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetResponseAsync_TaskCanceledException_PropagatesWithoutRetry()
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
                throw new TaskCanceledException("Simulated task cancellation");
            });

        var client = CreateClient(handler: handlerMock.Object);
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetResponseAsync(messages));
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void AnthropicOptions_DefaultHttpTimeoutSeconds_Is60()
    {
        var options = new AnthropicOptions();

        Assert.Equal(60, options.HttpTimeoutSeconds);
    }
}
