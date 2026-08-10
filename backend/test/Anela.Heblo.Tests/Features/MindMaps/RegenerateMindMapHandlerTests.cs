using Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class RegenerateMindMapHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobClient = new();

    private RegenerateMindMapHandler CreateSut() => new(_repository.Object, _backgroundJobClient.Object);

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
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Once);
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
    }
}
