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
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var processedOrder = new List<Guid>();
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MindMapDocument, MeetingTranscript, CancellationToken>((_, m, _) => processedOrder.Add(m.Id))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript _, CancellationToken _) => MindMapJson.Clone(current));

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(new[] { early.Item1, late.Item1 }, processedOrder);
        Assert.Equal(MindMapStatus.Idle, map.Status);
        Assert.Null(map.LastError);
        Assert.All(map.Meetings, m => Assert.NotNull(m.ProcessedAt));
        // One save per processed meeting (2) plus the final Idle save — pins that saving
        // happens per meeting rather than once at the end.
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RunAsync_SnapshotsPreviousVersion_PerProcessedMeeting()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        var originalJson = map.CurrentJson;
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        UpdaterAddsNodePerMeeting();

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(2, map.Versions.Count);
        var v1 = map.Versions.Single(v => v.VersionNumber == 1);
        Assert.Equal(originalJson, v1.Json);
        Assert.Equal(m1.Item1, v1.TriggerMeetingId);
        Assert.Equal(3, MindMapJson.Deserialize(map.CurrentJson).Nodes.Count);
    }

    [Fact]
    public async Task RunAsync_NumbersVersionsFromExistingMax_AndSnapshotsPostMeetingOneJson()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        map.Versions.Add(new MindMapVersion
        {
            Id = Guid.NewGuid(),
            MindMapId = map.Id,
            VersionNumber = 7,
            Json = "{}",
            CreatedAt = DateTime.UtcNow
        });
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        UpdaterAddsNodePerMeeting();

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        // max(existing VersionNumber) + 1, not Count + 1 (3 versions exist, but the seeded
        // one is numbered 7) — proves numbering tracks the max, not the count.
        Assert.Equal(3, map.Versions.Count);
        var v8 = map.Versions.Single(v => v.VersionNumber == 8);
        var v9 = map.Versions.Single(v => v.VersionNumber == 9);
        Assert.Equal(2, MindMapJson.Deserialize(v9.Json).Nodes.Count);
        Assert.Equal(m2.Item1, v9.TriggerMeetingId);
    }

    [Fact]
    public async Task RunAsync_SetsFailedAndStops_WhenGuardRejectsLlmResult_KeepingCurrentJson()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var map = MapWithMeetings(m1);
        var originalJson = map.CurrentJson;
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        // A document the real MindMapGuard rejects: root node id changed.
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript _, CancellationToken _) =>
            {
                var tampered = MindMapJson.Clone(current);
                tampered.RootNodeId = "not-the-root";
                return tampered;
            });

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Failed, map.Status);
        Assert.NotNull(map.LastError);
        Assert.Equal(originalJson, map.CurrentJson);
        Assert.Empty(map.Versions);
        Assert.Null(map.Meetings.Single().ProcessedAt);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.SetFailedAsync(map.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_RethrowsAndMarksFailed_WhenCancelled()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var map = MapWithMeetings(m1);
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("A task was canceled."));

        await Assert.ThrowsAsync<TaskCanceledException>(() => CreateSut().RunAsync(map.Id, CancellationToken.None));

        Assert.Equal(MindMapStatus.Failed, map.Status);
        _repository.Verify(r => r.SetFailedAsync(map.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_SetsFailedAndStops_WhenUpdaterThrows_KeepingCurrentJson()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        var originalJson = map.CurrentJson;
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MindMapUpdateException("LLM vrátil nevalidní dokument"));

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Failed, map.Status);
        Assert.Contains("nevalidní", map.LastError);
        Assert.Equal(originalJson, map.CurrentJson);
        Assert.All(map.Meetings, m => Assert.Null(m.ProcessedAt));
        Assert.Empty(map.Versions);
        // Failure is persisted via the change-tracker-free SetFailedAsync, never via a
        // tracked SaveChangesAsync (which could resubmit a poisoned change set).
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.SetFailedAsync(map.Id, map.LastError!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_KeepsEarlierProgress_WhenSecondMeetingFails()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
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
        // Exactly one tracked save (meeting 1's success) — meeting 2's failure is persisted
        // via SetFailedAsync instead, so earlier progress is never at risk of being resubmitted
        // alongside a poisoned change set.
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(1));
        _repository.Verify(r => r.SetFailedAsync(map.Id, "selhalo", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ReturnsQuietly_WhenMapMissing()
    {
        _repository.Setup(r => r.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMap?)null);

        await CreateSut().RunAsync(Guid.NewGuid(), CancellationToken.None);

        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_EndsIdle_WhenNothingPending()
    {
        var map = MapWithMeetings();
        map.Status = MindMapStatus.Updating;
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Idle, map.Status);
        _updater.Verify(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_AssertsUpdatingStatus_BeforeFirstUpdateCall_WhenStatusIsNotAlreadyUpdating()
    {
        // A second attach can enqueue job2 while job1 still holds the per-map lock; job1
        // then finishes and unconditionally sets Idle, overwriting whatever the attach
        // handler set for job2. If RunAsync doesn't re-assert Updating itself, job2 spends
        // its whole run with the map reading a stale, non-Updating status: no read-only
        // banner, no polling, and the save/restore "Updating" guards stay open.
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var map = MapWithMeetings(m1);
        map.Status = MindMapStatus.Idle;
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        MindMapStatus? statusDuringFirstUpdate = null;
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript _, CancellationToken _) =>
            {
                statusDuringFirstUpdate ??= map.Status;
                return MindMapJson.Clone(current);
            });

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Updating, statusDuringFirstUpdate);
    }

    [Fact]
    public async Task RunAsync_ReassertsUpdating_AndClearsLastError_WhenResumingFromFailedWithPendingWork()
    {
        // A requeued Hangfire retry (after cancellation/deploy) resumes with Status still
        // Failed from the previous attempt's MarkFailedAsync — with pending meetings left to
        // process, the guards that gate on Status == Updating must open again for the
        // duration of this run, not stay closed for however long the retry takes.
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var map = MapWithMeetings(m1);
        map.Status = MindMapStatus.Failed;
        map.LastError = "previous attempt failed";
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        MindMapStatus? statusDuringUpdate = null;
        string? lastErrorDuringUpdate = "not observed";
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript _, CancellationToken _) =>
            {
                statusDuringUpdate ??= map.Status;
                lastErrorDuringUpdate = map.LastError;
                return MindMapJson.Clone(current);
            });

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Updating, statusDuringUpdate);
        Assert.Null(lastErrorDuringUpdate);
        Assert.Equal(MindMapStatus.Idle, map.Status);
    }

    [Fact]
    public async Task RunAsync_DoesNotPersistUpdatingAssertion_WhenNothingPending()
    {
        // No pending meetings means no work — asserting Updating here would spuriously
        // flip a Failed map back to Updating (and clear its diagnostic LastError) even
        // though nothing is actually going to run.
        var map = MapWithMeetings();
        map.Status = MindMapStatus.Failed;
        map.LastError = "stale failure";
        _repository.Setup(r => r.GetForUpdateAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        // GetPendingMeetingsChronologically finds no pending work, so RunAsync falls
        // through to the unconditional Idle/clear-error tail at the end — same as
        // RunAsync_EndsIdle_WhenNothingPending above.
        Assert.Equal(MindMapStatus.Idle, map.Status);
        Assert.Null(map.LastError);
    }
}
