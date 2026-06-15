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
}
