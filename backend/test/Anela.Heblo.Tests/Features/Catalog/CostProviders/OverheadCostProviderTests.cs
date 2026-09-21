using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Accounting.CostPools;
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
/// Tests for OverheadCostProvider (M3).
///
/// M3 spreads the overhead pool - everything on accounts 51+52 that is neither VYROBA nor
/// SKLAD/MARKETING - across the same denominator M2 uses, so the two levels stay comparable.
/// Uses Collection attribute to ensure sequential execution due to static RefreshLock in the provider.
/// </summary>
[Collection(CostProviderRefreshLockCollection.Name)]
public class OverheadCostProviderTests
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

    private static Mock<ICostPoolService> BuildCostPoolService(params MonthlyCostPool[] pools)
    {
        var mock = new Mock<ICostPoolService>();
        mock.Setup(s => s.GetMonthlyPoolsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pools.ToList());
        return mock;
    }

    private static OverheadCostProvider CreateProvider(
        Mock<IOverheadCostCache>? cacheMock = null,
        Mock<ICatalogRepository>? repoMock = null,
        Mock<ICostPoolService>? costPoolMock = null,
        Mock<ILogger<OverheadCostProvider>>? loggerMock = null,
        int manufactureCostHistoryDays = DefaultHistoryDays)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ICatalogRepository)))
            .Returns((repoMock ?? new Mock<ICatalogRepository>()).Object);

        return new OverheadCostProvider(
            (cacheMock ?? new Mock<IOverheadCostCache>()).Object,
            serviceProviderMock.Object,
            (costPoolMock ?? BuildCostPoolService()).Object,
            (loggerMock ?? new Mock<ILogger<OverheadCostProvider>>()).Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = manufactureCostHistoryDays }),
            TimeProvider.System);
    }

    private static void VerifyLog(
        Mock<ILogger<OverheadCostProvider>> logger,
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
    internal async Task RefreshAsync_DistributesOverheadPoolPerSoldPiece()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var saleDate = new DateTime(now.Year, now.Month, 1).AddMonths(-1).AddDays(14);
        var month = new DateTime(saleDate.Year, saleDate.Month, 1);

        var products = new List<CatalogAggregate>
        {
            BuildProduct("PROD-A", new[] { (saleDate, 10.0) }),
            BuildProduct("PROD-B", new[] { (saleDate, 20.0) }),
            BuildProduct("PROD-C", new[] { (saleDate, 30.0) })
        };

        var overheadCost = 1200m;
        var totalSoldPieces = 60.0;
        var expectedCostPerPiece = (decimal)((double)overheadCost / totalSoldPieces);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(products);

        var costPoolMock = BuildCostPoolService(new MonthlyCostPool(month, CostPool.M3, overheadCost));

        CostCacheData? captured = null;
        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, costPoolMock: costPoolMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        captured!.IsHydrated.Should().BeTrue();
        captured.ProductCosts.Keys.Should().BeEquivalentTo("PROD-A", "PROD-B", "PROD-C");
        foreach (var monthly in captured.ProductCosts.Values)
        {
            monthly.Should().NotBeEmpty();
            monthly.Should().AllSatisfy(mc => mc.Cost.Should().Be(expectedCostPerPiece));
        }
    }

    [Fact]
    internal async Task RefreshAsync_IgnoresM1AndM2Pools_WhenSummingOverhead()
    {
        // The cost pool service returns every pool for the window. Only M3 may reach M3 - summing
        // the lot would charge each product the whole company ledger a second time on top of
        // M1 and M2, which already carry VYROBA and SKLAD+MARKETING.
        var now = DateTime.UtcNow;
        var saleDate = new DateTime(now.Year, now.Month, 1).AddMonths(-1).AddDays(14);
        var month = new DateTime(saleDate.Year, saleDate.Month, 1);

        var products = new List<CatalogAggregate>
        {
            BuildProduct("PROD-A", new[] { (saleDate, 10.0) })
        };

        var overheadCost = 500m;
        var expectedCostPerPiece = (decimal)((double)overheadCost / 10.0);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(products);

        var costPoolMock = BuildCostPoolService(
            new MonthlyCostPool(month, CostPool.M1, 9999m),
            new MonthlyCostPool(month, CostPool.M2, 8888m),
            new MonthlyCostPool(month, CostPool.M3, overheadCost));

        CostCacheData? captured = null;
        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, costPoolMock: costPoolMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        captured!.ProductCosts["PROD-A"].Should().AllSatisfy(mc => mc.Cost.Should().Be(expectedCostPerPiece));
    }

    [Fact]
    internal async Task RefreshAsync_ExcludesSyntheticBundleComponentSalesFromDenominator()
    {
        // Same rule as M2: a bundle's synthetic component rows carry quantity but no revenue, so
        // counting them would inflate the denominator and understate overhead for every product.
        var now = DateTime.UtcNow;
        var saleDate = new DateTime(now.Year, now.Month, 1).AddMonths(-1).AddDays(14);
        var month = new DateTime(saleDate.Year, saleDate.Month, 1);

        var product = new CatalogAggregate
        {
            ProductCode = "PROD-A",
            SalesHistory = new List<CatalogSaleRecord>
            {
                new() { Date = saleDate, ProductCode = "PROD-A", ProductName = "PROD-A", AmountTotal = 10 },
                new() { Date = saleDate, ProductCode = "PROD-A", ProductName = "PROD-A", AmountTotal = 1000, SourceBundleCode = "BUNDLE001" }
            }
        };

        var overheadCost = 1200m;
        var expectedCostPerPiece = (decimal)((double)overheadCost / 10.0);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var costPoolMock = BuildCostPoolService(new MonthlyCostPool(month, CostPool.M3, overheadCost));

        CostCacheData? captured = null;
        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, costPoolMock: costPoolMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        captured!.ProductCosts["PROD-A"].Should().AllSatisfy(mc => mc.Cost.Should().Be(expectedCostPerPiece));
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

        var costPoolMock = BuildCostPoolService(
            new MonthlyCostPool(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), CostPool.M3, 999m));

        CostCacheData? captured = null;
        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OverheadCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, costPoolMock: costPoolMock, loggerMock: loggerMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        captured!.IsHydrated.Should().BeTrue();
        captured.ProductCosts.Keys.Should().BeEquivalentTo("PROD-A", "PROD-B");
        foreach (var monthly in captured.ProductCosts.Values)
        {
            monthly.Should().NotBeEmpty();
            monthly.Should().AllSatisfy(mc => mc.Cost.Should().Be(0m));
        }

        VerifyLog(loggerMock, LogLevel.Warning, "No sales history found");
    }

    [Fact]
    internal async Task RefreshAsync_RequestsMonthAlignedWindow_FromCostPoolService()
    {
        // Arrange
        DateOnly? capturedFrom = null;
        DateOnly? capturedTo = null;

        var costPoolMock = new Mock<ICostPoolService>();
        costPoolMock
            .Setup(s => s.GetMonthlyPoolsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly, DateOnly, CancellationToken>((from, to, _) =>
            {
                capturedFrom = from;
                capturedTo = to;
            })
            .ReturnsAsync(new List<MonthlyCostPool>());

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate>());

        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, costPoolMock: costPoolMock, manufactureCostHistoryDays: 90);

        var nowBefore = DateTime.UtcNow;

        // Act
        await provider.RefreshAsync();

        var nowAfter = DateTime.UtcNow;

        // Assert — the window must be the same one every other cost provider derives, so the
        // margin months stay a subset of the months this provider emits.
        capturedFrom.Should().NotBeNull();
        capturedTo.Should().NotBeNull();

        capturedFrom!.Value.Day.Should().Be(1);

        var expectedFromLower = DateOnly.FromDateTime(nowBefore.AddDays(-90));
        var expectedFromUpper = DateOnly.FromDateTime(nowAfter.AddDays(-90));
        var capturedFromMonthStart = new DateOnly(capturedFrom.Value.Year, capturedFrom.Value.Month, 1);
        capturedFromMonthStart.Should().BeOnOrAfter(new DateOnly(expectedFromLower.Year, expectedFromLower.Month, 1));
        capturedFromMonthStart.Should().BeOnOrBefore(new DateOnly(expectedFromUpper.Year, expectedFromUpper.Month, 1));

        capturedTo!.Value.Day.Should().Be(DateTime.DaysInMonth(capturedTo.Value.Year, capturedTo.Value.Month));
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsSubset_WhenProductCodesProvided()
    {
        // Arrange
        var hydratedData = BuildHydratedCacheData(new[] { "A", "B", "C" });

        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydratedData);

        var provider = CreateProvider(cacheMock: cacheMock);

        // Act
        var result = await provider.GetCostsAsync(new List<string> { "A", "C", "DOES-NOT-EXIST" });

        // Assert
        result.Keys.Should().BeEquivalentTo("A", "C");
        hydratedData.ProductCosts.Keys.Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsFullDictionary_WhenProductCodesAreNull()
    {
        // Arrange
        var hydratedData = BuildHydratedCacheData(new[] { "A", "B", "C" });

        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydratedData);

        var provider = CreateProvider(cacheMock: cacheMock);

        // Act
        var result = await provider.GetCostsAsync(null);

        // Assert
        result.Keys.Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsEmptyAndLogsWarning_WhenCacheNotHydrated()
    {
        // Arrange
        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CostCacheData.Empty());

        var repoMock = new Mock<ICatalogRepository>();
        var costPoolMock = new Mock<ICostPoolService>();
        var loggerMock = new Mock<ILogger<OverheadCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, costPoolMock: costPoolMock, loggerMock: loggerMock);

        // Act
        var result = await provider.GetCostsAsync();

        // Assert
        result.Should().BeEmpty();
        VerifyLog(loggerMock, LogLevel.Warning, "OverheadCostCache not hydrated");

        repoMock.VerifyNoOtherCalls();
        costPoolMock.VerifyNoOtherCalls();
    }

    [Fact]
    internal async Task GetCostsAsync_LogsErrorAndRethrows_WhenCacheReadFails()
    {
        // Arrange
        var boom = new InvalidOperationException("cache offline");
        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ThrowsAsync(boom);

        var loggerMock = new Mock<ILogger<OverheadCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, loggerMock: loggerMock);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetCostsAsync());

        // Assert
        thrown.Should().BeSameAs(boom);
        VerifyLog(loggerMock, LogLevel.Error, "Error getting overhead costs");
    }

    [Fact]
    internal async Task RefreshAsync_ReleasesLockOnCostPoolException_AndAllowsSubsequentRefresh()
    {
        // Arrange
        var boom = new InvalidOperationException("ledger offline");

        var costPoolMock = new Mock<ICostPoolService>();
        costPoolMock.SetupSequence(s => s.GetMonthlyPoolsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom)
            .ReturnsAsync(new List<MonthlyCostPool>());

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate>());

        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OverheadCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, costPoolMock: costPoolMock, loggerMock: loggerMock);

        // Act 1 — first call throws
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RefreshAsync());
        thrown.Should().BeSameAs(boom);
        VerifyLog(loggerMock, LogLevel.Error, "Failed to refresh OverheadCostCache");
        cacheMock.Verify(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()), Times.Never);

        // Act 2 — second call must proceed (lock released by finally)
        await provider.RefreshAsync();

        VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress", Times.Never());
        VerifyLog(loggerMock, LogLevel.Information, "Starting OverheadCostCache refresh", Times.Exactly(2));
        cacheMock.Verify(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    internal async Task RefreshAsync_WhenRefreshAlreadyInProgress_SkipsSecondCallAndLogsInformation()
    {
        // Arrange - the first call parks inside the lock on this gate, so the second call is
        // guaranteed to meet a held lock rather than racing it.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(gate.Task);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate>());

        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OverheadCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, loggerMock: loggerMock);

        // Act
        var firstRefresh = provider.RefreshAsync();
        try
        {
            // Must hit the WaitAsync(0) skip path and return promptly instead of blocking.
            await provider.RefreshAsync();
        }
        finally
        {
            // Release the gate and drain the first call even if an assertion throws, so the static
            // RefreshLock is never left acquired for the rest of the collection.
            gate.TrySetResult();
            await firstRefresh;
        }

        // Assert
        repoMock.Verify(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        cacheMock.Verify(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyLog(loggerMock, LogLevel.Information, "OverheadCostCache refresh already in progress, skipping");
    }

    [Fact]
    internal async Task GetCostsAsync_WithEmptyProductCodes_ReturnsAllCachedEntries()
    {
        // Arrange
        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHydratedCacheData(new[] { "PRODUCT-A", "PRODUCT-B" }));

        var provider = CreateProvider(cacheMock: cacheMock);

        // Act - an empty filter means "no filter", same as null
        var result = await provider.GetCostsAsync(productCodes: new List<string>());

        // Assert
        result.Should().HaveCount(2);
        result.Keys.Should().BeEquivalentTo("PRODUCT-A", "PRODUCT-B");
    }

    [Fact]
    internal async Task RefreshAsync_LogsWarning_WhenOverheadPoolIsEmpty()
    {
        // Arrange - sales exist, so the zero comes from the pool, not from a missing denominator.
        var now = DateTime.UtcNow;
        var saleDate = new DateTime(now.Year, now.Month, 1).AddMonths(-1).AddDays(14);
        var month = new DateTime(saleDate.Year, saleDate.Month, 1);

        var products = new List<CatalogAggregate>
        {
            BuildProduct("PRODUCT-A", new[] { (saleDate, 100d) })
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(products);

        // CostPoolService emits a zero row rather than no row when the ledger comes back empty.
        var costPoolMock = BuildCostPoolService(new MonthlyCostPool(month, CostPool.M3, 0m));

        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OverheadCostProvider>>();

        var provider = CreateProvider(
            cacheMock: cacheMock,
            repoMock: repoMock,
            costPoolMock: costPoolMock,
            loggerMock: loggerMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        VerifyLog(loggerMock, LogLevel.Warning, "Overhead cost pool (M3) is empty");
    }
}
