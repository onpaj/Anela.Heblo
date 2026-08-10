using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class AttachMeetingHandlerTests
{
    private readonly Mock<IMindMapRepository> _mapRepository = new();
    private readonly Mock<IMeetingTranscriptRepository> _meetingRepository = new();
    private readonly Mock<IMeetingAccessGuard> _accessGuard = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobClient = new();

    private AttachMeetingHandler CreateSut() => new(
        _mapRepository.Object,
        _meetingRepository.Object,
        _accessGuard.Object,
        _backgroundJobClient.Object,
        NullLogger<AttachMeetingHandler>.Instance);

    private static MindMap Map() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Mapa",
        CurrentJson = "{}",
        Status = MindMapStatus.Idle
    };

    private static MeetingTranscript Meeting() => new()
    {
        Id = Guid.NewGuid(),
        PlaudRecordingId = "r",
        Subject = "Porada",
        Summary = "s",
        RawTranscript = "t"
    };

    [Fact]
    public async Task Handle_AttachesMeeting_SetsUpdating_AndEnqueuesJob()
    {
        var map = Map();
        var meeting = Meeting();
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(meeting.Id, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _accessGuard.Setup(g => g.CanAccess(meeting)).Returns(true);

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meeting.Id },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(MindMapStatus.Updating, map.Status);
        Assert.Single(map.Meetings, m => m.MeetingTranscriptId == meeting.Id && m.ProcessedAt == null);
        // Enqueue<T> is an extension over Create(Job, IState)
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Once);
        _mapRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenMapDoesNotExist()
    {
        _mapRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMap?)null);

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = Guid.NewGuid(), MeetingTranscriptId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        _meetingRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenMeetingDoesNotExist()
    {
        var map = Map();
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = Guid.NewGuid() },
            CancellationToken.None);

        // Same error code as the "exists but inaccessible" case above — deliberately not
        // distinguishable, so this endpoint cannot be used to probe which meeting ids exist.
        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        Assert.Empty(map.Meetings);
    }

    [Fact]
    public async Task Handle_ReturnsAlreadyAttached_WhenConcurrentAttachViolatesUniqueConstraint()
    {
        var map = Map();
        var meeting = Meeting();
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(meeting.Id, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _accessGuard.Setup(g => g.CanAccess(meeting)).Returns(true);
        _mapRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("duplicate key"));

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meeting.Id },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapMeetingAlreadyAttached, response.ErrorCode);
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RevertsStatusAndReturnsError_WhenEnqueueThrows()
    {
        var map = Map();
        var meeting = Meeting();
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(meeting.Id, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _accessGuard.Setup(g => g.CanAccess(meeting)).Returns(true);
        _backgroundJobClient.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()))
            .Throws(new InvalidOperationException("storage unavailable"));

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meeting.Id },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.InternalServerError, response.ErrorCode);
        Assert.Equal(MindMapStatus.Idle, map.Status);
        // The join row itself is deliberately NOT rolled back: the attach genuinely
        // succeeded and is already persisted, so the meeting stays attached and pending —
        // Regenerate is the recovery path once Status is no longer Updating.
        Assert.Single(map.Meetings, m => m.MeetingTranscriptId == meeting.Id && m.ProcessedAt == null);
        _mapRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_ReturnsInternalServerError_WhenEnqueueAndCompensatingSaveBothFail()
    {
        var map = Map();
        var meeting = Meeting();
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(meeting.Id, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _accessGuard.Setup(g => g.CanAccess(meeting)).Returns(true);
        _backgroundJobClient.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()))
            .Throws(new InvalidOperationException("storage unavailable"));
        // First save (persisting the attach) succeeds; the compensating revert save then fails too.
        _mapRepository.SetupSequence(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new DbUpdateException("revert save also failed"));

        // A double failure must still surface as a structured response, not an unhandled
        // exception escaping Handle (which would otherwise mask the original enqueue failure
        // behind a raw 500 and lose it from the response entirely).
        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meeting.Id },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.InternalServerError, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenUserCannotAccessMeeting()
    {
        var map = Map();
        var meeting = Meeting();
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(meeting.Id, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _accessGuard.Setup(g => g.CanAccess(meeting)).Returns(false);

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meeting.Id },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        Assert.Empty(map.Meetings);
    }

    [Fact]
    public async Task Handle_ReturnsAlreadyAttached_WhenLinkExists()
    {
        var map = Map();
        var meeting = Meeting();
        map.Meetings.Add(new MindMapMeeting { MeetingTranscriptId = meeting.Id });
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(meeting.Id, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _accessGuard.Setup(g => g.CanAccess(meeting)).Returns(true);

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meeting.Id },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapMeetingAlreadyAttached, response.ErrorCode);
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Never);
    }
}
