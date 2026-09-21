using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.Sales;
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
/// Shares the SalesCostProviderTests collection because SalesCostProvider guards
/// RefreshAsync with its own static SemaphoreSlim.
/// </summary>
[Collection("SalesCostProviderTests")]
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
        // Arrange - one sold piece makes SalesCostProvider's cost-per-piece
        // equal its whole pool, so the two numbers are directly comparable.
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
                new() { Date = saleDate, ProductCode = "PROD-1", ProductName = "PROD-1", AmountTotal = 1 }
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
            // 52x is in scope and must be counted...
            Entry(date, "CENTRALA", 20_000m, debitAccountNumber: "521100"),
            // ...while 6xx and 501 are out of scope and must not be, which is
            // what makes this an "on accounts 51+52" invariant rather than
            // "whatever the ledger happened to hand back".
            Entry(date, "CENTRALA", 999_999m, debitAccountNumber: "601000"),
            Entry(date, "SKLAD", 888_888m, debitAccountNumber: "501100"),
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
        // The 601000 and 501100 entries are excluded by the 51+52 prefix filter,
        // so they appear in neither the total nor any individual pool.
        pools.Sum(p => p.Amount).Should().Be(970_000m);
        pools.Where(p => p.Pool == CostPool.M3).Sum(p => p.Amount).Should().Be(770_000m);
        pools.Where(p => p.Pool == CostPool.M2).Sum(p => p.Amount).Should().Be(100_000m);
        pools.Where(p => p.Pool == CostPool.M1).Sum(p => p.Amount).Should().Be(100_000m);
    }
}
