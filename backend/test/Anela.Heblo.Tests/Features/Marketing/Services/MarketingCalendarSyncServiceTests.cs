using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Tests.Domain.Marketing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing.Services;

public class MarketingCalendarSyncServiceTests
{
    private static readonly DateTime WindowFrom = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowTo = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EventStart = new(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EventEnd = new(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly SyncActor Actor = new("user-import", "Import User");

    private readonly Mock<IMarketingActionRepository> _repositoryMock = new();
    private readonly Mock<IOutlookCalendarSync> _outlookSyncMock = new();
    private readonly Mock<IMarketingCategoryMapper> _mapperMock = new();
    private readonly MarketingCalendarSyncService _service;

    public MarketingCalendarSyncServiceTests()
    {
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction a, CancellationToken _) => { a.Id = 100; return a; });
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction>());
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction>());

        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto>());

        _mapperMock
            .Setup(m => m.MapToActionType(It.IsAny<IReadOnlyList<string>>()))
            .Returns(new CategoryMappingResult(MarketingActionType.SocialMedia, null, new List<string>()));

        _service = new MarketingCalendarSyncService(
            _repositoryMock.Object,
            _outlookSyncMock.Object,
            _mapperMock.Object,
            NullLogger<MarketingCalendarSyncService>.Instance);
    }

    private static OutlookEventDto BuildEvent(string id = "evt-1", string subject = "Test Event")
    {
        return new OutlookEventDto
        {
            Id = id,
            Subject = subject,
            Start = new GraphEventDateTime { DateTimeString = EventStart.ToString("O"), TimeZone = "UTC" },
            End = new GraphEventDateTime { DateTimeString = EventEnd.ToString("O"), TimeZone = "UTC" },
            Categories = Array.Empty<string>(),
        };
    }

    private static MarketingAction BuildSyncedAction(int id, string outlookEventId, string title = "Test Event")
    {
        return new MarketingActionTestBuilder()
            .WithId(id)
            .WithOutlookEventId(outlookEventId)
            .WithTitle(title)
            .WithDescription(null)
            .WithStartDate(EventStart)
            .WithEndDate(EventEnd)
            .WithActionType(MarketingActionType.SocialMedia)
            .WithCreatedAt(DateTime.UtcNow)
            .WithModifiedAt(DateTime.UtcNow)
            .WithCreatedBy("user-1")
            .Build();
    }

    private Task<ImportFromOutlookResponse> SyncAsync(bool dryRun = false) =>
        _service.SyncAsync(WindowFrom, WindowTo, Actor, dryRun, CancellationToken.None);

    // ─── Reconciliation ───────────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_WhenOrphanConfirmedGone_SoftDeletesWithActor()
    {
        // Arrange — Heblo has an action in the window; Outlook no longer lists it and GET returns 404
        var orphan = BuildSyncedAction(7, "evt-gone");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { orphan });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutlookEventDto?)null);

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.Deleted && i.OutlookEventId == "evt-gone" && i.CreatedActionId == 7);
        orphan.IsDeleted.Should().BeTrue();
        orphan.DeletedByUserId.Should().Be(Actor.UserId);
        orphan.DeletedByUsername.Should().Be(Actor.Username);
        _repositoryMock.Verify(x => x.UpdateAsync(orphan, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenOrphanStillExistsInOutlook_UpdatesInsteadOfDeleting()
    {
        // Arrange — event moved outside the window: not in the list, but GET still finds it (with a new title)
        var moved = BuildSyncedAction(8, "evt-moved", title: "Old Title");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { moved });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-moved", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEvent(id: "evt-moved", subject: "New Title"));

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(0);
        result.Updated.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.Updated && i.OutlookEventId == "evt-moved");
        moved.IsDeleted.Should().BeFalse();
        moved.Title.Should().Be("New Title");
    }

    [Fact]
    public async Task SyncAsync_WhenOrphanStillExistsAndUnchanged_SkipsIt()
    {
        // Arrange
        var unchanged = BuildSyncedAction(9, "evt-same");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { unchanged });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-same", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEvent(id: "evt-same"));

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(0);
        result.Skipped.Should().Be(1);
        unchanged.IsDeleted.Should().BeFalse();
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenOrphanConfirmationThrows_ReportsFailedAndContinues()
    {
        // Arrange — two orphans; the first confirmation blows up, the second is a real delete
        var failing = BuildSyncedAction(10, "evt-boom");
        var gone = BuildSyncedAction(11, "evt-gone");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { failing, gone });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-boom", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph 500"));
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutlookEventDto?)null);

        // Act
        var result = await SyncAsync();

        // Assert
        result.Failed.Should().Be(1);
        result.Deleted.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.Failed && i.OutlookEventId == "evt-boom" && i.Error == "Graph 500");
        failing.IsDeleted.Should().BeFalse();
        gone.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task SyncAsync_WhenListedEventExists_IsNotTreatedAsOrphan()
    {
        // Arrange — the action's event IS in the list → no GET, no delete
        var listed = BuildSyncedAction(12, "evt-listed");
        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { BuildEvent(id: "evt-listed") });
        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { listed });
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { listed });

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(0);
        result.Skipped.Should().Be(1);
        _outlookSyncMock.Verify(s => s.GetEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenDryRunAndOrphanGone_ReportsWouldDeleteWithoutPersisting()
    {
        // Arrange
        var orphan = BuildSyncedAction(13, "evt-gone");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { orphan });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutlookEventDto?)null);

        // Act
        var result = await SyncAsync(dryRun: true);

        // Assert
        result.Deleted.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.WouldDelete && i.OutlookEventId == "evt-gone");
        orphan.IsDeleted.Should().BeFalse();
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenBatchSaveFails_ReportsDeletesAsFailed()
    {
        // Arrange
        var orphan = BuildSyncedAction(14, "evt-gone");
        _repositoryMock
            .Setup(x => x.GetSyncedInWindowAsync(WindowFrom, WindowTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { orphan });
        _outlookSyncMock
            .Setup(s => s.GetEventAsync("evt-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutlookEventDto?)null);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB down"));

        // Act
        var result = await SyncAsync();

        // Assert
        result.Deleted.Should().Be(0);
        result.Failed.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == ImportStatus.Failed && i.OutlookEventId == "evt-gone" && i.Error == "DB down");
    }
}
