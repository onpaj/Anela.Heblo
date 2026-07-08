using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityMaterialLotStockQueryAdapterTests
{
    private readonly Mock<ICatalogRepository> _catalogMock = new();

    private DataQualityMaterialLotStockQueryAdapter CreateAdapter() => new(_catalogMock.Object);

    private void SetupCatalog(params CatalogAggregate[] items) =>
        _catalogMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<CatalogAggregate>)items.ToList());

    private static CatalogAggregate Item(
        string code,
        ProductType type,
        bool hasExpiration,
        decimal erp,
        params decimal[] lotAmounts)
    {
        var item = new CatalogAggregate
        {
            ProductCode = code,
            Type = type,
            HasExpiration = hasExpiration,
            Stock = new StockData { Erp = erp }
        };
        foreach (var amount in lotAmounts)
            item.Stock.Lots.Add(new CatalogLot { ProductCode = code, Amount = amount });
        return item;
    }

    [Fact]
    public async Task GetMaterialsWithExpirationAsync_ProjectsErpAndLotAmounts()
    {
        // Arrange
        SetupCatalog(Item("MAT001", ProductType.Material, hasExpiration: true, erp: 10m, lotAmounts: new[] { 4m, 6m }));

        // Act
        var result = await CreateAdapter().GetMaterialsWithExpirationAsync(CancellationToken.None);

        // Assert
        var snapshot = result.Single();
        snapshot.ProductCode.Should().Be("MAT001");
        snapshot.ErpStock.Should().Be(10m);
        snapshot.LotAmounts.Should().Equal(4m, 6m);
    }

    [Fact]
    public async Task GetMaterialsWithExpirationAsync_ExcludesNonMaterial()
    {
        // Arrange
        SetupCatalog(Item("PROD001", ProductType.Product, hasExpiration: true, erp: 10m, lotAmounts: new[] { 1m }));

        // Act
        var result = await CreateAdapter().GetMaterialsWithExpirationAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMaterialsWithExpirationAsync_ExcludesMaterialWithoutExpiration()
    {
        // Arrange
        SetupCatalog(Item("MAT001", ProductType.Material, hasExpiration: false, erp: 10m, lotAmounts: new[] { 1m }));

        // Act
        var result = await CreateAdapter().GetMaterialsWithExpirationAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMaterialsWithExpirationAsync_ReturnsOnlyInScopeItems()
    {
        // Arrange
        SetupCatalog(
            Item("MAT001", ProductType.Material, hasExpiration: true, erp: 10m, lotAmounts: new[] { 10m }),
            Item("PROD001", ProductType.Product, hasExpiration: true, erp: 5m, lotAmounts: new[] { 5m }),
            Item("MAT002", ProductType.Material, hasExpiration: false, erp: 3m, lotAmounts: new[] { 3m })
        );

        // Act
        var result = await CreateAdapter().GetMaterialsWithExpirationAsync(CancellationToken.None);

        // Assert
        result.Select(s => s.ProductCode).Should().Equal("MAT001");
    }

    [Fact]
    public async Task GetMaterialsWithExpirationAsync_WhenRepositoryEmpty_ReturnsEmpty()
    {
        // Arrange
        SetupCatalog();

        // Act
        var result = await CreateAdapter().GetMaterialsWithExpirationAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMaterialsWithExpirationAsync_PropagatesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _catalogMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<CatalogAggregate>)new List<CatalogAggregate>());

        // Act
        await CreateAdapter().GetMaterialsWithExpirationAsync(cts.Token);

        // Assert
        _catalogMock.Verify(r => r.GetAllAsync(cts.Token), Times.Once);
    }
}
