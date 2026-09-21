using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

/// <summary>
/// Tests for the CostPoolService cache and refresh paths.
/// Collection attribute forces sequential execution: RefreshAsync is guarded
/// by a static SemaphoreSlim shared across instances.
/// </summary>
[Collection("CostPoolServiceTests")]
public class CostPoolServiceCacheTests
{
    private static LedgerItem Entry(DateTime date, string department, decimal amount) => new()
    {
        Date = date,
        Department = department,
        Amount = amount,
        DocumentNumber = "DOC",
        ClientName = "CLIENT",
        VariableSymbol = "VS",
        DebitAccountNumber = "518100",
        DebitAccountName = "Ostatni sluzby",
        CreditAccountNumber = "321100",
        CreditAccountName = "Dodavatele"
    };

    private static Mock<ILedgerService> LedgerReturning(params LedgerItem[] items)
    {
        var mock = new Mock<ILedgerService>();
        mock.Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToList());
        return mock;
    }

    private static CostPoolService CreateService(
        Mock<ICostPoolCache> cacheMock,
        Mock<ILedgerService> ledgerMock,
        int historyDays = 400) =>
        new(cacheMock.Object,
            ledgerMock.Object,
            new Mock<ILogger<CostPoolService>>().Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = historyDays }));

    [Fact]
    public async Task GetMonthlyPoolsAsync_ServesFromCache_WhenCachedWindowCoversRange()
    {
        // Arrange - a complete cached payload: every pool present for every month,
        // matching the documented contract (FilterToRange never backfills)
        var cached = new CostPoolCacheData
        {
            Pools = new[]
            {
                new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M1, 120_000m),
                new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M2, 903_000m),
                new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M3, 903_000m),
                new MonthlyCostPool(new DateTime(2026, 8, 1), CostPool.M1, 110_000m),
                new MonthlyCostPool(new DateTime(2026, 8, 1), CostPool.M2, 871_400m),
                new MonthlyCostPool(new DateTime(2026, 8, 1), CostPool.M3, 871_400m),
            },
            DataFrom = new DateOnly(2026, 1, 1),
            DataTo = new DateOnly(2026, 12, 31),
            IsHydrated = true
        };
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cached);
        var ledgerMock = LedgerReturning();
        var service = CreateService(cacheMock, ledgerMock);

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        pools.Should().BeEquivalentTo(new[]
        {
            new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M1, 120_000m),
            new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M2, 903_000m),
            new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M3, 903_000m),
        }, options => options.WithStrictOrdering());
        pools.Select(p => (p.Month, p.Pool)).Should().ContainInOrder(
            (new DateTime(2026, 7, 1), CostPool.M1),
            (new DateTime(2026, 7, 1), CostPool.M2),
            (new DateTime(2026, 7, 1), CostPool.M3));
        ledgerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_ComputesLive_WhenCachedWindowDoesNotCoverRange()
    {
        // Arrange - cache holds 2026 only, caller asks about 2025
        var cached = new CostPoolCacheData
        {
            Pools = Array.Empty<MonthlyCostPool>(),
            DataFrom = new DateOnly(2026, 1, 1),
            DataTo = new DateOnly(2026, 12, 31),
            IsHydrated = true
        };
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cached);
        var ledgerMock = LedgerReturning(Entry(new DateTime(2025, 5, 4), "CENTRALA", 42m));
        var service = CreateService(cacheMock, ledgerMock);

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31));

        // Assert
        pools.Single(p => p.Pool == CostPool.M3).Amount.Should().Be(42m);
        ledgerMock.Verify(l => l.GetLedgerItems(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_ComputesLive_WhenCacheIsNotHydrated()
    {
        // Arrange - an unhydrated cache must not yield a silently-empty result
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        var ledgerMock = LedgerReturning(Entry(new DateTime(2026, 7, 4), "CENTRALA", 77m));
        var service = CreateService(cacheMock, ledgerMock);

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        pools.Single(p => p.Pool == CostPool.M3).Amount.Should().Be(77m);
    }

    [Fact]
    public async Task RefreshAsync_StoresComputedTotalsForTheConfiguredWindow()
    {
        // Arrange
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        CostPoolCacheData? stored = null;
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()))
                 .Callback<CostPoolCacheData, CancellationToken>((d, _) => stored = d)
                 .Returns(Task.CompletedTask);
        var ledgerMock = LedgerReturning(Entry(DateTime.UtcNow.Date, "CENTRALA", 500m));
        var service = CreateService(cacheMock, ledgerMock, historyDays: 60);

        // Act
        await service.RefreshAsync();

        // Assert
        stored.Should().NotBeNull();
        stored!.IsHydrated.Should().BeTrue();
        stored.Pools.Should().NotBeEmpty();
        // The stored window is the whole-month expansion of the configured
        // window, because that is what Pools actually covers.
        var rawFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60));
        var rawTo = DateOnly.FromDateTime(DateTime.UtcNow);

        stored.DataFrom.Should().Be(new DateOnly(rawFrom.Year, rawFrom.Month, 1));
        stored.DataTo.Should().Be(new DateOnly(
            rawTo.Year, rawTo.Month, DateTime.DaysInMonth(rawTo.Year, rawTo.Month)));
    }

    [Fact]
    public async Task RefreshAsync_StoresAWindow_ThatCoversTheWholeBoundaryMonth()
    {
        // Arrange - the refresh window starts mid-month (day 15 of some month),
        // but ComputeAsync widened it to the 1st, so a caller asking for that
        // whole month must hit the cache rather than pulling the ledger again.
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        CostPoolCacheData? stored = null;
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()))
                 .Callback<CostPoolCacheData, CancellationToken>((d, _) => stored = d)
                 .Returns(Task.CompletedTask);
        var ledgerMock = LedgerReturning(Entry(DateTime.UtcNow.Date, "CENTRALA", 500m));
        var service = CreateService(cacheMock, ledgerMock, historyDays: 60);

        // Act
        await service.RefreshAsync();

        // Assert
        var rawFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60));
        var firstOfBoundaryMonth = new DateOnly(rawFrom.Year, rawFrom.Month, 1);

        stored.Should().NotBeNull();
        stored!.Covers(firstOfBoundaryMonth, DateOnly.FromDateTime(DateTime.UtcNow))
              .Should().BeTrue("the boundary month is fully computed, so asking for it must not miss the cache");
    }

    [Fact]
    public async Task RefreshAsync_LogsAndRethrows_WhenLedgerFails()
    {
        // Arrange
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock.Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("FlexiBee unreachable"));
        var service = CreateService(cacheMock, ledgerMock);

        // Act
        var act = () => service.RefreshAsync();

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_SkipsConcurrentRefresh_RatherThanQueueingIt()
    {
        // Arrange - hold the first refresh inside its ledger call, so the second
        // one arrives while the static lock is still held
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var blockingLedger = new Mock<ILedgerService>();
        blockingLedger.Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.TrySetResult();
                await release.Task;
                return (IList<LedgerItem>)new List<LedgerItem>();
            });

        var first = CreateService(cacheMock, blockingLedger);
        var second = CreateService(cacheMock, LedgerReturning(Entry(DateTime.UtcNow.Date, "CENTRALA", 1m)));

        var firstRun = first.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await second.RefreshAsync();

        // Assert - the second refresh returned immediately, computing and storing nothing
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // and the first one still completes normally once unblocked
        release.SetResult();
        await firstRun.WaitAsync(TimeSpan.FromSeconds(5));
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_ReleasesItsLock_SoLaterRefreshesStillRun()
    {
        // Arrange - a failed refresh must not wedge the static semaphore shut
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var failingLedger = new Mock<ILedgerService>();
        failingLedger.Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("FlexiBee unreachable"));

        var failing = CreateService(cacheMock, failingLedger);
        await new Func<Task>(() => failing.RefreshAsync()).Should().ThrowAsync<HttpRequestException>();

        var healthy = CreateService(cacheMock, LedgerReturning(Entry(DateTime.UtcNow.Date, "CENTRALA", 1m)));

        // Act
        await healthy.RefreshAsync();

        // Assert
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
