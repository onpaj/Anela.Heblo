using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

/// <summary>
/// All four entity syncs share one scoped AnalyticsDbContext. On 2026-09-24 the ledger sync left
/// two failed INSERTs on the change tracker and every later entity died on them, so a single bad
/// ledger page was reported as a four-service outage. One entity's mess must not become another's.
/// </summary>
public class SyncOrchestratorIsolationTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task SyncAllAsync_WhenOneEntityLeavesTheTrackerDirty_TheNextOneStillRuns()
    {
        await using var ctx = CreateInMemoryContext();

        var poisoner = new Mock<IEntitySyncService>();
        poisoner.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // Exactly what the failed ledger batch left behind: an Added entity that will
                // collide the moment anything else calls SaveChanges on this context.
                ctx.LedgerEntries.Add(new Anela.Heblo.Persistence.Analytics.Entities.LedgerEntry
                {
                    FlexiId = 1,
                    EntryDate = new DateOnly(2026, 9, 1),
                    RawPayload = "{}",
                    SyncedAt = DateTimeOffset.UtcNow,
                });
                return new SyncResult(0, 0, IsSuccess: false);
            });

        var healthy = new Mock<IEntitySyncService>();
        healthy.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(5, 5, IsSuccess: true));

        var sut = new FlexiAnalyticsSyncService(
            [poisoner.Object, healthy.Object], ctx, NullLogger<FlexiAnalyticsSyncService>.Instance);

        var report = await sut.SyncAllAsync();

        healthy.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        report.FailedServices.Should().Be(1, "only the entity that actually failed should count");
        report.TotalUpserted.Should().Be(5);
        ctx.ChangeTracker.Entries().Should().BeEmpty("the tracker is cleared between entities");
    }
}
