using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.DeleteMeetingTranscript;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class DeleteMeetingTranscriptHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _mockRepository;
    private readonly Mock<IMeetingAccessGuard> _mockAccessGuard;
    private readonly Mock<ICurrentUserService> _mockCurrentUser;
    private readonly Mock<ILogger<DeleteMeetingTranscriptHandler>> _mockLogger;
    private readonly DeleteMeetingTranscriptHandler _handler;

    public DeleteMeetingTranscriptHandlerTests()
    {
        _mockRepository = new Mock<IMeetingTranscriptRepository>();
        _mockAccessGuard = new Mock<IMeetingAccessGuard>();
        _mockCurrentUser = new Mock<ICurrentUserService>();
        _mockLogger = new Mock<ILogger<DeleteMeetingTranscriptHandler>>();

        _mockAccessGuard.Setup(g => g.IsManager()).Returns(true);
        _mockCurrentUser
            .Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("id-1", "Ondra", "ondra@anela.cz", true));

        _handler = new DeleteMeetingTranscriptHandler(
            _mockRepository.Object,
            _mockAccessGuard.Object,
            _mockCurrentUser.Object,
            _mockLogger.Object);
    }

    private MeetingTranscript SetupTranscript(out Guid id)
    {
        id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_1",
            Subject = "Subject",
            Summary = "Summary",
            RawTranscript = "Transcript",
            Status = MeetingTranscriptStatus.PendingReview,
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        return entity;
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotManager_ReturnsForbiddenAndDoesNotDelete()
    {
        // Arrange
        _mockAccessGuard.Setup(g => g.IsManager()).Returns(false);
        SetupTranscript(out var id);

        // Act
        var response = await _handler.Handle(
            new DeleteMeetingTranscriptRequest { TranscriptId = id },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _mockRepository.Verify(
            r => r.DeleteAsync(It.IsAny<MeetingTranscript>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTranscriptDoesNotExist_ReturnsResourceNotFound()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _mockRepository
            .Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        // Act
        var response = await _handler.Handle(
            new DeleteMeetingTranscriptRequest { TranscriptId = missingId },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _mockRepository.Verify(
            r => r.DeleteAsync(It.IsAny<MeetingTranscript>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenManagerDeletesExistingTranscript_DeletesWithCurrentUserEmail()
    {
        // Arrange
        var entity = SetupTranscript(out var id);

        // Act
        var response = await _handler.Handle(
            new DeleteMeetingTranscriptRequest { TranscriptId = id },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _mockRepository.Verify(
            r => r.DeleteAsync(entity, "ondra@anela.cz", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
