using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class LogisticsCatalogSourceAdapterTests
{
    private readonly Mock<ICatalogRepository> _repository = new();

    private LogisticsCatalogSourceAdapter CreateAdapter() => new(_repository.Object);

    private static CatalogAggregate MakeAggregate(
        string code,
        ProductType type = ProductType.Set,
        string productName = "Product",
        string? image = null,
        decimal eshopStock = 0m,
        decimal erpStock = 0m,
        decimal transport = 0m,
        StockSource primaryStockSource = StockSource.Erp,
        decimal stockMinSetup = 0m,
        int optimalStockDaysSetup = 0)
    {
        return new CatalogAggregate
        {
            ProductCode = code,
            ProductName = productName,
            Type = type,
            Image = image,
            Stock = new StockData
            {
                Eshop = eshopStock,
                Erp = erpStock,
                Transport = transport,
                PrimaryStockSource = primaryStockSource,
            },
            Properties = new CatalogProperties
            {
                StockMinSetup = stockMinSetup,
                OptimalStockDaysSetup = optimalStockDaysSetup,
            },
        };
    }

    private static readonly DateTime From = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetGiftPackageSetsAsync_ReturnsOnlySetTypeProducts()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetAllAsync(ct))
            .ReturnsAsync(new[]
            {
                MakeAggregate("SET-1", ProductType.Set),
                MakeAggregate("MAT-1", ProductType.Material),
                MakeAggregate("GOODS-1", ProductType.Goods),
            });

        var result = await CreateAdapter().GetGiftPackageSetsAsync(From, To, ct);

        result.Should().ContainSingle();
        result[0].ProductCode.Should().Be("SET-1");
    }

    [Fact]
    public async Task GetGiftPackageSetsAsync_ProjectsAllFields()
    {
        var ct = CancellationToken.None;
        var aggregate = MakeAggregate(
            code: "SET-1",
            type: ProductType.Set,
            productName: "Gift Set",
            image: "image.jpg",
            erpStock: 10m,
            transport: 5m,
            primaryStockSource: StockSource.Erp,
            stockMinSetup: 3m,
            optimalStockDaysSetup: 14);

        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2025, 6, 1), AmountB2B = 4, AmountB2C = 2 },
        };

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { aggregate });

        var result = (await CreateAdapter().GetGiftPackageSetsAsync(From, To, ct)).Single();

        result.ProductCode.Should().Be("SET-1");
        result.ProductName.Should().Be("Gift Set");
        result.Image.Should().Be("image.jpg");
        result.AvailableStock.Should().Be(aggregate.Stock.Available);
        result.TotalSoldInPeriod.Should().Be(aggregate.GetTotalSold(From, To));
        result.StockMinSetup.Should().Be((int)aggregate.Properties.StockMinSetup);
        result.OptimalStockDaysSetup.Should().Be(14);
    }

    [Fact]
    public async Task GetGiftPackageAsync_ReturnsNullForNonSetProduct()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetByIdAsync("MAT-1", ct))
            .ReturnsAsync(MakeAggregate("MAT-1", ProductType.Material));

        var result = await CreateAdapter().GetGiftPackageAsync("MAT-1", From, To, ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGiftPackageAsync_ReturnsNullWhenProductNotFound()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetByIdAsync("MISSING", ct))
            .ReturnsAsync((CatalogAggregate?)null);

        var result = await CreateAdapter().GetGiftPackageAsync("MISSING", From, To, ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGiftPackageAsync_ProjectsFieldsCorrectly()
    {
        var ct = CancellationToken.None;
        var aggregate = MakeAggregate(
            code: "SET-2",
            type: ProductType.Set,
            productName: "Premium Set",
            image: "premium.jpg",
            erpStock: 20m,
            transport: 3m,
            primaryStockSource: StockSource.Erp,
            stockMinSetup: 5m,
            optimalStockDaysSetup: 30);

        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2025, 3, 1), AmountB2B = 10, AmountB2C = 5 },
        };

        _repository.Setup(r => r.GetByIdAsync("SET-2", ct)).ReturnsAsync(aggregate);

        var result = await CreateAdapter().GetGiftPackageAsync("SET-2", From, To, ct);

        result.Should().NotBeNull();
        result!.ProductCode.Should().Be("SET-2");
        result.ProductName.Should().Be("Premium Set");
        result.Image.Should().Be("premium.jpg");
        result.AvailableStock.Should().Be(aggregate.Stock.Available);
        result.TotalSoldInPeriod.Should().Be(aggregate.GetTotalSold(From, To));
        result.StockMinSetup.Should().Be((int)aggregate.Properties.StockMinSetup);
        result.OptimalStockDaysSetup.Should().Be(30);
    }

    [Fact]
    public async Task GetCatalogItemAsync_ReturnsNullWhenNotFound()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetByIdAsync("MISSING", ct))
            .ReturnsAsync((CatalogAggregate?)null);

        var result = await CreateAdapter().GetCatalogItemAsync("MISSING", ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCatalogItemAsync_ProjectsImageAndEshopStock()
    {
        var ct = CancellationToken.None;
        var aggregate = MakeAggregate(
            code: "PROD-1",
            type: ProductType.Product,
            image: "product.png",
            eshopStock: 7m,
            erpStock: 12m,
            transport: 2m,
            primaryStockSource: StockSource.Erp);

        _repository.Setup(r => r.GetByIdAsync("PROD-1", ct)).ReturnsAsync(aggregate);

        var result = await CreateAdapter().GetCatalogItemAsync("PROD-1", ct);

        result.Should().NotBeNull();
        result!.ProductCode.Should().Be("PROD-1");
        result.Image.Should().Be("product.png");
        result.EshopStock.Should().Be(7m);
        result.AvailableStock.Should().Be(aggregate.Stock.Available);
    }
}
