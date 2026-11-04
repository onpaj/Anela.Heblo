using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Accounting.CostPools;
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
/// The defining property of revenue-proportional allocation: whatever the catalogue looks like,
/// the costs handed out add back up to the pool that was handed in.
///
///     Σ (naklad na kus × prodane kusy) == pool
///
/// The per-product tests pin individual figures, which a formula can satisfy while still leaking
/// or inventing money overall - a product excluded from the numerator but left in the denominator
/// under-allocates, and one left in the denominator with negative revenue over-allocates to
/// everybody else. Both sides have to agree on who is allocatable, and this is the test that says
/// so out loud, for M2 and M3 alike.
/// </summary>
[Collection(CostProviderRefreshLockCollection.Name)]
public class MarginPoolConservationTests
{
    private const int HistoryDays = 90;
    private const decimal Pool = 10_000m;

    /// <summary>
    /// A catalogue with every shape the allocation has to survive: a cheap high-volume product, an
    /// expensive low-volume one, a product that sold nothing in the window, and one whose returns
    /// outweighed its sales. Only the first two earn a share.
    /// </summary>
    private static List<CatalogAggregate> BuildMixedCatalog(DateTime saleDate, DateTime beforeWindow)
    {
        return new List<CatalogAggregate>
        {
            BuildProduct("CHEAP", new[] { (saleDate, 100.0, 5_000m) }),
            BuildProduct("PREMIUM", new[] { (saleDate, 10.0, 15_000m) }),
            BuildProduct("DORMANT", new[] { (beforeWindow, 40.0, 8_000m) }),
            BuildProduct("RETURNED", new[] { (saleDate, 4.0, 400m), (saleDate, -7.0, -800m) })
        };
    }

    [Fact]
    internal async Task M2_AllocatesExactlyThePool_AcrossAMixedCatalog()
    {
        // Arrange
        var (saleDate, beforeWindow) = BuildWindowDates();
        var products = BuildMixedCatalog(saleDate, beforeWindow);

        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock
            .Setup(s => s.GetCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>(), "SKLAD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics> { new() { Date = saleDate, Cost = Pool, Department = "SKLAD" } });
        ledgerMock
            .Setup(s => s.GetCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>(), "MARKETING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        CostCacheData? captured = null;
        var cacheMock = new Mock<ISalesCostCache>();
        cacheMock
            .Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
            .Returns(Task.CompletedTask);

        var serviceProviderMock = BuildServiceProvider(products);

        var provider = new SalesCostProvider(
            cacheMock.Object,
            serviceProviderMock,
            ledgerMock.Object,
            new Mock<ILogger<SalesCostProvider>>().Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = HistoryDays }),
            TimeProvider.System);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        AllocatedTotal(captured!, products, saleDate).Should().Be(Pool);
    }

    [Fact]
    internal async Task M3_AllocatesExactlyThePool_AcrossAMixedCatalog()
    {
        // Arrange
        var (saleDate, beforeWindow) = BuildWindowDates();
        var products = BuildMixedCatalog(saleDate, beforeWindow);

        var costPoolMock = new Mock<ICostPoolService>();
        costPoolMock
            .Setup(s => s.GetMonthlyPoolsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyCostPool> { new(saleDate, CostPool.M3, Pool) });

        CostCacheData? captured = null;
        var cacheMock = new Mock<IOverheadCostCache>();
        cacheMock
            .Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
            .Returns(Task.CompletedTask);

        var provider = new OverheadCostProvider(
            cacheMock.Object,
            BuildServiceProvider(products),
            costPoolMock.Object,
            new Mock<ILogger<OverheadCostProvider>>().Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = HistoryDays }),
            TimeProvider.System);

        // Act
        await provider.RefreshAsync();

        // Assert
        captured.Should().NotBeNull();
        AllocatedTotal(captured!, products, saleDate).Should().Be(Pool);
    }

    // ===== Helpers =====

    /// <summary>
    /// What the catalogue actually gets charged: each product's flat cost per piece multiplied by
    /// the pieces it sold in the window. Products charged nothing contribute nothing, whether they
    /// were excluded for having no sales or for having returned more than they sold.
    /// </summary>
    private static decimal AllocatedTotal(
        CostCacheData captured,
        IEnumerable<CatalogAggregate> products,
        DateTime saleDate)
    {
        var total = 0m;

        foreach (var product in products)
        {
            var costPerPiece = captured.ProductCosts[product.ProductCode!].First().Cost;
            var pieces = (decimal)product.SalesHistory.Where(s => s.Date == saleDate).Sum(s => s.AmountTotal);

            total += costPerPiece * pieces;
        }

        return total;
    }

    private static (DateTime saleDate, DateTime beforeWindow) BuildWindowDates()
    {
        var now = DateTime.UtcNow;
        var saleDate = new DateTime(now.Year, now.Month, 1).AddMonths(-1).AddDays(14);

        // Comfortably outside a 90-day window, so DORMANT is genuinely dormant.
        return (saleDate, saleDate.AddYears(-1));
    }

    private static IServiceProvider BuildServiceProvider(List<CatalogAggregate> products)
    {
        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(products);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ICatalogRepository)))
            .Returns(repoMock.Object);

        return serviceProviderMock.Object;
    }

    private static CatalogAggregate BuildProduct(
        string productCode,
        IEnumerable<(DateTime date, double amount, decimal revenue)> sales)
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
                    AmountTotal = s.amount,
                    SumTotal = s.revenue
                })
                .ToList()
        };
    }
}
