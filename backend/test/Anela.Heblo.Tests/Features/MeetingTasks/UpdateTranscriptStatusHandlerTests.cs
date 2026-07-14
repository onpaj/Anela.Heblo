using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateTranscriptStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class UpdateTranscriptStatusHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _mockRepository;
    private readonly Mock<IMeetingAccessGuard> _mockAccessGuard;
    private readonly Mock<ICurrentUserService> _mockCurrentUser;
    private readonly Mock<ILogger<UpdateTranscriptStatusHandler>> _mockLogger;
    private readonly UpdateTranscriptStatusHandler _handler;

    public UpdateTranscriptStatusHandlerTests()
    {
        _mockRepository = new Mock<IMeetingTranscriptRepository>();
        _mockAccessGuard = new Mock<IMeetingAccessGuard>();
        _mockCurrentUser = new Mock<ICurrentUserService>();
        _mockLogger = new Mock<ILogger<UpdateTranscriptStatusHandler>>();

        _mockAccessGuard.Setup(g => g.CanAccess(It.IsAny<MeetingTranscript>())).Returns(true);
        _mockCurrentUser
            .Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("id-1", "Ondra", "ondra@anela.cz", true));

        _handler = new UpdateTranscriptStatusHandler(
            _mockRepository.Object,
            _mockAccessGuard.Object,
            _mockCurrentUser.Object,
            _mockLogger.Object);
    }

    private MeetingTranscript SetupTranscript(MeetingTranscriptStatus status, out Guid id)
    {
        id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_1",
            Subject = "Subject",
            Summary = "Summary",
            RawTranscript = "Transcript",
            Status = status,
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return entity;
    }

    [Fact]
    public async Task Handle_WhenMovingToApproved_SetsStatusAndReviewMetadata()
    {
        // Arrange
        var entity = SetupTranscript(MeetingTranscriptStatus.PendingReview, out var id);

        // Act
        var response = await _handler.Handle(
            new UpdateTranscriptStatusRequest { TranscriptId = id, Status = "Approved" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Status.Should().Be("Approved");
        entity.Status.Should().Be(MeetingTranscriptStatus.Approved);
        entity.ReviewedAt.Should().NotBeNull();
        entity.ReviewedByUser.Should().Be("ondra@anela.cz");
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenMovingBackToPendingReview_ClearsReviewMetadata()
    {
        // Arrange
        var entity = SetupTranscript(MeetingTranscriptStatus.Approved, out var id);
        entity.ReviewedAt = DateTime.UtcNow;
        entity.ReviewedByUser = "ondra@anela.cz";

        // Act
        var response = await _handler.Handle(
            new UpdateTranscriptStatusRequest { TranscriptId = id, Status = "PendingReview" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Status.Should().Be("PendingReview");
        entity.Status.Should().Be(MeetingTranscriptStatus.PendingReview);
        entity.ReviewedAt.Should().BeNull();
        entity.ReviewedByUser.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenStatusIsPartiallyApproved_ReturnsValidationError()
    {
        // Arrange
        var entity = SetupTranscript(MeetingTranscriptStatus.PendingReview, out var id);

        // Act
        var response = await _handler.Handle(
            new UpdateTranscriptStatusRequest { TranscriptId = id, Status = "PartiallyApproved" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        entity.Status.Should().Be(MeetingTranscriptStatus.PendingReview);
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenStatusIsUnknown_ReturnsValidationError()
    {
        // Arrange
        SetupTranscript(MeetingTranscriptStatus.PendingReview, out var id);

        // Act
        var response = await _handler.Handle(
            new UpdateTranscriptStatusRequest { TranscriptId = id, Status = "Nonsense" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMeetingNotFound_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        // Act
        var response = await _handler.Handle(
            new UpdateTranscriptStatusRequest { TranscriptId = id, Status = "Approved" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_WhenAccessDenied_ReturnsNotFound()
    {
        // Arrange
        var entity = SetupTranscript(MeetingTranscriptStatus.PendingReview, out var id);
        _mockAccessGuard.Setup(g => g.CanAccess(entity)).Returns(false);

        // Act
        var response = await _handler.Handle(
            new UpdateTranscriptStatusRequest { TranscriptId = id, Status = "Approved" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
