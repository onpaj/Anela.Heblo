using Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class RegenerateMindMapHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobClient = new();

    private RegenerateMindMapHandler CreateSut() => new(
        _repository.Object,
        _backgroundJobClient.Object,
        NullLogger<RegenerateMindMapHandler>.Instance);

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenMapDoesNotExist()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMap?)null);

        var response = await CreateSut().Handle(new RegenerateMindMapRequest { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsUpdateInProgress_WhenAlreadyUpdating()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Updating };
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new RegenerateMindMapRequest { Id = map.Id }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapUpdateInProgress, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_EnqueuesJob_WhenPendingMeetingsExist()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Failed, LastError = "x" };
        map.Meetings.Add(new MindMapMeeting { MeetingTranscriptId = Guid.NewGuid(), ProcessedAt = null });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new RegenerateMindMapRequest { Id = map.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(MindMapStatus.Updating, map.Status);
        // Regenerating a Failed map must clear the stale error, not just flip the status —
        // otherwise the UI shows "updating" alongside the old error text until the job clears it.
        Assert.Null(map.LastError);
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ClearsFailedState_WhenNothingPending()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Failed, LastError = "x" };
        map.Meetings.Add(new MindMapMeeting { MeetingTranscriptId = Guid.NewGuid(), ProcessedAt = DateTime.UtcNow });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new RegenerateMindMapRequest { Id = map.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(MindMapStatus.Idle, map.Status);
        Assert.Null(map.LastError);
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RevertsStatusAndReturnsError_WhenEnqueueThrows()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Failed, LastError = "x" };
        map.Meetings.Add(new MindMapMeeting { MeetingTranscriptId = Guid.NewGuid(), ProcessedAt = null });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _backgroundJobClient.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()))
            .Throws(new InvalidOperationException("storage unavailable"));

        var response = await CreateSut().Handle(new RegenerateMindMapRequest { Id = map.Id }, CancellationToken.None);

        Assert.Equal(ErrorCodes.InternalServerError, response.ErrorCode);
        Assert.Equal(MindMapStatus.Failed, map.Status);
        // The compensation must restore the original diagnostic message too — not just the
        // status — otherwise a transient enqueue failure silently erases why the map failed.
        Assert.Equal("x", map.LastError);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_ReturnsInternalServerError_WhenEnqueueAndCompensatingSaveBothFail()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Failed, LastError = "x" };
        map.Meetings.Add(new MindMapMeeting { MeetingTranscriptId = Guid.NewGuid(), ProcessedAt = null });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _backgroundJobClient.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()))
            .Throws(new InvalidOperationException("storage unavailable"));
        // First save (setting Updating) succeeds; the compensating revert save then fails too.
        _repository.SetupSequence(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new DbUpdateException("revert save also failed"));

        // A double failure must still surface as a structured response, not an unhandled
        // exception escaping Handle.
        var response = await CreateSut().Handle(new RegenerateMindMapRequest { Id = map.Id }, CancellationToken.None);

        Assert.Equal(ErrorCodes.InternalServerError, response.ErrorCode);
    }
}
