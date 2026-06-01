using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class ExplainSummaryHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repoMock = new();
    private readonly Mock<IMeetingSummaryExplainer> _explainerMock = new();
    private readonly Mock<IMeetingAccessGuard> _guardMock = new();
    private readonly ExplainSummaryHandler _sut;

    public ExplainSummaryHandlerTests()
    {
        _guardMock.Setup(g => g.CanAccess(It.IsAny<MeetingTranscript>())).Returns(true);
        _sut = new ExplainSummaryHandler(
            _repoMock.Object,
            _explainerMock.Object,
            _guardMock.Object,
            NullLogger<ExplainSummaryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsRequiredFieldMissing_WhenSelectedTextIsEmpty()
    {
        // Arrange
        var request = new ExplainSummaryRequest { TranscriptId = Guid.NewGuid(), SelectedText = "   " };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        _repoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenTranscriptDoesNotExist()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        var request = new ExplainSummaryRequest { TranscriptId = transcriptId, SelectedText = "some text" };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _explainerMock.Verify(
            e => e.ExplainAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsExplanation_OnSuccess()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            PlaudRecordingId = "rec-001",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Test meeting",
            Summary = "Meeting summary",
            RawTranscript = "Full raw transcript text here.",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
        };

        _repoMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        _explainerMock
            .Setup(e => e.ExplainAsync(
                transcript.RawTranscript,
                "some selected text",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingSummaryExplanation
            {
                RelevantTranscript = "relevant slice",
                Explanation = "because of this",
            });

        var request = new ExplainSummaryRequest { TranscriptId = transcriptId, SelectedText = "some selected text" };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.RelevantTranscript.Should().Be("relevant slice");
        response.Explanation.Should().Be("because of this");
    }
}
