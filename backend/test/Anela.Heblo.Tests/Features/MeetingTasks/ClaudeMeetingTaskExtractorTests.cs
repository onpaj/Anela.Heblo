using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class ClaudeMeetingTaskExtractorTests
{
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly Mock<ILogger<ClaudeMeetingTaskExtractor>> _mockLogger;
    private readonly ClaudeMeetingTaskExtractor _extractor;

    public ClaudeMeetingTaskExtractorTests()
    {
        _mockChatClient = new Mock<IChatClient>();
        _mockLogger = new Mock<ILogger<ClaudeMeetingTaskExtractor>>();
        _extractor = new ClaudeMeetingTaskExtractor(_mockChatClient.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ExtractAsync_WithValidJsonResponse_ReturnsParsedTasks()
    {
        // Arrange
        const string summary = "Meeting summary";
        const string transcript = "Meeting transcript";

        var jsonResponse = """[{"title":"Meeting Action","description":"Follow up with client","assignee":"John","dueDate":"2026-06-01"}]""";
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, jsonResponse)]);

        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        // Act
        var result = await _extractor.ExtractAsync(summary, transcript, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Meeting Action");
        result[0].Description.Should().Be("Follow up with client");
        result[0].Assignee.Should().Be("John");
        result[0].DueDate.Should().Be(new DateTime(2026, 6, 1));
    }

    [Fact]
    public async Task ExtractAsync_WhenChatClientThrowsException_ReturnsEmptyList()
    {
        // Arrange
        const string summary = "Meeting summary";
        const string transcript = "Meeting transcript";

        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        // Act
        var result = await _extractor.ExtractAsync(summary, transcript, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        // Verify logger was called
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<HttpRequestException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
