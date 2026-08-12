using Anela.Heblo.Application.Features.MindMaps.UseCases.DetachMeeting;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class DetachMeetingHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();

    private DetachMeetingHandler CreateSut() => new(_repository.Object);

    private static MindMap Map() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Mapa",
        CurrentJson = "{}",
        Status = MindMapStatus.Idle
    };

    [Fact]
    public async Task Handle_RemovesLink_WhenMeetingIsAttached()
    {
        var map = Map();
        var meetingId = Guid.NewGuid();
        map.Meetings.Add(new MindMapMeeting { MeetingTranscriptId = meetingId, ProcessedAt = DateTime.UtcNow });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(
            new DetachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meetingId },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Empty(map.Meetings);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenLinkDoesNotExist()
    {
        var map = Map();
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(
            new DetachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenMapDoesNotExist()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMap?)null);

        var response = await CreateSut().Handle(
            new DetachMeetingRequest { MindMapId = Guid.NewGuid(), MeetingTranscriptId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
