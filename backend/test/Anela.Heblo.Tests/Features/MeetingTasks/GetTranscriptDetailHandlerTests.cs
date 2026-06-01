using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GetTranscriptDetailHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly Mock<IMeetingAccessGuard> _guardMock;
    private readonly GetTranscriptDetailHandler _handler;

    public GetTranscriptDetailHandlerTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _guardMock = new Mock<IMeetingAccessGuard>();
        _guardMock.Setup(g => g.CanAccess(It.IsAny<MeetingTranscript>())).Returns(true);
        _handler = new GetTranscriptDetailHandler(
            _repositoryMock.Object,
            _guardMock.Object,
            NullLogger<GetTranscriptDetailHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ExistingTranscript_ReturnsDetailWithTasks()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            PlaudRecordingId = "rec-001",
            PlaudCreatedAt = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
            Subject = "Sprint Planning",
            Summary = "Plan the sprint",
            RawTranscript = "",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = new DateTime(2026, 5, 1, 9, 30, 0, DateTimeKind.Utc),
            Tasks = new List<ProposedTask>
            {
                new ProposedTask
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = transcriptId,
                    Title = "Write spec",
                    Description = "Draft the spec",
                    Assignee = "ondra",
                    Status = ProposedTaskStatus.Pending,
                    IsManuallyAdded = false
                }
            }
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        var request = new GetTranscriptDetailRequest { Id = transcriptId };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Transcript.Should().NotBeNull();
        result.Transcript.Id.Should().Be(transcriptId);
        result.Transcript.Subject.Should().Be("Sprint Planning");
        result.Transcript.PlaudRecordingId.Should().Be("rec-001");
        result.Transcript.Status.Should().Be("PendingReview");
        result.Transcript.Tasks.Should().HaveCount(1);
        result.Transcript.Tasks[0].Title.Should().Be("Write spec");
        result.Transcript.Tasks[0].Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Handle_MapsRawTranscript()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            PlaudRecordingId = "rec_1",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Subject",
            Summary = "Summary",
            RawTranscript = "raw transcript text",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>()
        };
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        // Act
        var result = await _handler.Handle(
            new GetTranscriptDetailRequest { Id = transcriptId }, CancellationToken.None);

        // Assert
        result.Transcript!.RawTranscript.Should().Be("raw transcript text");
    }

    [Fact]
    public async Task Handle_MapsAssigneeEmail()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            PlaudRecordingId = "rec_1",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Subject",
            Summary = "Summary",
            RawTranscript = "",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new ProposedTask
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = transcriptId,
                    Title = "T",
                    Description = "D",
                    Assignee = "Andrea Nováková",
                    AssigneeEmail = "andrea@anela.cz",
                    Status = ProposedTaskStatus.Pending
                }
            }
        };
        _repositoryMock
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        // Act
        var result = await _handler.Handle(
            new GetTranscriptDetailRequest { Id = transcriptId }, CancellationToken.None);

        // Assert
        result.Transcript!.Tasks.Single().AssigneeEmail.Should().Be("andrea@anela.cz");
    }

    [Fact]
    public async Task Handle_NonExistentTranscript_ReturnsNotFound()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        var request = new GetTranscriptDetailRequest { Id = missingId };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }
}
