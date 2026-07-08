using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class LotStockReconciliationComparerTests
{
    private readonly Mock<ICatalogRepository> _catalogMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private LotStockReconciliationComparer CreateSut() => new(_catalogMock.Object);

    private void SetupCatalog(params CatalogAggregate[] items) =>
        _catalogMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<CatalogAggregate>)items.ToList());

    private static CatalogAggregate Material(
        string code,
        decimal erp,
        bool hasExpiration = true,
        params decimal[] lotAmounts)
    {
        var item = new CatalogAggregate
        {
            ProductCode = code,
            Type = ProductType.Material,
            HasExpiration = hasExpiration,
            Stock = new StockData { Erp = erp }
        };
        foreach (var amount in lotAmounts)
            item.Stock.Lots.Add(new CatalogLot { ProductCode = code, Amount = amount });
        return item;
    }

    [Fact]
    public async Task TestType_IsLotSumVsErpStock()
    {
        CreateSut().TestType.Should().Be(DqtTestType.LotSumVsErpStock);
    }

    [Fact]
    public async Task CompareAsync_ReturnsNoMismatch_WhenLotSumEqualsErp_AndCountsItem()
    {
        // Arrange
        SetupCatalog(Material("MAT001", erp: 10m, lotAmounts: new[] { 4m, 6m }));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(1);
    }

    [Fact]
    public async Task CompareAsync_ReturnsNoMismatch_WhenDifferenceWithinTolerance()
    {
        // Arrange — |10.005 - 10| = 0.005 <= 0.01
        SetupCatalog(Material("MAT001", erp: 10m, lotAmounts: new[] { 10.005m }));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(1);
    }

    [Fact]
    public async Task CompareAsync_ReturnsSumMismatch_WhenLotSumDiffersBeyondTolerance()
    {
        // Arrange
        SetupCatalog(Material("MAT001", erp: 10m, lotAmounts: new[] { 5m, 3m }));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        var mismatch = result.Mismatches.Single();
        mismatch.EntityKey.Should().Be("MAT001");
        ((LotStockReconciliationMismatch)mismatch.MismatchCode)
            .Should().Be(LotStockReconciliationMismatch.SumMismatch);
        mismatch.HebloValue.Should().Be(10m.ToString("F2"));
        mismatch.ShoptetValue.Should().Be(8m.ToString("F2"));
        mismatch.Details.Should()
            .Contain($"ERP: {10m:F2}")
            .And.Contain($"Šarže: {8m:F2}")
            .And.Contain($"Rozdíl: {-2m:F2}");
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingLots_WhenErpPositiveButNoLots()
    {
        // Arrange
        SetupCatalog(Material("MAT001", erp: 10m));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        ((LotStockReconciliationMismatch)result.Mismatches.Single().MismatchCode)
            .Should().Be(LotStockReconciliationMismatch.MissingLots);
    }

    [Fact]
    public async Task CompareAsync_ReturnsOrphanLots_WhenLotsPresentButErpZero()
    {
        // Arrange
        SetupCatalog(Material("MAT001", erp: 0m, lotAmounts: new[] { 5m }));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        ((LotStockReconciliationMismatch)result.Mismatches.Single().MismatchCode)
            .Should().Be(LotStockReconciliationMismatch.OrphanLots);
    }

    [Fact]
    public async Task CompareAsync_ExcludesNonMaterial_EvenWhenLotSumMismatches()
    {
        // Arrange
        var goods = new CatalogAggregate
        {
            ProductCode = "PROD001",
            Type = ProductType.Product,
            HasExpiration = true,
            Stock = new StockData { Erp = 10m }
        };
        goods.Stock.Lots.Add(new CatalogLot { ProductCode = "PROD001", Amount = 1m });
        SetupCatalog(goods);

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(0);
    }

    [Fact]
    public async Task CompareAsync_ExcludesMaterialWithoutExpiration()
    {
        // Arrange
        SetupCatalog(Material("MAT001", erp: 10m, hasExpiration: false, lotAmounts: new[] { 1m }));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(0);
    }

    [Fact]
    public async Task CompareAsync_ReturnsEmpty_WhenRepositoryEmpty()
    {
        // Arrange
        SetupCatalog();

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(0);
    }

    [Fact]
    public async Task CompareAsync_CountsAllInScopeItems_AndReportsOnlyMismatches()
    {
        // Arrange
        SetupCatalog(
            Material("OK001", erp: 10m, lotAmounts: new[] { 10m }),          // matches
            Material("BAD001", erp: 10m, lotAmounts: new[] { 3m }),          // SumMismatch
            Material("MISS001", erp: 5m),                                    // MissingLots
            Material("ORPH001", erp: 0m, lotAmounts: new[] { 2m }),          // OrphanLots
            Material("NOEXP001", erp: 5m, hasExpiration: false, lotAmounts: new[] { 1m }) // excluded
        );

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.TotalChecked.Should().Be(4); // NOEXP001 excluded
        result.Mismatches.Select(m => m.EntityKey)
            .Should().BeEquivalentTo(new[] { "BAD001", "MISS001", "ORPH001" });
    }

    [Fact]
    public async Task CompareAsync_IgnoresDateRange()
    {
        // Arrange
        SetupCatalog(Material("MAT001", erp: 10m, lotAmounts: new[] { 10m }));

        // Act — arbitrary range must not affect the snapshot result
        var result = await CreateSut().CompareAsync(
            new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 2), CancellationToken.None);

        // Assert
        result.TotalChecked.Should().Be(1);
        result.Mismatches.Should().BeEmpty();
    }
}
