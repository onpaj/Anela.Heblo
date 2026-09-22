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

    [Fact]
    public async Task BackfillAsync_LeavesAWatermarkTheNightlyIncrementalCanCarryOnFrom()
    {
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();
        client.Setup(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var before = DateTimeOffset.UtcNow;
        await CreateService(client.Object, ctx)
            .BackfillAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("OK");
        // Set from when the backfill STARTED, not when it finished, so rows edited while it was
        // running are re-read by the next delta instead of being missed.
        state.Watermark.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
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
