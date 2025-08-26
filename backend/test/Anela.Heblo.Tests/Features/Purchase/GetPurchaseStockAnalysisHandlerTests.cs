using Anela.Heblo.Application.Features.Purchase;
using FluentAssertions;
using Anela.Heblo.Application.Features.Purchase.Model;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Moq;
using FluentAssertions;
using Xunit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Purchase;

public class GetPurchaseStockAnalysisHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly Mock<ILogger<GetPurchaseStockAnalysisHandler>> _loggerMock;
    private readonly GetPurchaseStockAnalysisHandler _handler;

    public GetPurchaseStockAnalysisHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _loggerMock = new Mock<ILogger<GetPurchaseStockAnalysisHandler>>();
        _handler = new GetPurchaseStockAnalysisHandler(_catalogRepositoryMock.Object, _stockSeverityCalculatorMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsAnalysisResponse()
    {
        var catalogItems = CreateTestCatalogItems();
        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            FromDate = DateTime.UtcNow.AddMonths(-6),
            ToDate = DateTime.UtcNow,
            StockStatus = StockStatusFilter.All,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().NotBeEmpty();
        response.TotalCount.Should().Be(2);
        response.PageNumber.Should().Be(1);
        response.PageSize.Should().Be(10);
        response.Summary.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_FilterByCriticalStatus_ReturnsOnlyCriticalItems()
    {
        var catalogItems = CreateTestCatalogItems();
        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        var request = new GetPurchaseStockAnalysisRequest
        {
            StockStatus = StockStatusFilter.Critical,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        Assert.All(response.Items, item => Assert.Equal(StockSeverity.Critical, item.Severity));
    }

    [Fact]
    public async Task Handle_OnlyConfiguredFilter_ReturnsOnlyConfiguredItems()
    {
        var catalogItems = CreateTestCatalogItems();
        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        var request = new GetPurchaseStockAnalysisRequest
        {
            OnlyConfigured = true,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        Assert.All(response.Items, item => Assert.True(item.IsConfigured));
    }

    [Fact]
    public async Task Handle_SearchTerm_FiltersItemsBySearchTerm()
    {
        var catalogItems = CreateTestCatalogItems();
        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        var request = new GetPurchaseStockAnalysisRequest
        {
            SearchTerm = "MAT001",
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().HaveCount(1);
        response.Items[0].ProductCode.Should().Be("MAT001");
    }

    [Fact]
    public async Task Handle_InvalidDateRange_ThrowsArgumentException()
    {
        var request = new GetPurchaseStockAnalysisRequest
        {
            FromDate = DateTime.UtcNow,
            ToDate = DateTime.UtcNow.AddDays(-1)
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPage()
    {
        var catalogItems = CreateManyTestCatalogItems(25);
        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        var request = new GetPurchaseStockAnalysisRequest
        {
            PageNumber = 2,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Count.Should().Be(10);
        response.TotalCount.Should().Be(25);
        response.PageNumber.Should().Be(2);
    }

    [Fact]
    public async Task Handle_SortByStockEfficiency_ReturnsSortedItems()
    {
        var catalogItems = CreateTestCatalogItems();
        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        var request = new GetPurchaseStockAnalysisRequest
        {
            SortBy = StockAnalysisSortBy.StockEfficiency,
            SortDescending = true,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        var efficiencies = response.Items.Select(i => i.StockEfficiencyPercentage).ToList();
        efficiencies.Should().BeEquivalentTo(efficiencies.OrderByDescending(e => e));
    }

    private List<CatalogAggregate> CreateTestCatalogItems()
    {
        return new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "MAT001",
                ProductName = "Material 1",
                Type = ProductType.Material,
                Stock = new StockData { Erp = 10, Eshop = 0, Transport = 0, Reserve = 0 },
                Properties = new CatalogProperties { StockMinSetup = 50, OptimalStockDaysSetup = 30 },
                SupplierNames = new List<string> { "Supplier A" },
                MinimalOrderQuantity = "100",
                PurchaseHistory = new List<CatalogPurchaseRecord>
                {
                    new CatalogPurchaseRecord
                    {
                        Date = DateTime.UtcNow.AddDays(-30),
                        SupplierName = "Supplier A",
                        Amount = 100,
                        PricePerPiece = 10,
                        PriceTotal = 1000
                    }
                }
            },
            new CatalogAggregate
            {
                ProductCode = "GOD001",
                ProductName = "Goods 1",
                Type = ProductType.Goods,
                Stock = new StockData { Erp = 100, Eshop = 0, Transport = 0, Reserve = 0 },
                Properties = new CatalogProperties { StockMinSetup = 20, OptimalStockDaysSetup = 14 },
                SupplierNames = new List<string> { "Supplier B" },
                MinimalOrderQuantity = "50",
                SalesHistory = new List<CatalogSaleRecord>
                {
                    new CatalogSaleRecord
                    {
                        Date = DateTime.UtcNow.AddDays(-7),
                        AmountB2B = 5,
                        AmountB2C = 10,
                        SumB2B = 500,
                        SumB2C = 1000
                    }
                }
            }
        };
    }

    private List<CatalogAggregate> CreateManyTestCatalogItems(int count)
    {
        var items = new List<CatalogAggregate>();

        for (int i = 0; i < count; i++)
        {
            items.Add(new CatalogAggregate
            {
                ProductCode = $"MAT{i:D3}",
                ProductName = $"Material {i}",
                Type = i % 2 == 0 ? ProductType.Material : ProductType.Goods,
                Stock = new StockData { Erp = i * 10, Eshop = 0, Transport = 0, Reserve = 0 },
                Properties = new CatalogProperties
                {
                    StockMinSetup = i * 5,
                    OptimalStockDaysSetup = i % 3 == 0 ? 0 : 30
                },
                SupplierNames = new List<string> { $"Supplier {i}" },
                MinimalOrderQuantity = (i * 10).ToString()
            });
        }

        return items;
    }
}