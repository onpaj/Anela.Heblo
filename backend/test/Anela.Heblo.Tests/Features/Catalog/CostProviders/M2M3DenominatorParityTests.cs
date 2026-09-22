using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Application.Shared.CostPools;
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
/// OverheadCostProvider documents that it spreads M3 over "the denominator SalesCostProvider uses,
/// so M2 and M3 are directly comparable per piece". Nothing in the type system enforces that - a
/// correction to one provider's share of the allocation (the way the SourceBundleCode exclusion was
/// once added) can silently decouple the two levels while the comment still claims they match.
///
/// This pins the invariant end to end: feed both providers the same catalogue and a known pool each,
/// and the per-piece costs must come out in exactly the ratio of those pools. That only holds if the
/// denominators are identical.
/// </summary>
[Collection(CostProviderRefreshLockCollection.Name)]
public class M2M3DenominatorParityTests
{
    private const int HistoryDays = 90;
    private const decimal SalesPool = 1_000m;
    private const decimal OverheadPool = 3_000m;

    [Fact]
    internal async Task M2AndM3_AllocateOverTheSameSalesRevenueDenominator()
    {
        // Arrange - a catalogue that exercises the parts of the denominator most likely to drift:
        // a normal product, a product whose sales fall outside the window, and a bundle component
        // row that must not be counted on either side.
        var now = DateTime.UtcNow;
        var inWindow = new DateTime(now.Year, now.Month, 1).AddMonths(-1).AddDays(14);
        var outOfWindow = new DateTime(now.Year, now.Month, 1).AddMonths(-24);
        var month = new DateTime(inWindow.Year, inWindow.Month, 1);

        var products = new List<CatalogAggregate>
        {
            BuildProduct("PRODUCT-A", new[]
            {
                (inWindow, 100d, 25_000m, (string?)null)
            }),
            BuildProduct("PRODUCT-B", new[]
            {
                (inWindow, 40d, 4_000m, (string?)null),
                // Outside the cost window - must not reach either denominator.
                (outOfWindow, 999d, 99_900m, (string?)null),
                // Synthetic bundle component - excluded by both providers; it carries pieces but
                // no revenue, so counting it would sink PRODUCT-B's revenue per piece.
                (inWindow, 500d, 0m, (string?)"SET-001")
            })
        };

        var salesCosts = await RunSalesProviderAsync(products, month);
        var overheadCosts = await RunOverheadProviderAsync(products, month);

        // Assert
        salesCosts.Should().NotBeEmpty("the M2 provider must produce a cost for the test to mean anything");
        overheadCosts.Keys.Should().BeEquivalentTo(salesCosts.Keys);

        var expectedRatio = (double)(OverheadPool / SalesPool);

        foreach (var (productCode, salesMonthlyCosts) in salesCosts)
        {
            foreach (var salesCost in salesMonthlyCosts)
            {
                var overheadCost = overheadCosts[productCode].Single(c => c.Month == salesCost.Month);

                salesCost.Cost.Should().BeGreaterThan(0,
                    "a zero M2 cost would make the ratio vacuous for {0}", productCode);

                ((double)(overheadCost.Cost / salesCost.Cost)).Should().BeApproximately(expectedRatio, 1e-9,
                    "M3 and M2 divide their pools by the same sales-revenue denominator, so their " +
                    "per-piece costs differ only by the ratio of the pools ({0} for {1})", productCode, salesCost.Month);
            }
        }
    }

    private static async Task<Dictionary<string, List<MonthlyCost>>> RunSalesProviderAsync(
        List<CatalogAggregate> products,
        DateTime month)
    {
        var cacheMock = BuildCache<ISalesCostCache>(out var captured);

        // M2 pulls via GetCosts with its own prefix set (50/51/52), not GetDirectCosts -
        // 50x in SKLAD/MARKETING is expedition packaging and marketing print.
        var m2Prefixes = CostPoolDefinition.AccountPrefixesFor(CostPool.M2);

        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock
            .Setup(l => l.GetCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), m2Prefixes, "SKLAD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics> { new() { Date = month, Cost = SalesPool, Department = "SKLAD" } });
        ledgerMock
            .Setup(l => l.GetCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), m2Prefixes, "MARKETING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        var provider = new SalesCostProvider(
            cacheMock.Object,
            BuildServiceProvider(products),
            ledgerMock.Object,
            new Mock<ILogger<SalesCostProvider>>().Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = HistoryDays }));

        await provider.RefreshAsync();

        return captured.Value!.ProductCosts.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static async Task<Dictionary<string, List<MonthlyCost>>> RunOverheadProviderAsync(
        List<CatalogAggregate> products,
        DateTime month)
    {
        var cacheMock = BuildCache<IOverheadCostCache>(out var captured);

        var costPoolMock = new Mock<ICostPoolService>();
        costPoolMock
            .Setup(s => s.GetMonthlyPoolsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyCostPool> { new(month, CostPool.M3, OverheadPool) });

        var provider = new OverheadCostProvider(
            cacheMock.Object,
            BuildServiceProvider(products),
            costPoolMock.Object,
            new Mock<ILogger<OverheadCostProvider>>().Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = HistoryDays }),
            TimeProvider.System);

        await provider.RefreshAsync();

        return captured.Value!.ProductCosts.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static Mock<TCache> BuildCache<TCache>(out StrongBox<CostCacheData> captured)
        where TCache : class, ICostCache
    {
        var box = new StrongBox<CostCacheData>();
        captured = box;

        var mock = new Mock<TCache>();
        mock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((data, _) => box.Value = data)
            .Returns(Task.CompletedTask);

        return mock;
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
        IEnumerable<(DateTime date, double amount, decimal revenue, string? sourceBundleCode)> sales)
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
                    SumTotal = s.revenue,
                    SourceBundleCode = s.sourceBundleCode
                })
                .ToList()
        };
    }

    private sealed class StrongBox<T>
    {
        public T? Value { get; set; }
    }
}
