using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogPurchasePriceSyncSourceAdapterTests
{
    private const int MaterialWarehouseId = 5;
    private const int ProductsWarehouseId = 4;
    private static readonly DateTime Today = new(2026, 9, 23);

    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly Mock<IProductPriceErpClient> _prices = new();
    private readonly Mock<IErpStockClient> _stock = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Today.AddHours(2), TimeSpan.Zero));

    private CatalogPurchasePriceSyncSourceAdapter CreateAdapter() =>
        new(_catalog.Object, _prices.Object, _stock.Object, _time);

    private void GivenCatalog(params (string Code, ProductType Type)[] items) =>
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.Select(i => new CatalogAggregate { ProductCode = i.Code, Type = i.Type }).ToList());

    private void GivenCenik(params ProductPriceErp[] rows) =>
        _prices.Setup(p => p.GetAllAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(rows);

    private void GivenStock(int warehouseId, params (string Code, decimal Price)[] rows) =>
        _stock.Setup(s => s.StockToDateAsync(Today, warehouseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.Select(r => new ErpStock { ProductCode = r.Code, Price = r.Price }).ToList());

    private static ProductPriceErp Cenik(string code, int id, decimal purchasePrice) =>
        new() { ProductCode = code, ErpItemId = id, PurchasePrice = purchasePrice };

    [Fact]
    public async Task material_takes_prumCena_from_the_material_warehouse()
    {
        // Arrange
        GivenCatalog(("AKL097", ProductType.Material));
        GivenCenik(Cenik("AKL097", 789, 3.048594m));
        GivenStock(MaterialWarehouseId, ("AKL097", 0.311m));
        GivenStock(ProductsWarehouseId, ("OTHER", 1m));

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new PurchasePriceSyncCandidate
        {
            ProductCode = "AKL097",
            ProductType = MaterialProductType.Material,
            ErpItemId = 789,
            CurrentPurchasePrice = 3.048594m,
            StockPrice = 0.311m,
        });
    }

    [Fact]
    public async Task goods_take_prumCena_from_the_products_warehouse()
    {
        // Arrange
        GivenCatalog(("ZBO001", ProductType.Goods));
        GivenCenik(Cenik("ZBO001", 12, 100m));
        GivenStock(MaterialWarehouseId, ("OTHER", 1m));
        GivenStock(ProductsWarehouseId, ("ZBO001", 80m));

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        var candidate = result.Should().ContainSingle().Subject;
        candidate.ProductType.Should().Be(MaterialProductType.Goods);
        candidate.StockPrice.Should().Be(80m);
    }

    [Fact]
    public async Task material_with_stock_only_in_the_goods_warehouse_has_no_stock_price()
    {
        // Arrange
        GivenCatalog(("AKL097", ProductType.Material));
        GivenCenik(Cenik("AKL097", 789, 3m));
        GivenStock(MaterialWarehouseId, ("OTHER", 1m));
        GivenStock(ProductsWarehouseId, ("AKL097", 0.3m));

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.StockPrice.Should().BeNull();
    }

    [Fact]
    public async Task ignores_products_semi_products_and_sets()
    {
        // Arrange
        GivenCatalog(("DEZ001100", ProductType.Product), ("DEZ001001M", ProductType.SemiProduct), ("SET001", ProductType.Set));
        GivenCenik(Cenik("DEZ001100", 818, 214m), Cenik("DEZ001001M", 900, 2m), Cenik("SET001", 901, 50m));
        GivenStock(MaterialWarehouseId, ("OTHER", 1m));
        GivenStock(ProductsWarehouseId, ("DEZ001100", 65m));

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task excludes_items_without_a_cenik_row_or_with_erp_item_id_zero()
    {
        // Arrange
        GivenCatalog(("NO-CENIK", ProductType.Material), ("ZERO-ID", ProductType.Material));
        GivenCenik(Cenik("ZERO-ID", 0, 1m));
        GivenStock(MaterialWarehouseId, ("NO-CENIK", 1m), ("ZERO-ID", 1m));
        GivenStock(ProductsWarehouseId, ("OTHER", 1m));

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task duplicate_codes_in_cenik_or_stock_do_not_throw_and_first_row_wins()
    {
        // Arrange
        GivenCatalog(("AKL097", ProductType.Material));
        GivenCenik(Cenik("AKL097", 789, 3m), Cenik("AKL097", 790, 9m));
        GivenStock(MaterialWarehouseId, ("AKL097", 0.3m), ("AKL097", 0.9m));
        GivenStock(ProductsWarehouseId, ("OTHER", 1m));

        // Act
        var result = await CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        var candidate = result.Should().ContainSingle().Subject;
        candidate.ErpItemId.Should().Be(789);
        candidate.StockPrice.Should().Be(0.3m);
    }

    [Fact]
    public async Task propagates_a_stock_load_failure()
    {
        // Arrange
        GivenCatalog(("AKL097", ProductType.Material));
        GivenCenik(Cenik("AKL097", 789, 3m));
        _stock.Setup(s => s.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("flexi down"));

        // Act
        var act = () => CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task material_warehouse_returning_no_rows_fails_the_job()
    {
        // Arrange: Rem.FlexiBeeSDK's StockToDateClient swallows non-2xx responses and returns an
        // empty list, so an empty material warehouse must be treated as a failed read, not "no stock".
        GivenCatalog(("AKL097", ProductType.Material));
        GivenCenik(Cenik("AKL097", 789, 3m));
        GivenStock(MaterialWarehouseId);
        GivenStock(ProductsWarehouseId, ("OTHER", 1m));

        // Act
        var act = () => CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain($"warehouse {MaterialWarehouseId}").And.Contain(Today.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task goods_warehouse_returning_no_rows_fails_the_job()
    {
        // Arrange
        GivenCatalog(("ZBO001", ProductType.Goods));
        GivenCenik(Cenik("ZBO001", 12, 100m));
        GivenStock(MaterialWarehouseId, ("OTHER", 1m));
        GivenStock(ProductsWarehouseId);

        // Act
        var act = () => CreateAdapter().GetCandidatesAsync(CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain($"warehouse {ProductsWarehouseId}").And.Contain(Today.ToString("yyyy-MM-dd"));
    }
}
