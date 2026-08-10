using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapUpdateJobTests
{
    private readonly Mock<IMindMapRepository> _repository = new();
    private readonly Mock<IMindMapUpdater> _updater = new();

    private MindMapUpdateJob CreateSut() => new(
        _repository.Object,
        _updater.Object,
        new MindMapGuard(),
        NullLogger<MindMapUpdateJob>.Instance);

    private static MindMap MapWithMeetings(params (Guid id, DateTime createdAt)[] meetings)
    {
        var doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Projekt" } }
        };
        var map = new MindMap
        {
            Id = Guid.NewGuid(),
            Name = "Projekt",
            Status = MindMapStatus.Updating,
            CurrentJson = MindMapJson.Serialize(doc)
        };
        foreach (var (id, createdAt) in meetings)
        {
            map.Meetings.Add(new MindMapMeeting
            {
                Id = Guid.NewGuid(),
                MindMapId = map.Id,
                MeetingTranscriptId = id,
                MeetingTranscript = new MeetingTranscript
                {
                    Id = id,
                    PlaudRecordingId = id.ToString(),
                    Subject = $"Porada {createdAt:d}",
                    Summary = "s",
                    RawTranscript = "t",
                    PlaudCreatedAt = createdAt
                }
            });
        }
        return map;
    }

    /// <summary>Updater echo: returns the current doc plus one node named after the meeting.</summary>
    private void UpdaterAddsNodePerMeeting()
    {
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript meeting, CancellationToken _) =>
            {
                var next = MindMapJson.Clone(current);
                next.Nodes.Add(new MindMapNode
                {
                    Id = $"new-{meeting.Id:N}",
                    ParentId = next.RootNodeId,
                    Title = meeting.Subject
                });
                return next;
            });
    }

    [Fact]
    public async Task RunAsync_ProcessesPendingMeetingsChronologically_AndEndsIdle()
    {
        var early = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var late = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(late, early); // attached out of order
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var processedOrder = new List<Guid>();
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MindMapDocument, MeetingTranscript, CancellationToken>((_, m, _) => processedOrder.Add(m.Id))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript _, CancellationToken _) => MindMapJson.Clone(current));

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(new[] { early.Item1, late.Item1 }, processedOrder);
        Assert.Equal(MindMapStatus.Idle, map.Status);
        Assert.Null(map.LastError);
        Assert.All(map.Meetings, m => Assert.NotNull(m.ProcessedAt));
    }

    [Fact]
    public async Task RunAsync_SnapshotsPreviousVersion_PerProcessedMeeting()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        var originalJson = map.CurrentJson;
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        UpdaterAddsNodePerMeeting();

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(2, map.Versions.Count);
        var v1 = map.Versions.Single(v => v.VersionNumber == 1);
        Assert.Equal(originalJson, v1.Json);
        Assert.Equal(m1.Item1, v1.TriggerMeetingId);
        Assert.Equal(3, MindMapJson.Deserialize(map.CurrentJson).Nodes.Count);
    }

    [Fact]
    public async Task RunAsync_SetsFailedAndStops_WhenUpdaterThrows_KeepingCurrentJson()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        var originalJson = map.CurrentJson;
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MindMapUpdateException("LLM vrátil nevalidní dokument"));

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Failed, map.Status);
        Assert.Contains("nevalidní", map.LastError);
        Assert.Equal(originalJson, map.CurrentJson);
        Assert.All(map.Meetings, m => Assert.Null(m.ProcessedAt));
        Assert.Empty(map.Versions);
    }

    [Fact]
    public async Task RunAsync_KeepsEarlierProgress_WhenSecondMeetingFails()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var call = 0;
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript _, CancellationToken _) =>
            {
                if (++call == 2) throw new MindMapUpdateException("selhalo");
                return MindMapJson.Clone(current);
            });

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Failed, map.Status);
        Assert.NotNull(map.Meetings.Single(m => m.MeetingTranscriptId == m1.Item1).ProcessedAt);
        Assert.Null(map.Meetings.Single(m => m.MeetingTranscriptId == m2.Item1).ProcessedAt);
        Assert.Single(map.Versions);
    }

    [Fact]
    public async Task RunAsync_ReturnsQuietly_WhenMapMissing()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMap?)null);

        await CreateSut().RunAsync(Guid.NewGuid(), CancellationToken.None);

        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_EndsIdle_WhenNothingPending()
    {
        var map = MapWithMeetings();
        map.Status = MindMapStatus.Updating;
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Idle, map.Status);
        _updater.Verify(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
