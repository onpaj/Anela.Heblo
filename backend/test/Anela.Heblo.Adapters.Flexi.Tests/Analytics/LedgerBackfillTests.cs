using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class LedgerBackfillTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static LedgerSyncService CreateService(ILedgerClient client, AnalyticsDbContext ctx) =>
        new(client,
            new SyncWatermarkRepository(ctx),
            ctx,
            Options.Create(new FlexiAnalyticsSyncOptions
            {
                BatchSize = 2,
                // Tests must not sit in Task.Delay; throttling is exercised in production config only.
                BackfillThrottleMilliseconds = 0,
            }),
            NullLogger<LedgerSyncService>.Instance);

    private static LedgerItemFlexiDto Dto(long id, DateTime accountingDate) => new()
    {
        Id = -1,
        JournalId = id.ToString(),
        AccountingDate = accountingDate,
        LastUpdate = new DateTimeOffset(accountingDate, TimeSpan.Zero),
        AmountLocal = 1.0,
        DebitAccountList = [new AccountFlexiDto { Code = "518033" }],
        DepartmentList = [new DepartmentFlexiDto { Code = "MARKETING" }],
    };

    [Fact]
    public void MonthWindows_CoversTheRangeInCalendarMonthsClippedToBothEnds()
    {
        var windows = LedgerSyncService
            .MonthWindows(new DateOnly(2020, 1, 15), new DateOnly(2020, 3, 10))
            .ToList();

        windows.Should().Equal(
            (new DateOnly(2020, 1, 15), new DateOnly(2020, 1, 31)),
            (new DateOnly(2020, 2, 1), new DateOnly(2020, 2, 29)),
            (new DateOnly(2020, 3, 1), new DateOnly(2020, 3, 10)));
    }

    [Fact]
    public void MonthWindows_HandlesARangeInsideASingleMonth()
    {
        LedgerSyncService
            .MonthWindows(new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 20))
            .Should().Equal((new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 20)));
    }

    [Fact]
    public async Task BackfillAsync_QueriesOneAccountingDateWindowPerMonthAndPagesEachOne()
    {
        // The whole point of the month windows: FlexiBee's offset stays shallow. Paging the
        // 2020-onward ledger through one ever-growing offset is what makes the naive backfill
        // unusable on a shared 1-vCore server.
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        // January: a full page then a short one. February: nothing.
        client.SetupSequence(c => c.GetAsync(
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                2, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dto(1, new DateTime(2026, 1, 3)), Dto(2, new DateTime(2026, 1, 4))])
            .ReturnsAsync([Dto(3, new DateTime(2026, 1, 9))]);

        var result = await CreateService(client.Object, ctx)
            .BackfillAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28));

        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(3);
        (await ctx.LedgerEntries.CountAsync()).Should().Be(3);

        client.Verify(c => c.GetAsync(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
            null, null, null, 2, 0, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.GetAsync(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
            null, null, null, 2, 2, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.GetAsync(
            new DateTime(2026, 2, 1), new DateTime(2026, 2, 28),
            null, null, null, 2, 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BackfillAsync_IsIdempotentSoAnInterruptedRunCanSimplyBeRepeated()
    {
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<int>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dto(7, new DateTime(2026, 1, 3))]);
        client.Setup(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.Is<int>(skip => skip > 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var svc = CreateService(client.Object, ctx);
        await svc.BackfillAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        await svc.BackfillAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        (await ctx.LedgerEntries.CountAsync()).Should().Be(1);
    }

    /// <summary>Every window comes back empty — what a rejected query looks like from here.</summary>
    private static Mock<ILedgerClient> EmptyClient()
    {
        var client = new Mock<ILedgerClient>();
        client.Setup(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>());
        return client;
    }

    /// <summary>One row on the first window, nothing after: a small but genuine load.</summary>
    private static Mock<ILedgerClient> ClientWithOneRow()
    {
        var client = new Mock<ILedgerClient>();
        var served = false;
        client.Setup(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (served) return new List<LedgerItemFlexiDto>();
                served = true;
                return [Dto(11, new DateTime(2020, 1, 15))];
            });
        return client;
    }

    [Fact]
    public async Task BackfillAsync_TreatsAnEntirelyEmptyRangeAsAFailure()
    {
        await using var ctx = CreateInMemoryContext();

        // ContactListClient-style behaviour: the SDK logs a rejected FlexiBee response and returns
        // an empty list rather than throwing. Without this guard a backfill against expired
        // credentials ingests nothing, exits 0, stamps the watermark with "now", and the nightly
        // delta then starts from now -- six years of history skipped behind a green sync_state.
        var result = await CreateService(EmptyClient().Object, ctx)
            .BackfillAsync(new DateOnly(2020, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsSuccess.Should().BeFalse();

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("FAILED");
        state.LastErrorMessage.Should().Contain("no rows at all");
        state.Watermark.Should().BeNull();
    }

    [Fact]
    public async Task BackfillAsync_LeavesAWatermarkTheNightlyIncrementalCanCarryOnFrom()
    {
        await using var ctx = CreateInMemoryContext();

        var before = DateTimeOffset.UtcNow;
        await CreateService(ClientWithOneRow().Object, ctx)
            .BackfillAsync(new DateOnly(2020, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("OK");
        // Set from when the backfill STARTED, not when it finished, so rows edited while it was
        // running are re-read by the next delta instead of being missed.
        state.Watermark.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task BackfillAsync_DoesNotHandOverTheWatermarkWhenTheRangeStopsShortOfToday()
    {
        await using var ctx = CreateInMemoryContext();

        // Staged load: one month of 2026, years short of today. Stamping the watermark with "now"
        // here would tell the nightly delta that everything up to now is on disk, and every
        // un-edited row between this window and today would be stranded with the run reading OK.
        await CreateService(ClientWithOneRow().Object, ctx)
            .BackfillAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("OK");
        state.Watermark.Should().BeNull();
    }

    [Fact]
    public async Task BackfillAsync_NeverMovesAnExistingWatermarkBackwards()
    {
        await using var ctx = CreateInMemoryContext();
        var repo = new SyncWatermarkRepository(ctx);
        var state = await repo.GetOrCreateAsync("ledger_entry");
        var ahead = DateTimeOffset.UtcNow.AddDays(1);
        state.Watermark = ahead;
        await repo.SaveAsync(state);

        await CreateService(ClientWithOneRow().Object, ctx)
            .BackfillAsync(new DateOnly(2020, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));

        (await ctx.SyncStates.FindAsync("ledger_entry"))!.Watermark.Should().Be(ahead);
    }

    [Fact]
    public async Task BackfillAsync_RejectsARangeThatEndsBeforeItStarts()
    {
        await using var ctx = CreateInMemoryContext();
        var svc = CreateService(EmptyClient().Object, ctx);

        // An inverted range yields zero month windows, so without the guard it would ingest
        // nothing, report OK, and hand over a watermark covering a load that never happened.
        await FluentActions
            .Awaiting(() => svc.BackfillAsync(new DateOnly(2026, 5, 1), new DateOnly(2026, 1, 1)))
            .Should().ThrowAsync<ArgumentException>();

        (await ctx.SyncStates.FindAsync("ledger_entry")).Should().BeNull();
    }

    [Fact]
    public async Task BackfillAsync_ReportsACallerRequestedStopAsCancelledNotFailed()
    {
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();
        using var cts = new CancellationTokenSource();

        client.Setup(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new TaskCanceledException();
            });

        var svc = CreateService(client.Object, ctx);
        await FluentActions
            .Awaiting(() => svc.BackfillAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        // Ctrl+C in the backfill tool must not look like a Flexi outage in sync_state.
        state!.LastRunStatus.Should().Be("CANCELLED");
        state.Watermark.Should().BeNull();
    }

    [Fact]
    public async Task BackfillAsync_KeepsEarlierMonthsWhenALaterOneFails()
    {
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dto(11, new DateTime(2026, 1, 3))]);
        client.Setup(c => c.GetAsync(
                new DateTime(2026, 2, 1), new DateTime(2026, 2, 28),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi timed out"));

        var result = await CreateService(client.Object, ctx)
            .BackfillAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28));

        result.IsSuccess.Should().BeFalse();
        (await ctx.LedgerEntries.CountAsync()).Should().Be(1);

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("FAILED");
        state.LastErrorMessage.Should().Contain("Flexi timed out");
        // No watermark: the nightly incremental must not think history is loaded.
        state.Watermark.Should().BeNull();
    }
}
