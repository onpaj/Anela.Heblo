using Anela.Heblo.Application.Shared.Rag;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.Rag;

public class RagQueryExpanderTests
{
    private readonly Mock<IChatClient> _chatClient = new();
    private readonly Mock<ILogger<RagQueryExpander>> _logger = new();

    private RagQueryExpander CreateExpander() =>
        new(_chatClient.Object, _logger.Object);

    private static RagQueryExpansionConfig EnabledConfig(string model = "test-model", string prompt = "Expand:") =>
        new(Enabled: true, Model: model, Prompt: prompt);

    private void SetupChatClient(string responseText)
    {
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);
    }

    [Fact]
    public async Task ExpandAsync_when_disabled_returns_raw_query_without_calling_chat()
    {
        // Arrange
        var config = new RagQueryExpansionConfig(Enabled: false, Model: "test-model", Prompt: "Expand:");
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("my raw query", config, CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be("my raw query");
        result.ProblemTags.Should().BeEmpty();
        _chatClient.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExpandAsync_when_query_is_whitespace_returns_raw_without_calling_chat()
    {
        // Arrange
        var config = EnabledConfig();
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("   ", config, CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be("   ");
        _chatClient.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExpandAsync_when_enabled_returns_chat_response()
    {
        // Arrange
        SetupChatClient("EXPANDED");
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("original query", EnabledConfig(), CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be("EXPANDED");
    }

    [Fact]
    public async Task ExpandAsync_when_enabled_uses_configured_model()
    {
        // Arrange
        ChatOptions? capturedOptions = null;
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "expanded")]);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(chatResponse);

        var config = EnabledConfig(model: "claude-haiku-4-5-20251001");
        var expander = CreateExpander();

        // Act
        await expander.ExpandAsync("query", config, CancellationToken.None);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ModelId.Should().Be("claude-haiku-4-5-20251001");
    }

    [Fact]
    public async Task ExpandAsync_when_chat_returns_whitespace_falls_back_to_raw_query()
    {
        // Arrange
        SetupChatClient("  ");
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("original query", EnabledConfig(), CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be("original query");
        result.ProblemTags.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task ExpandAsync_on_transient_exception_falls_back_to_raw_query(Type exceptionType)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "transient failure")!;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("original query", EnabledConfig(), CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be("original query");
    }

    [Fact]
    public async Task ExpandAsync_when_enabled_sends_prompt_and_query_to_chat()
    {
        // Arrange
        IEnumerable<ChatMessage>? capturedMessages = null;
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "expanded")]);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (messages, _, _) => capturedMessages = messages)
            .ReturnsAsync(chatResponse);

        var config = EnabledConfig(prompt: "MyPrompt");
        var expander = CreateExpander();

        // Act
        await expander.ExpandAsync("myquery", config, CancellationToken.None);

        // Assert
        capturedMessages.Should().NotBeNull();
        var messageList = capturedMessages!.ToList();
        messageList.Should().HaveCount(1);
        messageList[0].Text.Should().Be("MyPrompt\nmyquery");
    }

    [Fact]
    public async Task ExpandAsync_JsonResponse_ReturnsExpandedQueryAndTags()
    {
        // Arrange
        SetupChatClient("""{"expanded": "Problém: akné", "problemTags": ["akné"], "productTags": ["hořčíkové máslo"]}""");
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("mám akné, pomůže máslo?", EnabledConfig(), CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be("Problém: akné");
        result.ProblemTags.Should().Equal("akné");
        result.ProductTags.Should().Equal("hořčíkové máslo");
    }

    [Fact]
    public async Task ExpandAsync_MalformedJson_LogsWarningAndEmbedsRawResponse()
    {
        // Arrange
        const string malformed = "{\"expanded\": \"Problém: ak";
        SetupChatClient(malformed);
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("dotaz", EnabledConfig(), CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be(malformed);
        result.ProblemTags.Should().BeEmpty();
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExpandAsync_PlainTextResponse_DoesNotLogWarning()
    {
        // Arrange
        SetupChatClient("Problém: popraskané ruce");
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("dotaz", EnabledConfig(), CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be("Problém: popraskané ruce");
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
