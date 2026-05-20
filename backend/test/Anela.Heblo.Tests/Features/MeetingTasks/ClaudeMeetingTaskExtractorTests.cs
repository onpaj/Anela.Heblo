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
    private readonly Mock<IMeetingUserDirectory> _mockDirectory;
    private readonly ClaudeMeetingTaskExtractor _extractor;

    public ClaudeMeetingTaskExtractorTests()
    {
        _mockChatClient = new Mock<IChatClient>();
        _mockLogger = new Mock<ILogger<ClaudeMeetingTaskExtractor>>();
        _mockDirectory = new Mock<IMeetingUserDirectory>();
        _mockDirectory.Setup(d => d.GetAll()).Returns(new List<MeetingUser>
        {
            new("andrea@anela.cz", "Andrea Nováková", new[] { "Andy" }),
        });
        _extractor = new ClaudeMeetingTaskExtractor(
            _mockChatClient.Object, _mockDirectory.Object, _mockLogger.Object);
    }

    private void SetupResponse(string json)
    {
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, json)]);
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);
    }

    [Fact]
    public async Task ExtractAsync_WithValidJsonResponse_ReturnsParsedTasks()
    {
        SetupResponse("""[{"title":"Meeting Action","description":"Follow up","assignee":"John","assigneeEmail":null,"dueDate":"2026-06-01"}]""");

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Meeting Action");
        result[0].Assignee.Should().Be("John");
        result[0].AssigneeEmail.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ParsesAssigneeEmailWhenLlmMatchesUser()
    {
        SetupResponse("""[{"title":"T","description":"D","assignee":"Andrea Nováková","assigneeEmail":"andrea@anela.cz","dueDate":null}]""");

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result[0].AssigneeEmail.Should().Be("andrea@anela.cz");
    }

    [Fact]
    public async Task ExtractAsync_IncludesDirectoryUsersInPrompt()
    {
        SetupResponse("[]");

        await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        _mockChatClient.Verify(x => x.GetResponseAsync(
            It.Is<IEnumerable<ChatMessage>>(msgs =>
                msgs.Any(m => m.Role == ChatRole.System && m.Text!.Contains("andrea@anela.cz"))),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_WhenChatClientThrows_ReturnsEmptyList()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_WithMarkdownWrappedJson_StripsFenceAndParses()
    {
        SetupResponse("```json\n[{\"title\":\"Action\",\"description\":\"Do it\",\"assignee\":\"Bob\",\"assigneeEmail\":null,\"dueDate\":null}]\n```");

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Action");
    }

    [Fact]
    public async Task ExtractAsync_PassesMaxOutputTokens8192ToChatClient()
    {
        ChatOptions? capturedOptions = null;
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "[]")]));

        await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        capturedOptions.Should().NotBeNull();
        capturedOptions!.MaxOutputTokens.Should().Be(8192);
    }

    [Fact]
    public async Task ExtractAsync_WhenJsonInvalid_LogsErrorAndReturnsEmpty()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "not-valid-json{{{")]));

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Should().BeEmpty();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("malformed JSON")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_WhenResponseIsEmptyArray_LogsWarningAndReturnsEmpty()
    {
        SetupResponse("[]");

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Should().BeEmpty();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("no tasks")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_WhenApiThrows_LogsErrorAndReturnsEmpty()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Should().BeEmpty();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("extraction failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
