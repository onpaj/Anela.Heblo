using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.MeetingTasks;

public class UpdateMeetingAccessHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly Mock<IMeetingAccessGuard> _guardMock;
    private readonly Mock<ICurrentUserService> _userServiceMock;
    private readonly Mock<IMeetingUserDirectory> _directoryMock;
    private readonly UpdateMeetingAccessHandler _handler;

    public UpdateMeetingAccessHandlerTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _guardMock = new Mock<IMeetingAccessGuard>();
        _userServiceMock = new Mock<ICurrentUserService>();
        _directoryMock = new Mock<IMeetingUserDirectory>();

        _handler = new UpdateMeetingAccessHandler(
            _repositoryMock.Object,
            _guardMock.Object,
            _userServiceMock.Object,
            _directoryMock.Object,
            new Mock<ILogger<UpdateMeetingAccessHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenNotManager()
    {
        _guardMock.Setup(x => x.IsManager()).Returns(false);

        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest { TranscriptId = Guid.NewGuid(), AccessLevel = "Public" },
            default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTranscriptMissing()
    {
        _guardMock.Setup(x => x.IsManager()).Returns(true);
        var id = Guid.NewGuid();
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest { TranscriptId = id, AccessLevel = "Public" },
            default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsValidationError_WhenRestrictedWithEmptyEmails()
    {
        var id = Guid.NewGuid();
        SetupManagerWithTranscript(id);

        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest { TranscriptId = id, AccessLevel = "Restricted", RestrictedUserEmails = [] },
            default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Handle_ReturnsValidationError_WhenEmailNotInDirectory()
    {
        var id = Guid.NewGuid();
        SetupManagerWithTranscript(id);
        _directoryMock.Setup(x => x.GetAll()).Returns(
            new List<MeetingUser> { new MeetingUser("known@test.com", "Known User", []) }.AsReadOnly());

        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest { TranscriptId = id, AccessLevel = "Restricted", RestrictedUserEmails = ["unknown@test.com"] },
            default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.Params!["email"].Should().Be("unknown@test.com");
    }

    [Fact]
    public async Task Handle_HappyPath_Public_CallsSetAccess_ReturnsSuccess()
    {
        var id = Guid.NewGuid();
        var transcript = SetupManagerWithTranscript(id);
        _userServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Manager", "manager@test.com", true));

        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest { TranscriptId = id, AccessLevel = "Public", RestrictedUserEmails = [] },
            default);

        result.Success.Should().BeTrue();
        result.AccessLevel.Should().Be("Public");
        _repositoryMock.Verify(
            x => x.SetAccessAsync(transcript, MeetingAccessLevel.Public, It.IsAny<IReadOnlyList<MeetingAccessGrant>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_HappyPath_Restricted_ResolvesGrantsAndCallsSetAccess()
    {
        var id = Guid.NewGuid();
        var transcript = SetupManagerWithTranscript(id);
        _userServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Manager", "manager@test.com", true));
        _directoryMock.Setup(x => x.GetAll()).Returns(
            new List<MeetingUser> { new MeetingUser("user@test.com", "Test User", []) }.AsReadOnly());

        var result = await _handler.Handle(
            new UpdateMeetingAccessRequest
            {
                TranscriptId = id,
                AccessLevel = "Restricted",
                RestrictedUserEmails = ["USER@TEST.COM"]
            },
            default);

        result.Success.Should().BeTrue();
        result.AccessLevel.Should().Be("Restricted");
        result.Grants.Should().ContainSingle(g => g.UserEmail == "user@test.com");
        _repositoryMock.Verify(
            x => x.SetAccessAsync(
                transcript,
                MeetingAccessLevel.Restricted,
                It.Is<IReadOnlyList<MeetingAccessGrant>>(grants =>
                    grants.Count == 1 && grants[0].UserEmail == "user@test.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private MeetingTranscript SetupManagerWithTranscript(Guid id)
    {
        _guardMock.Setup(x => x.IsManager()).Returns(true);
        var transcript = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "test",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Test",
            Summary = "Test",
            RawTranscript = "Test",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            AccessLevel = MeetingAccessLevel.Private,
            AccessGrants = []
        };
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        return transcript;
    }
}
