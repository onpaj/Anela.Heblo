using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

/// <summary>
/// Tests for SalesCostProvider.
/// Uses Collection attribute to ensure sequential execution due to static RefreshLock in the provider.
/// </summary>
[Collection("SalesCostProviderTests")]
public class SalesCostProviderTests
{
    private const int DefaultHistoryDays = 90;

    // ===== Helpers =====

    private static CatalogAggregate BuildProduct(
        string productCode,
        IEnumerable<(DateTime date, double amount)> sales)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            SalesHistory = sales
                .Select(s => new CatalogSaleRecord
                {
                    Date = s.date,
                    ProductCode = productCode,
                    ProductName = productCode,
                    AmountTotal = s.amount
                })
                .ToList()
        };
    }

    private static SalesCostProvider CreateProvider(
        Mock<ISalesCostCache>? cacheMock = null,
        Mock<ICatalogRepository>? repoMock = null,
        Mock<ILedgerService>? ledgerMock = null,
        Mock<ILogger<SalesCostProvider>>? loggerMock = null,
        int manufactureCostHistoryDays = DefaultHistoryDays)
    {
        return new SalesCostProvider(
            (cacheMock ?? new Mock<ISalesCostCache>()).Object,
            (repoMock ?? new Mock<ICatalogRepository>()).Object,
            (ledgerMock ?? new Mock<ILedgerService>()).Object,
            (loggerMock ?? new Mock<ILogger<SalesCostProvider>>()).Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = manufactureCostHistoryDays }));
    }

    private static void VerifyLog(
        Mock<ILogger<SalesCostProvider>> logger,
        LogLevel level,
        string messageContains,
        Times? times = null)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(messageContains)),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            times ?? Times.AtLeastOnce());
    }

    private static CostCacheData BuildHydratedCacheData(IEnumerable<string> productCodes)
    {
        var month = new DateTime(2026, 1, 1);
        var dict = productCodes.ToDictionary(
            code => code,
            code => new List<MonthlyCost> { new(month, 1m) });
        return new CostCacheData
        {
            ProductCosts = dict,
            LastUpdated = DateTime.UtcNow,
            DataFrom = DateOnly.FromDateTime(month),
            DataTo = DateOnly.FromDateTime(month.AddMonths(1).AddDays(-1)),
            IsHydrated = true
        };
    }

    // ===== Tests =====

    [Fact]
    internal async Task RefreshAsync_DistributesCostPerPiece_WhenSalesExist()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var saleDate = new DateTime(now.Year, now.Month, 1).AddMonths(-1).AddDays(14);

        var products = new List<CatalogAggregate>
        {
            BuildProduct("PROD-A", new[] { (saleDate, 10.0) }),
            BuildProduct("PROD-B", new[] { (saleDate, 20.0) }),
            BuildProduct("PROD-C", new[] { (saleDate, 30.0) })
        };
        var warehouseCost = 600m;
        var marketingCost = 600m;
        var totalSoldPieces = 60.0;
        var expectedCostPerPiece = (decimal)((double)(warehouseCost + marketingCost) / totalSoldPieces);

        var callOrder = new List<string>();

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("WaitForCurrentMergeAsync"))
            .Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("GetAllAsync"))
            .ReturnsAsync(products);

        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = saleDate, Cost = warehouseCost, Department = "SKLAD" }
            });
        ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = saleDate, Cost = marketingCost, Department = "MARKETING" }
            });

        CostCacheData? captured = null;
        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, ledgerMock: ledgerMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        captured!.IsHydrated.Should().BeTrue();
        captured.ProductCosts.Should().HaveCount(3);
        captured.ProductCosts.Keys.Should().BeEquivalentTo("PROD-A", "PROD-B", "PROD-C");

        var firstList = captured.ProductCosts.Values.First();
        firstList.Should().NotBeEmpty();
        var monthCount = firstList.Count;

        foreach (var monthly in captured.ProductCosts.Values)
        {
            monthly.Should().HaveCount(monthCount);
            monthly.Should().AllSatisfy(mc => mc.Cost.Should().Be(expectedCostPerPiece));
        }

        ledgerMock.Verify(
            s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()),
            Times.Once);
        ledgerMock.Verify(
            s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()),
            Times.Once);

        callOrder.Should().Equal("WaitForCurrentMergeAsync", "GetAllAsync");
    }

    [Fact]
    internal async Task RefreshAsync_WritesZeroCostsAndLogsWarning_WhenNoSalesInPeriod()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            BuildProduct("PROD-A", Array.Empty<(DateTime, double)>()),
            BuildProduct("PROD-B", Array.Empty<(DateTime, double)>()),
            BuildProduct(string.Empty, Array.Empty<(DateTime, double)>()),
            BuildProduct(null!, Array.Empty<(DateTime, double)>())
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(products);

        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = DateTime.UtcNow, Cost = 999m, Department = "SKLAD" }
            });

        CostCacheData? captured = null;
        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<SalesCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, ledgerMock: ledgerMock, loggerMock: loggerMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        captured!.IsHydrated.Should().BeTrue();
        captured.ProductCosts.Should().HaveCount(2);
        captured.ProductCosts.Keys.Should().BeEquivalentTo("PROD-A", "PROD-B");
        foreach (var monthly in captured.ProductCosts.Values)
        {
            monthly.Should().NotBeEmpty();
            monthly.Should().AllSatisfy(mc => mc.Cost.Should().Be(0m));
        }

        VerifyLog(loggerMock, LogLevel.Warning, "No sales history found");
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsFullDictionary_WhenProductCodesAreNull()
    {
        // Arrange
        var hydratedData = BuildHydratedCacheData(new[] { "A", "B", "C" });

        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydratedData);

        var provider = CreateProvider(cacheMock: cacheMock);

        // Act
        var result = await provider.GetCostsAsync(null);

        // Assert
        result.Should().HaveCount(3);
        result.Keys.Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsFullDictionary_WhenProductCodesAreEmpty()
    {
        // Arrange
        var hydratedData = BuildHydratedCacheData(new[] { "A", "B", "C" });

        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydratedData);

        var provider = CreateProvider(cacheMock: cacheMock);

        // Act
        var result = await provider.GetCostsAsync(new List<string>());

        // Assert
        result.Should().HaveCount(3);
        result.Keys.Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsSubset_WhenProductCodesProvided()
    {
        // Arrange
        var hydratedData = BuildHydratedCacheData(new[] { "A", "B", "C" });
        var originalCount = hydratedData.ProductCosts.Count;
        var originalKeys = hydratedData.ProductCosts.Keys.ToArray();

        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydratedData);

        var provider = CreateProvider(cacheMock: cacheMock);

        // Act
        var result = await provider.GetCostsAsync(new List<string> { "A", "C", "DOES-NOT-EXIST" });

        // Assert
        result.Should().HaveCount(2);
        result.Keys.Should().BeEquivalentTo("A", "C");

        hydratedData.ProductCosts.Should().HaveCount(originalCount);
        hydratedData.ProductCosts.Keys.Should().BeEquivalentTo(originalKeys);
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsEmptyAndLogsWarning_WhenCacheNotHydrated()
    {
        // Arrange
        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CostCacheData.Empty());

        var repoMock = new Mock<ICatalogRepository>();
        var ledgerMock = new Mock<ILedgerService>();
        var loggerMock = new Mock<ILogger<SalesCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, ledgerMock: ledgerMock, loggerMock: loggerMock);

        // Act
        var result = await provider.GetCostsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        VerifyLog(loggerMock, LogLevel.Warning, "SalesCostCache not hydrated");

        repoMock.VerifyNoOtherCalls();
        ledgerMock.VerifyNoOtherCalls();
    }

    [Fact]
    internal async Task RefreshAsync_PassesMonthAlignedDateRange_ToLedgerService()
    {
        // Arrange
        DateTime? capturedFrom = null;
        DateTime? capturedTo = null;

        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, string?, CancellationToken>((from, to, _, _) =>
            {
                capturedFrom = from;
                capturedTo = to;
            })
            .ReturnsAsync(new List<CostStatistics>());
        ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate>());

        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, ledgerMock: ledgerMock, manufactureCostHistoryDays: 90);

        var nowBefore = DateTime.UtcNow;

        // Act
        await provider.RefreshAsync();

        var nowAfter = DateTime.UtcNow;

        // Assert
        capturedFrom.Should().NotBeNull();
        capturedTo.Should().NotBeNull();

        capturedFrom!.Value.Day.Should().Be(1);

        var expectedFromLower = DateOnly.FromDateTime(nowBefore.AddDays(-90));
        var expectedFromUpper = DateOnly.FromDateTime(nowAfter.AddDays(-90));
        var capturedFromMonthStart = new DateOnly(capturedFrom.Value.Year, capturedFrom.Value.Month, 1);
        capturedFromMonthStart.Should().BeOnOrAfter(new DateOnly(expectedFromLower.Year, expectedFromLower.Month, 1));
        capturedFromMonthStart.Should().BeOnOrBefore(new DateOnly(expectedFromUpper.Year, expectedFromUpper.Month, 1));

        capturedTo!.Value.Day.Should().Be(DateTime.DaysInMonth(capturedTo.Value.Year, capturedTo.Value.Month));
        capturedTo.Value.Hour.Should().Be(23);
        capturedTo.Value.Minute.Should().Be(59);
        capturedTo.Value.Second.Should().Be(59);

        var capturedToMonthStart = new DateOnly(capturedTo.Value.Year, capturedTo.Value.Month, 1);
        capturedToMonthStart.Should().BeOnOrAfter(new DateOnly(nowBefore.Year, nowBefore.Month, 1));
        capturedToMonthStart.Should().BeOnOrBefore(new DateOnly(nowAfter.Year, nowAfter.Month, 1));
    }

    [Theory]
    [InlineData(2024, 2, 29)]
    [InlineData(2023, 2, 28)]
    [InlineData(2024, 4, 30)]
    [InlineData(2024, 12, 31)]
    internal void DaysInMonth_ReturnsExpected_ForCalendarBoundaries(int year, int month, int expectedDays)
    {
        DateTime.DaysInMonth(year, month).Should().Be(expectedDays);
    }

    [Fact]
    internal async Task RefreshAsync_SkipsSecondInvocation_WhenFirstStillRunning()
    {
        // Arrange
        var gate = new TaskCompletionSource<IList<CostStatistics>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate>());

        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<SalesCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, ledgerMock: ledgerMock, loggerMock: loggerMock);

        // Act 1 — start first refresh; blocks on gate inside ComputeAllCostsAsync
        var firstRefresh = provider.RefreshAsync();

        while (!ledgerMock.Invocations.Any(i => i.Method.Name == nameof(ILedgerService.GetDirectCosts)))
        {
            await Task.Yield();
        }

        // Act 2 — second refresh; detects lock and skips immediately
        await provider.RefreshAsync();

        // Assert intermediate: second invocation skipped
        VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress");
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Release gate, let first refresh finish
        gate.SetResult(new List<CostStatistics>());
        await firstRefresh;

        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Act 3 — fresh refresh after lock released must proceed
        await provider.RefreshAsync();

        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    internal async Task GetCostsAsync_LogsErrorAndRethrows_WhenCacheReadFails()
    {
        // Arrange
        var boom = new InvalidOperationException("cache offline");
        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ThrowsAsync(boom);

        var loggerMock = new Mock<ILogger<SalesCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, loggerMock: loggerMock);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetCostsAsync());

        // Assert
        thrown.Should().BeSameAs(boom);
        VerifyLog(loggerMock, LogLevel.Error, "Error getting sales costs");
    }

    [Fact]
    internal async Task RefreshAsync_ReleasesLockOnLedgerException_AndAllowsSubsequentRefresh()
    {
        // Arrange
        var boom = new InvalidOperationException("ledger offline");

        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock.SetupSequence(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom)
            .ReturnsAsync(new List<CostStatistics>());
        ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate>());

        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<SalesCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, ledgerMock: ledgerMock, loggerMock: loggerMock);

        // Act 1 — first call throws
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RefreshAsync());
        thrown.Should().BeSameAs(boom);
        VerifyLog(loggerMock, LogLevel.Error, "Failed to refresh SalesCostCache");
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Act 2 — second call must proceed (lock released by finally)
        await provider.RefreshAsync();

        VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress", Times.Never());
        // Positive proof: second call entered the normal execution path twice (once before exception, once on success)
        VerifyLog(loggerMock, LogLevel.Information, "Starting SalesCostCache refresh", Times.Exactly(2));
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    internal async Task RefreshAsync_ReleasesLockOnRepositoryException_AndAllowsSubsequentRefresh()
    {
        // Arrange
        var boom = new InvalidOperationException("repo offline");

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.SetupSequence(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom)
            .ReturnsAsync(new List<CatalogAggregate>());

        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<SalesCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, ledgerMock: ledgerMock, loggerMock: loggerMock);

        // Act 1
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RefreshAsync());
        thrown.Should().BeSameAs(boom);
        VerifyLog(loggerMock, LogLevel.Error, "Failed to refresh SalesCostCache");
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Act 2 — second call proceeds (lock released)
        await provider.RefreshAsync();

        VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress", Times.Never());
        // Positive proof: second call entered the normal execution path twice (once before exception, once on success)
        VerifyLog(loggerMock, LogLevel.Information, "Starting SalesCostCache refresh", Times.Exactly(2));
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
