using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Tests.Features.Catalog.CostProviders;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

/// <summary>
/// Guards the design's central claim: the M2 total CostPoolService reports is
/// the same money SalesCostProvider spreads across products. Both read the same
/// FakeLedgerService, so agreement here is structural rather than coincidental.
///
/// Shares CostProviderRefreshLockCollection because SalesCostProvider guards RefreshAsync
/// with its own static SemaphoreSlim: refreshing beside another test that refreshes the same
/// provider makes one of the two skip its refresh and leave its cache unhydrated.
/// </summary>
[Collection(CostProviderRefreshLockCollection.Name)]
public class CostPoolSalesCostParityTests
{
    private const int HistoryDays = 90;

    private static LedgerItem Entry(
        DateTime date,
        string department,
        decimal amount,
        string debitAccountNumber = "518100") => new()
        {
            Date = date,
            Department = department,
            Amount = amount,
            DocumentNumber = "DOC",
            ClientName = "CLIENT",
            VariableSymbol = "VS",
            DebitAccountNumber = debitAccountNumber,
            DebitAccountName = "Ostatni sluzby",
            CreditAccountNumber = "321100",
            CreditAccountName = "Dodavatele"
        };

    [Fact]
    public async Task M2PoolTotal_EqualsTheSpendSalesCostProviderDistributes()
    {
        // Arrange - a single sale carrying the company's whole revenue makes
        // SalesCostProvider's cost-per-piece equal its whole pool, so the two
        // numbers are directly comparable.
        var saleDate = DateTime.UtcNow.Date.AddDays(-10);
        var entries = new List<LedgerItem>
        {
            // VYROBA deliberately differs from SKLAD+MARKETING (M2) so a
            // wholesale M1/M2 swap in CostPoolDefinition.Resolve would not
            // pass this test by coincidence.
            Entry(saleDate, "SKLAD", 30_000m),
            Entry(saleDate, "MARKETING", 70_000m),
            Entry(saleDate, "VYROBA", 90_000m),
            Entry(saleDate, "CENTRALA", 500_000m),
        };
        var ledger = new FakeLedgerService(entries);

        var product = new CatalogAggregate
        {
            ProductCode = "PROD-1",
            SalesHistory = new List<CatalogSaleRecord>
            {
                new() { Date = saleDate, ProductCode = "PROD-1", ProductName = "PROD-1", AmountTotal = 1, SumTotal = 250m }
            }
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ICatalogRepository)))
                           .Returns(repoMock.Object);

        CostCacheData? salesCosts = null;
        var salesCacheMock = new Mock<ISalesCostCache>();
        salesCacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
                      .Callback<CostCacheData, CancellationToken>((d, _) => salesCosts = d)
                      .Returns(Task.CompletedTask);

        var options = Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = HistoryDays });

        var salesProvider = new SalesCostProvider(
            salesCacheMock.Object,
            serviceProviderMock.Object,
            ledger,
            new Mock<ILogger<SalesCostProvider>>().Object,
            options);

        var poolCacheMock = new Mock<ICostPoolCache>();
        poolCacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(CostPoolCacheData.Empty());

        var poolService = new CostPoolService(
            poolCacheMock.Object,
            ledger,
            new Mock<ILogger<CostPoolService>>().Object,
            options);

        // Act
        await salesProvider.RefreshAsync();
        var pools = await poolService.GetMonthlyPoolsAsync(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-HistoryDays)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        // Assert
        salesCosts.Should().NotBeNull();
        var salesCostPerPiece = salesCosts!.ProductCosts["PROD-1"].First().Cost;
        var m2Total = pools.Where(p => p.Pool == CostPool.M2).Sum(p => p.Amount);

        m2Total.Should().Be(100_000m);
        salesCostPerPiece.Should().Be(m2Total);
    }

    [Fact]
    public async Task PoolsSumToTheFullLedgerTotal_IncludingSpendNoMarginLevelSeesToday()
    {
        // Arrange
        var date = DateTime.UtcNow.Date.AddDays(-10);
        var entries = new List<LedgerItem>
        {
            Entry(date, "SKLAD", 30_000m),
            Entry(date, "MARKETING", 70_000m),
            Entry(date, "VYROBA", 100_000m),
            Entry(date, "CENTRALA", 500_000m),
            Entry(date, "ESHOP", 250_000m),
            // BUVOL is a separate activity, not Anela overhead - excluded outright.
            Entry(date, "BUVOL", 344_911m),
            Entry(date, "BUVOL", 449_000m, debitAccountNumber: "521100"),
            // 52x is in scope everywhere and must be counted...
            Entry(date, "CENTRALA", 20_000m, debitAccountNumber: "521100"),
            // ...6xx is out of scope everywhere and must not be...
            Entry(date, "CENTRALA", 999_999m, debitAccountNumber: "601000"),
            // ...and 50x counts only inside M2, where it is packaging and print.
            // In CENTRALA the same prefix is cost of goods sold, which is an
            // order of magnitude larger than every pool combined, so letting it
            // fall into the M3 catch-all would swamp the overhead total.
            Entry(date, "SKLAD", 888_888m, debitAccountNumber: "501100"),
            Entry(date, "CENTRALA", 777_777m, debitAccountNumber: "501100"),
        };
        var poolCacheMock = new Mock<ICostPoolCache>();
        poolCacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(CostPoolCacheData.Empty());

        var poolService = new CostPoolService(
            poolCacheMock.Object,
            new FakeLedgerService(entries),
            new Mock<ILogger<CostPoolService>>().Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = HistoryDays }));

        // Act
        var pools = await poolService.GetMonthlyPoolsAsync(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-HistoryDays)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        // Assert - 770 000 of this was invisible to the system before this feature.
        // 601000 is out of scope for every pool; 501100 counts in SKLAD (M2) but
        // not in CENTRALA, so neither the total nor M3 carries the 777 777; and
        // BUVOL reaches no pool at all despite being on in-scope accounts.
        pools.Sum(p => p.Amount).Should().Be(1_858_888m);
        pools.Where(p => p.Pool == CostPool.M3).Sum(p => p.Amount).Should().Be(770_000m);
        pools.Where(p => p.Pool == CostPool.M2).Sum(p => p.Amount).Should().Be(988_888m);
        pools.Where(p => p.Pool == CostPool.M1).Sum(p => p.Amount).Should().Be(100_000m);
    }

    [Fact]
    public async Task M2PoolAndPerPieceCost_BothIncludeConsumablesBookedInWarehouseAndMarketing()
    {
        // Arrange - 501 in SKLAD is shipping packaging (cartons, printed tape) and
        // 504 in MARKETING is print; both are fulfilment/marketing spend and belong
        // in M2. The same prefix in CENTRALA is cost of goods sold and must not
        // reach any pool.
        var saleDate = DateTime.UtcNow.Date.AddDays(-10);
        var entries = new List<LedgerItem>
        {
            Entry(saleDate, "SKLAD", 30_000m),
            Entry(saleDate, "SKLAD", 4_000m, debitAccountNumber: "501001"),
            Entry(saleDate, "MARKETING", 70_000m),
            Entry(saleDate, "MARKETING", 1_000m, debitAccountNumber: "504001"),
            Entry(saleDate, "VYROBA", 90_000m, debitAccountNumber: "501001"),
            Entry(saleDate, "CENTRALA", 500_000m, debitAccountNumber: "501001"),
        };
        var ledger = new FakeLedgerService(entries);

        var product = new CatalogAggregate
        {
            ProductCode = "PROD-1",
            SalesHistory = new List<CatalogSaleRecord>
            {
                new() { Date = saleDate, ProductCode = "PROD-1", ProductName = "PROD-1", AmountTotal = 1, SumTotal = 250m }
            }
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ICatalogRepository)))
                           .Returns(repoMock.Object);

        CostCacheData? salesCosts = null;
        var salesCacheMock = new Mock<ISalesCostCache>();
        salesCacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
                      .Callback<CostCacheData, CancellationToken>((d, _) => salesCosts = d)
                      .Returns(Task.CompletedTask);

        var options = Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = HistoryDays });

        var salesProvider = new SalesCostProvider(
            salesCacheMock.Object,
            serviceProviderMock.Object,
            ledger,
            new Mock<ILogger<SalesCostProvider>>().Object,
            options);

        var poolCacheMock = new Mock<ICostPoolCache>();
        poolCacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(CostPoolCacheData.Empty());

        var poolService = new CostPoolService(
            poolCacheMock.Object,
            ledger,
            new Mock<ILogger<CostPoolService>>().Object,
            options);

        // Act
        await salesProvider.RefreshAsync();
        var pools = await poolService.GetMonthlyPoolsAsync(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-HistoryDays)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        // Assert
        salesCosts.Should().NotBeNull();
        var salesCostPerPiece = salesCosts!.ProductCosts["PROD-1"].First().Cost;

        salesCostPerPiece.Should().Be(105_000m);
        pools.Where(p => p.Pool == CostPool.M2).Sum(p => p.Amount).Should().Be(105_000m);
        pools.Where(p => p.Pool == CostPool.M1).Sum(p => p.Amount).Should().Be(0m);
        pools.Where(p => p.Pool == CostPool.M3).Sum(p => p.Amount).Should().Be(0m);
    }
}
