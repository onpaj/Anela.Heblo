using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

/// <summary>
/// Tests for ManufactureBasedMaterialCostProvider.
/// Uses Collection attribute to ensure sequential execution due to static _refreshLock in the provider.
/// </summary>
[Collection("ManufactureBasedMaterialCostProviderTests")]
public class ManufactureBasedMaterialCostProviderTests
{
    /// <summary>
    /// 4000 days (~11 years) — comfortably covers any synthetic month anchored to UtcNow,
    /// so the SUT's [now - historyDays, now] window always contains seeded records.
    /// </summary>
    private const int DefaultHistoryDays = 4000;

    // ===== Helpers =====

    private static DateTime CurrentMonthStartUtc() =>
        new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    private static DateTime MonthOffsetFromNow(int monthsOffset) =>
        CurrentMonthStartUtc().AddMonths(monthsOffset);

    private static CatalogAggregate BuildManufacturedProduct(
        string productCode,
        ProductType type,
        IEnumerable<(DateTime date, decimal pricePerPiece, double amount)>? history = null,
        decimal? purchasePriceWithVat = null)
    {
        var agg = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productCode,
            Type = type,
        };

        if (history != null)
        {
            agg.ManufactureHistory = history
                .Select(h => new ManufactureHistoryRecord
                {
                    Date = h.date,
                    PricePerPiece = h.pricePerPiece,
                    Amount = h.amount,
                    PriceTotal = h.pricePerPiece * (decimal)h.amount,
                    ProductCode = productCode,
                    DocumentNumber = "TEST-DOC",
                })
                .ToList();
        }

        if (purchasePriceWithVat.HasValue)
        {
            agg.ErpPrice = new ProductPriceErp { PurchasePriceWithVat = purchasePriceWithVat.Value };
        }

