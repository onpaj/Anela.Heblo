using System.Text.Json;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class ClaudeMeetingSummaryExplainerTests
{
    private readonly Mock<IChatClient> _chatClientMock = new();
    private readonly ClaudeMeetingSummaryExplainer _sut;

    public ClaudeMeetingSummaryExplainerTests()
    {
        _sut = new ClaudeMeetingSummaryExplainer(
            _chatClientMock.Object,
            NullLogger<ClaudeMeetingSummaryExplainer>.Instance);
    }

    private void SetupChatResponse(string text)
    {
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);
    }

    [Fact]
    public async Task ExplainAsync_ParsesValidJson()
    {
        var json = JsonSerializer.Serialize(new
        {
            relevantTranscript = "the relevant part",
            explanation = "this is why"
        });
        SetupChatResponse(json);

        var result = await _sut.ExplainAsync("transcript text", "selected", CancellationToken.None);

        result.RelevantTranscript.Should().Be("the relevant part");
        result.Explanation.Should().Be("this is why");
    }

    [Fact]
    public async Task ExplainAsync_StripsFenceBeforeParsing()
    {
        var json = "```json\n{ \"relevantTranscript\": \"sliced\", \"explanation\": \"detail\" }\n```";
        SetupChatResponse(json);

        var result = await _sut.ExplainAsync("transcript", "text", CancellationToken.None);

        result.RelevantTranscript.Should().Be("sliced");
        result.Explanation.Should().Be("detail");
    }

    [Fact]
    public async Task ExplainAsync_ReturnsFallback_OnMalformedJson()
    {
        SetupChatResponse("not json at all");

        var result = await _sut.ExplainAsync("transcript", "text", CancellationToken.None);

        result.RelevantTranscript.Should().Be(string.Empty);
        result.Explanation.Should().Be("Vysvětlení není k dispozici.");
    }

    [Fact]
    public async Task ExplainAsync_ReturnsFallback_WhenChatClientThrows()
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("LLM unavailable"));

        var result = await _sut.ExplainAsync("transcript", "text", CancellationToken.None);

        result.RelevantTranscript.Should().Be(string.Empty);
        result.Explanation.Should().Be("Vysvětlení není k dispozici.");
    }
}
