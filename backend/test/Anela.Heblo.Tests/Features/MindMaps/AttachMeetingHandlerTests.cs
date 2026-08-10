using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
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
