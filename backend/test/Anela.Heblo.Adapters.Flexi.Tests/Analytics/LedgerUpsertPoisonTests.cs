using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

/// <summary>
/// Regression for the first live failure of flexi-analytics-sync, 2026-09-24 03:00 Prague.
///
/// What happened, in order: a ledger row arrived twice inside one run, the second
/// <c>DbSet.Add</c> threw "another instance with the same key value is already being tracked",
/// and the two already-Added entities stayed on the change tracker. The catch block then wrote
/// the FAILED state through the SAME context, so SaveChanges flushed those orphaned INSERTs
/// alongside the sync_state UPDATE and died on
/// <c>duplicate key value violates unique constraint "PK_ledger_entry"</c>. The status was
/// therefore never persisted (sync_state sat at RUNNING for six hours) and the poisoned context
/// then failed all three remaining entities — one bad page took the whole job down.
/// </summary>
public class LedgerUpsertPoisonTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static LedgerSyncService CreateService(ILedgerClient client, AnalyticsDbContext ctx) =>
        new(client,
            new SyncWatermarkRepository(ctx),
            ctx,
            Options.Create(new FlexiAnalyticsSyncOptions { BatchSize = 2, BackfillThrottleMilliseconds = 0 }),
            NullLogger<LedgerSyncService>.Instance);

    private static LedgerItemFlexiDto Dto(long id, DateTime date) => new()
    {
        Id = -1,
        JournalId = id.ToString(),
        AccountingDate = date,
        LastUpdate = new DateTimeOffset(date, TimeSpan.Zero),
        AmountLocal = 1.0,
        DebitAccountList = [new AccountFlexiDto { Code = "518033" }],
    };

    [Fact]
    public async Task SyncAsync_WhenTheSameRowArrivesTwiceInOnePage_StillSucceeds()
    {
        // FlexiBee orders the changed-since query by lastUpdate with no tiebreaker, and bulk
        // operations leave huge blocks sharing one timestamp, so skip-based paging can hand the
        // same row back twice. That must be an upsert, not a crash.
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();

        client.SetupSequence(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dto(77, new DateTime(2026, 9, 23)), Dto(77, new DateTime(2026, 9, 23))])
            .ReturnsAsync([]);

        var result = await CreateService(client.Object, ctx).SyncAsync();

        result.IsSuccess.Should().BeTrue();
        (await ctx.LedgerEntries.CountAsync()).Should().Be(1);
        (await ctx.SyncStates.FindAsync("ledger_entry"))!.LastRunStatus.Should().Be("OK");
    }

    [Fact]
    public async Task SyncAsync_WhenTheSameRowArrivesOnTwoDifferentPages_StillSucceeds()
    {
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();

        client.SetupSequence(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dto(1, new DateTime(2026, 9, 23)), Dto(2, new DateTime(2026, 9, 23))])
            .ReturnsAsync([Dto(2, new DateTime(2026, 9, 23)), Dto(3, new DateTime(2026, 9, 23))])
            .ReturnsAsync([]);

        var result = await CreateService(client.Object, ctx).SyncAsync();

        result.IsSuccess.Should().BeTrue();
        (await ctx.LedgerEntries.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task SyncAsync_WhenABatchBlowsUp_StillRecordsTheFailureInsteadOfLeavingItRunning()
    {
        // The six-hour RUNNING row: the catch block could not persist because the same context was
        // still holding the failed batch's INSERTs, so the bookkeeping write took them along and
        // died too. Recording *why* a run failed must not depend on the run having gone well.
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();

        ctx.LedgerEntries.Add(new LedgerEntry
        {
            FlexiId = 500,
            EntryDate = new DateOnly(2026, 9, 1),
            RawPayload = "{}",
            SyncedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        client.SetupSequence(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dto(501, new DateTime(2026, 9, 23)), Dto(502, new DateTime(2026, 9, 23))])
            .ThrowsAsync(new HttpRequestException("Flexi fell over"));

        var result = await CreateService(client.Object, ctx).SyncAsync();

        result.IsSuccess.Should().BeFalse();

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("FAILED", "a stuck RUNNING row tells an operator nothing");
        state.LastErrorMessage.Should().Contain("Flexi fell over");
        state.LastRunFinishedAt.Should().NotBeNull();
    }
}