        return agg;
    }

    private static CatalogAggregate BuildNonManufacturedProduct(
        string productCode,
        ProductType type,
        decimal? purchasePriceWithVat)
    {
        var agg = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productCode,
            Type = type,
        };

        if (purchasePriceWithVat.HasValue)
        {
            agg.ErpPrice = new ProductPriceErp { PurchasePriceWithVat = purchasePriceWithVat.Value };
        }

        return agg;
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
            IsHydrated = true,
        };
    }

    private static ManufactureBasedMaterialCostProvider CreateProvider(
        Mock<IMaterialCostCache>? cacheMock = null,
        Mock<ICatalogRepository>? repoMock = null,
        Mock<ILogger<ManufactureBasedMaterialCostProvider>>? loggerMock = null,
        int manufactureCostHistoryDays = DefaultHistoryDays)
    {
        return new ManufactureBasedMaterialCostProvider(
            (cacheMock ?? new Mock<IMaterialCostCache>()).Object,
            (repoMock ?? new Mock<ICatalogRepository>()).Object,
            (loggerMock ?? new Mock<ILogger<ManufactureBasedMaterialCostProvider>>()).Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = manufactureCostHistoryDays }));
    }

    private static void VerifyLog(
        Mock<ILogger<ManufactureBasedMaterialCostProvider>> logger,
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

    /// <summary>
    /// Captures the CostCacheData written to the cache by RefreshAsync and returns
    /// the configured cache mock. Tests can read the captured value via the out parameter.
    /// </summary>
    private static Mock<IMaterialCostCache> BuildCaptureCacheMock(out Action<Action<CostCacheData>> onCaptured)
    {
        var cacheMock = new Mock<IMaterialCostCache>();
        Action<CostCacheData>? subscriber = null;
        onCaptured = handler => subscriber = handler;

        cacheMock
            .Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => subscriber?.Invoke(d))
            .Returns(Task.CompletedTask);

        return cacheMock;
    }

    private static int ExpectedMonthCount(DateOnly from, DateOnly to)
    {
        return ((to.Year - from.Year) * 12) + (to.Month - from.Month) + 1;
    }

    // ===== Tests =====

    [Fact]
    internal void Scaffold_Compiles_AndCanCreateProvider()
    {
        var provider = CreateProvider();
        provider.Should().NotBeNull();
    }

    // ===== FR-1: Manufactured product types route to manufacture-history =====

    [Theory]
    [InlineData(ProductType.Product)]
    [InlineData(ProductType.Set)]
    [InlineData(ProductType.SemiProduct)]
    internal async Task RefreshAsync_UsesManufactureHistory_WhenProductTypeIsManufactured(ProductType type)
    {
        // Arrange — a single manufacture record at price 100m; PurchasePriceWithVat is a sentinel
        // that MUST NOT appear anywhere in the resulting cost series.
        var manufactureMonth = MonthOffsetFromNow(-2).AddDays(10);
        var product = BuildManufacturedProduct(
            productCode: "PROD-A",
            type: type,
            history: new[] { (manufactureMonth, 100m, 5.0) },
            purchasePriceWithVat: 999m);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        captured!.ProductCosts.Should().ContainKey("PROD-A");
        var series = captured.ProductCosts["PROD-A"];
        series.Should().NotBeEmpty();
        series.Should().AllSatisfy(mc => mc.Cost.Should().Be(100m));
        series.Select(mc => mc.Cost).Should().NotContain(999m);
    }

    // ===== FR-2: Non-manufactured types fall back to purchase-price =====

    [Theory]
    [InlineData(ProductType.Material)]
    [InlineData(ProductType.Goods)]
    [InlineData(ProductType.UNDEFINED)]
    internal async Task RefreshAsync_UsesPurchasePrice_WhenProductTypeIsNonManufactured(ProductType type)
    {
        // Arrange — populated ManufactureHistory MUST be ignored for non-manufactured types.
        var manufactureMonth = MonthOffsetFromNow(-2).AddDays(10);
        var product = BuildManufacturedProduct(
            productCode: "MAT-1",
            type: type,
            history: new[] { (manufactureMonth, 100m, 5.0) },
            purchasePriceWithVat: 50m);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        var series = captured!.ProductCosts["MAT-1"];
        series.Should().NotBeEmpty();
        series.Should().AllSatisfy(mc => mc.Cost.Should().Be(50m));

        // Series spans the SUT window [now - DefaultHistoryDays, now] aligned to first-of-month.
        var expectedMonthCount = ExpectedMonthCount(captured.DataFrom, captured.DataTo);
        series.Should().HaveCount(expectedMonthCount);
    }

    // ===== FR-3: Carry-forward fills gap months =====

    [Fact]
    internal async Task RefreshAsync_CarriesForwardLastKnownPrice_WhenMonthHasNoManufacture()
    {
        // Arrange — anchors: M1 = 4 months ago, M2 = 3 months ago, M3 = 2 months ago, M4 = 1 month ago.
        // Records exist at M1 (100m) and M3 (200m). Expect carry-forward in M2 (=100m) and M4 (=200m).
        var m1 = MonthOffsetFromNow(-4);
        var m2 = MonthOffsetFromNow(-3);
        var m3 = MonthOffsetFromNow(-2);
        var m4 = MonthOffsetFromNow(-1);

        var product = BuildManufacturedProduct(
            productCode: "PROD-CF",
            type: ProductType.Product,
            history: new[]
            {
                (m1.AddDays(5), 100m, 1.0),
                (m3.AddDays(5), 200m, 1.0),
            });

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        var series = captured!.ProductCosts["PROD-CF"];

        series.Single(mc => mc.Month == m1).Cost.Should().Be(100m);
        series.Single(mc => mc.Month == m2).Cost.Should().Be(100m); // carry-forward
        series.Single(mc => mc.Month == m3).Cost.Should().Be(200m);
        series.Single(mc => mc.Month == m4).Cost.Should().Be(200m); // carry-forward

        // No gaps between m1 and m4 — every month present.
        var inRange = series.Where(mc => mc.Month >= m1 && mc.Month <= m4).OrderBy(mc => mc.Month).ToList();
        inRange.Should().HaveCount(4);
        inRange.Select(mc => mc.Month).Should().Equal(m1, m2, m3, m4);
    }

    // ===== FR-4: Future-manufacture backfill =====

    [Fact]
    internal async Task RefreshAsync_BackfillsFromEarliestFuturePrice_WhenNoPriorManufactureExists()
    {
        // Arrange — only manufacture record sits 2 months ago at 300m. Months 4 and 3 months ago
        // precede the record and must be backfilled with 300m.
        var earliest = MonthOffsetFromNow(-2);
        var product = BuildManufacturedProduct(
            productCode: "PROD-BF",
            type: ProductType.Product,
            history: new[] { (earliest.AddDays(5), 300m, 1.0) });

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert — every month in the SUT's window contains 300m (backfill + record + carry-forward).
        captured.Should().NotBeNull();
        var series = captured!.ProductCosts["PROD-BF"];
        series.Should().NotBeEmpty();
        series.Should().AllSatisfy(mc => mc.Cost.Should().Be(300m));
    }

    [Fact]
    internal async Task RefreshAsync_BackfillsUsingEarliestFuturePrice_WhenMultipleFutureRecordsExist()
    {
        // Arrange — records at M3 (=300m) and M5 (=500m), backfill must pick 300m (earliest),
        // not 500m (latest). Earlier months M1, M2 must read 300m. M4 carries forward 300m.
        var m1 = MonthOffsetFromNow(-5);
        var m2 = MonthOffsetFromNow(-4);
        var m3 = MonthOffsetFromNow(-3);
        var m4 = MonthOffsetFromNow(-2);
        var m5 = MonthOffsetFromNow(-1);

        var product = BuildManufacturedProduct(
            productCode: "PROD-BF2",
            type: ProductType.Product,
            history: new[]
            {
                (m3.AddDays(5), 300m, 1.0),
                (m5.AddDays(5), 500m, 1.0),
            });

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        var series = captured!.ProductCosts["PROD-BF2"];

        series.Single(mc => mc.Month == m1).Cost.Should().Be(300m); // backfill
        series.Single(mc => mc.Month == m2).Cost.Should().Be(300m); // backfill
        series.Single(mc => mc.Month == m3).Cost.Should().Be(300m);
        series.Single(mc => mc.Month == m4).Cost.Should().Be(300m); // carry-forward
        series.Single(mc => mc.Month == m5).Cost.Should().Be(500m);
    }

    // ===== FR-5: Empty/null manufacture history falls back to purchase price =====

    [Fact]
    internal async Task RefreshAsync_FallsBackToPurchasePrice_WhenManufactureHistoryIsEmptyList()
    {
        // Arrange — manufactured-type product with explicitly empty history.
        var product = BuildManufacturedProduct(
            productCode: "PROD-EMPTY",
            type: ProductType.Product,
            history: Array.Empty<(DateTime, decimal, double)>(),
            purchasePriceWithVat: 75m);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        var series = captured!.ProductCosts["PROD-EMPTY"];
        series.Should().NotBeEmpty();
        series.Should().AllSatisfy(mc => mc.Cost.Should().Be(75m));
        series.Should().HaveCount(ExpectedMonthCount(captured.DataFrom, captured.DataTo));
    }

    [Fact]
    internal async Task RefreshAsync_FallsBackToPurchasePrice_WhenManufactureHistoryDefaultsToEmpty()
    {
        // Arrange — manufactured-type product, do not touch ManufactureHistory (default empty list).
        var product = BuildManufacturedProduct(
            productCode: "PROD-DEFAULT",
            type: ProductType.Product,
            history: null,
            purchasePriceWithVat: 75m);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        var series = captured!.ProductCosts["PROD-DEFAULT"];
        series.Should().NotBeEmpty();
        series.Should().AllSatisfy(mc => mc.Cost.Should().Be(75m));
    }

    // ===== FR-6: Missing/non-positive purchase price returns empty list =====

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    internal async Task RefreshAsync_ReturnsEmptyCostList_WhenPurchasePriceIsNullOrNonPositive(double? priceOrNull)
    {
        // Arrange — non-manufactured product so we hit the purchase-price path directly.
        decimal? price = priceOrNull.HasValue ? (decimal?)priceOrNull.Value : null;
        var product = BuildNonManufacturedProduct(
            productCode: "MAT-EMPTY",
            type: ProductType.Material,
            purchasePriceWithVat: price);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        captured!.ProductCosts.Should().ContainKey("MAT-EMPTY");
        captured.ProductCosts["MAT-EMPTY"].Should().BeEmpty();
    }

    [Fact]
    internal async Task RefreshAsync_ReturnsEmptyCostList_WhenManufacturedTypeFallbackHasNoPurchasePrice()
    {
        // Arrange — manufactured type with empty history and no ErpPrice → fallback returns empty.
        var product = BuildManufacturedProduct(
            productCode: "PROD-NO-PRICE",
            type: ProductType.Product,
            history: Array.Empty<(DateTime, decimal, double)>(),
            purchasePriceWithVat: null);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        captured!.ProductCosts.Should().ContainKey("PROD-NO-PRICE");
        captured.ProductCosts["PROD-NO-PRICE"].Should().BeEmpty();
    }

    // ===== FR-7: Weighted-average per month aggregation =====

    [Fact]
    internal async Task RefreshAsync_AggregatesWithWeightedAverage_WhenMonthHasMultipleManufactureRecords()
    {
        // Arrange — two records in the same month: (100, 10) and (200, 30) → weighted avg = 175m.
        // A second month has a single record (300, 5) which must remain 300m (independent month).
        var monthA = MonthOffsetFromNow(-3);
        var monthB = MonthOffsetFromNow(-2);

        var product = BuildManufacturedProduct(
            productCode: "PROD-WA",
            type: ProductType.Product,
            history: new[]
            {
                (monthA.AddDays(2),  100m, 10.0),
                (monthA.AddDays(20), 200m, 30.0),
                (monthB.AddDays(5),  300m,  5.0),
            });

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        var series = captured!.ProductCosts["PROD-WA"];
        series.Single(mc => mc.Month == monthA).Cost.Should().Be(175m);
        series.Single(mc => mc.Month == monthB).Cost.Should().Be(300m);
    }

    // ===== FR-8: Zero total amount throws DivideByZeroException =====

    [Fact]
    internal async Task RefreshAsync_ThrowsDivideByZero_WhenMonthlyAmountSumIsZero()
    {
        // Arrange — two records in the same month both with Amount = 0; weighted-average denominator is 0.
        // The SUT uses `decimal` arithmetic, so `decimal / 0m` raises DivideByZeroException
        // (decimal does NOT produce NaN — that's a double-only behavior).
        // The exception is logged at Error and rethrown by RefreshAsync; this test pins that contract.
        var month = MonthOffsetFromNow(-2);
        var product = BuildManufacturedProduct(
            productCode: "PROD-DBZ",
            type: ProductType.Product,
            history: new[]
            {
                (month.AddDays(2), 100m, 0.0),
                (month.AddDays(3), 200m, 0.0),
            });

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

        var cacheMock = new Mock<IMaterialCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, loggerMock: loggerMock);

        // Act + Assert
        await Assert.ThrowsAsync<DivideByZeroException>(() => provider.RefreshAsync());

        // The exception path logs at Error and never writes the cache.
        VerifyLog(loggerMock, LogLevel.Error, "Failed to refresh MaterialCostCache");
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ===== FR-9: GetCostsAsync filters by product codes when cache hydrated =====

    [Fact]
    internal async Task GetCostsAsync_ReturnsFullDictionary_WhenProductCodesAreNull()
    {
        var hydrated = BuildHydratedCacheData(new[] { "A", "B", "C" });
        var cacheMock = new Mock<IMaterialCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydrated);

        var provider = CreateProvider(cacheMock: cacheMock);

        var result = await provider.GetCostsAsync(null);

        result.Should().HaveCount(3);
        result.Keys.Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsFullDictionary_WhenProductCodesAreEmpty()
    {
        var hydrated = BuildHydratedCacheData(new[] { "A", "B", "C" });
        var cacheMock = new Mock<IMaterialCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydrated);

        var provider = CreateProvider(cacheMock: cacheMock);

        var result = await provider.GetCostsAsync(new List<string>());

        result.Should().HaveCount(3);
        result.Keys.Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsSubset_WhenProductCodesProvided()
    {
        var hydrated = BuildHydratedCacheData(new[] { "A", "B", "C" });
        var cacheMock = new Mock<IMaterialCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydrated);

        var provider = CreateProvider(cacheMock: cacheMock);

        var result = await provider.GetCostsAsync(new List<string> { "A", "C", "MISSING" });

        result.Should().HaveCount(2);
        result.Keys.Should().BeEquivalentTo("A", "C");
    }

    [Fact]
    internal async Task GetCostsAsync_ReturnsEmptyDictionary_WhenRequestedProductCodeNotInCache()
    {
        var hydrated = BuildHydratedCacheData(new[] { "A", "B", "C" });
        var cacheMock = new Mock<IMaterialCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydrated);

        var provider = CreateProvider(cacheMock: cacheMock);

        var result = await provider.GetCostsAsync(new List<string> { "MISSING" });

        result.Should().BeEmpty();
    }

    // ===== FR-10: GetCostsAsync returns empty + warning when cache not hydrated =====

    [Fact]
    internal async Task GetCostsAsync_ReturnsEmptyAndLogsWarning_WhenCacheNotHydrated()
    {
        // Arrange
        var cacheMock = new Mock<IMaterialCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CostCacheData.Empty());

        var repoMock = new Mock<ICatalogRepository>();
        var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, loggerMock: loggerMock);

        // Act
        var result = await provider.GetCostsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        VerifyLog(loggerMock, LogLevel.Warning, "MaterialCostCache not hydrated");

        // Cache hydration miss must not trigger any repository work.
        repoMock.VerifyNoOtherCalls();
    }

    // ===== FR-11: GetCostsAsync logs error and rethrows when cache throws =====

    [Fact]
    internal async Task GetCostsAsync_LogsErrorAndRethrows_WhenCacheReadFails()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var cacheMock = new Mock<IMaterialCostCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ThrowsAsync(boom);

        var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, loggerMock: loggerMock);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetCostsAsync());

        // Assert
        thrown.Should().BeSameAs(boom);
        VerifyLog(loggerMock, LogLevel.Error, "Error getting material costs");
    }

    // ===== FR-12a: RefreshAsync populates cache on success =====

    [Fact]
    internal async Task RefreshAsync_PopulatesCache_OnSuccess_AndExcludesEmptyProductCodes()
    {
        // Arrange — three real products + two skip cases (empty + null code).
        var products = new List<CatalogAggregate>
        {
            BuildNonManufacturedProduct("MAT-A", ProductType.Material, 10m),
            BuildNonManufacturedProduct("MAT-B", ProductType.Material, 20m),
            BuildNonManufacturedProduct("MAT-C", ProductType.Material, 30m),
            BuildNonManufacturedProduct(string.Empty, ProductType.Material, 99m), // skipped
            BuildNonManufacturedProduct(null!, ProductType.Material, 99m),         // skipped
        };

        var callOrder = new List<string>();

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("WaitForCurrentMergeAsync"))
            .Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("GetAllAsync"))
            .ReturnsAsync(products);

        var cacheMock = BuildCaptureCacheMock(out var subscribe);
        CostCacheData? captured = null;
        subscribe(d => captured = d);

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, manufactureCostHistoryDays: 90);

        var nowBefore = DateTime.UtcNow;

        // Act
        await provider.RefreshAsync();

        var nowAfter = DateTime.UtcNow;

        // Assert — cache populated with exactly the three non-empty product codes.
        captured.Should().NotBeNull();
        captured!.IsHydrated.Should().BeTrue();
        captured.ProductCosts.Keys.Should().BeEquivalentTo("MAT-A", "MAT-B", "MAT-C");

        // DataFrom/DataTo within tolerant window around UtcNow.
        var expectedFromLower = DateOnly.FromDateTime(nowBefore.AddDays(-90));
        var expectedFromUpper = DateOnly.FromDateTime(nowAfter.AddDays(-90));
        captured.DataFrom.Should().BeOnOrAfter(expectedFromLower);
        captured.DataFrom.Should().BeOnOrBefore(expectedFromUpper);

        captured.DataTo.Should().BeOnOrAfter(DateOnly.FromDateTime(nowBefore));
        captured.DataTo.Should().BeOnOrBefore(DateOnly.FromDateTime(nowAfter));

        // Call order
        callOrder.Should().Equal("WaitForCurrentMergeAsync", "GetAllAsync");
    }

    // ===== FR-12b: RefreshAsync skips concurrent second call =====

    [Fact]
    internal async Task RefreshAsync_SkipsSecondInvocation_WhenFirstStillRunning()
    {
        // Arrange — gate `GetAllAsync` with a TaskCompletionSource so the first refresh blocks
        // inside ComputeAllCostsAsync, holding the static _refreshLock. The second refresh must
        // detect WaitAsync(0) failure, log "refresh already in progress", and return.
        var gate = new TaskCompletionSource<IEnumerable<CatalogAggregate>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).Returns(gate.Task);

        var cacheMock = new Mock<IMaterialCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, loggerMock: loggerMock);

        // Act 1 — first refresh; blocks on gate after acquiring the lock.
        var firstRefresh = provider.RefreshAsync();

        while (!repoMock.Invocations.Any(i => i.Method.Name == nameof(ICatalogRepository.GetAllAsync)))
        {
            await Task.Yield();
        }

        // Act 2 — second refresh; sees lock held, short-circuits.
        await provider.RefreshAsync();

        // Assert intermediate
        VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress");
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Release gate, let first refresh finish.
        gate.SetResult((IEnumerable<CatalogAggregate>)new List<CatalogAggregate>());
        await firstRefresh;

        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Act 3 — fresh refresh after lock released must proceed.
        await provider.RefreshAsync();

        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ===== FR-12c: RefreshAsync releases lock when repository throws =====

    [Fact]
    internal async Task RefreshAsync_ReleasesLockOnRepositoryException_AndAllowsSubsequentRefresh()
    {
        // Arrange — first GetAllAsync throws, second succeeds. Lock must be released by the SUT's
        // `finally` block so the second call proceeds rather than logging "refresh already in progress".
        var boom = new InvalidOperationException("repo offline");

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.SetupSequence(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom)
            .ReturnsAsync(new List<CatalogAggregate>());

        var cacheMock = new Mock<IMaterialCostCache>();
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

        var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, loggerMock: loggerMock);

        // Act 1 — first call throws and logs.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RefreshAsync());
        thrown.Should().BeSameAs(boom);
        VerifyLog(loggerMock, LogLevel.Error, "Failed to refresh MaterialCostCache");
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Act 2 — second call must proceed (lock released by finally).
        await provider.RefreshAsync();

        // Positive proof: the second call entered the normal execution path, never the "skip" branch.
        VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress", Times.Never());
        VerifyLog(loggerMock, LogLevel.Information, "Starting MaterialCostCache refresh", Times.Exactly(2));
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
