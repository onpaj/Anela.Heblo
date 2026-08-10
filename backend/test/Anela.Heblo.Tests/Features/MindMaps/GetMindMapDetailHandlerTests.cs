using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class GetMindMapDetailHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenMapMissing()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMap?)null);
        var handler = new GetMindMapDetailHandler(_repository.Object);

        var response = await handler.Handle(new GetMindMapDetailRequest { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_MapsMeetingsAndVersions_NewestFirst()
    {
        var meetingId = Guid.NewGuid();
        var map = new MindMap
        {
            Id = Guid.NewGuid(),
            Name = "Mapa",
            CurrentJson = "{}",
            Status = MindMapStatus.Failed,
            LastError = "chyba"
        };
        map.Meetings.Add(new MindMapMeeting
        {
            MeetingTranscriptId = meetingId,
            AttachedAt = new DateTime(2026, 8, 1),
            MeetingTranscript = new MeetingTranscript
            {
                Id = meetingId,
                PlaudRecordingId = "r",
                Subject = "Porada",
                Summary = "s",
                RawTranscript = "t",
                PlaudCreatedAt = new DateTime(2026, 7, 30)
            }
        });
        map.Versions.Add(new MindMapVersion { VersionNumber = 1, Json = "{}", TriggerMeetingId = meetingId });
        map.Versions.Add(new MindMapVersion { VersionNumber = 2, Json = "{}" });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var handler = new GetMindMapDetailHandler(_repository.Object);

        var response = await handler.Handle(new GetMindMapDetailRequest { Id = map.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Failed", response.Status);
        Assert.Equal("chyba", response.LastError);
        Assert.Equal("Porada", response.Meetings.Single().Subject);
        Assert.Equal(2, response.Versions.First().VersionNumber);
        Assert.Equal("Porada", response.Versions.Last().TriggerMeetingSubject);
    }
}
