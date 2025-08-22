using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Handlers;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class GetProductMarginSummaryHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IProductMarginAnalysisService> _marginAnalysisServiceMock;
    private readonly GetProductMarginSummaryHandler _handler;

    public GetProductMarginSummaryHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _marginAnalysisServiceMock = new Mock<IProductMarginAnalysisService>();
        _handler = new GetProductMarginSummaryHandler(_catalogRepositoryMock.Object, _marginAnalysisServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsCorrectResponse()
    {
        // Arrange
        var request = new GetProductMarginSummaryRequest
        {
            TimeWindow = "current-year",
            TopProductCount = 3
        };

        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        var products = new List<CatalogAggregate>
        {
            CreateTestProduct("PROD001", "Product 1", 100m, new[]
            {
                new CatalogSaleRecord { Date = new DateTime(2024, 3, 15), AmountB2B = 10, AmountB2C = 5 }
            }),
            CreateTestProduct("PROD002", "Product 2", 50m, new[]
            {
                new CatalogSaleRecord { Date = new DateTime(2024, 4, 20), AmountB2B = 20, AmountB2C = 10 }
            })
        };

        var marginMap = new Dictionary<string, decimal>
        {
            { "PROD001", 1500m }, // 15 units * 100 margin
            { "PROD002", 1500m }  // 30 units * 50 margin
        };

        _marginAnalysisServiceMock
            .Setup(x => x.ParseTimeWindow("current-year"))
            .Returns((fromDate, toDate));

        _catalogRepositoryMock
            .Setup(x => x.GetProductsWithSalesInPeriod(fromDate, toDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _marginAnalysisServiceMock
            .Setup(x => x.CalculateProductTotalMargin(products, fromDate, toDate))
            .Returns(marginMap);

        _marginAnalysisServiceMock
            .Setup(x => x.CalculateMaterialCosts(It.IsAny<CatalogAggregate>()))
            .Returns(20m);

        _marginAnalysisServiceMock
            .Setup(x => x.CalculateLaborCosts(It.IsAny<CatalogAggregate>()))
            .Returns(15m);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3000m, result.TotalMargin); // Sum of both products
        Assert.Equal("current-year", result.TimeWindow);
        Assert.Equal(fromDate, result.FromDate);
        Assert.Equal(toDate, result.ToDate);
        Assert.Equal(2, result.TopProducts.Count); // Both products should be in top products
        Assert.NotEmpty(result.MonthlyData);
    }

    [Theory]
    [InlineData("current-year")]
    [InlineData("last-6-months")]
    [InlineData("last-12-months")]
    public async Task Handle_DifferentTimeWindows_ParsesCorrectly(string timeWindow)
    {
        // Arrange
        var request = new GetProductMarginSummaryRequest { TimeWindow = timeWindow };
        var expectedDates = (DateTime.Today.AddMonths(-6), DateTime.Today);

        _marginAnalysisServiceMock
            .Setup(x => x.ParseTimeWindow(timeWindow))
            .Returns(expectedDates);

        _catalogRepositoryMock
            .Setup(x => x.GetProductsWithSalesInPeriod(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        _marginAnalysisServiceMock
            .Setup(x => x.CalculateProductTotalMargin(It.IsAny<List<CatalogAggregate>>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns(new Dictionary<string, decimal>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(timeWindow, result.TimeWindow);
        _marginAnalysisServiceMock.Verify(x => x.ParseTimeWindow(timeWindow), Times.Once);
    }

    private static CatalogAggregate CreateTestProduct(string productCode, string productName, decimal marginAmount, CatalogSaleRecord[] salesHistory)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productName,
            Type = ProductType.Product,
            MarginAmount = marginAmount,
            SalesHistory = salesHistory.ToList(),
            EshopPrice = new ProductPriceEshop
            {
                ProductCode = productCode,
                PriceWithoutVat = marginAmount + 50m, // Some selling price
                PriceWithVat = (marginAmount + 50m) * 1.21m
            },
            ManufactureCostHistory = new List<ManufactureCost>
            {
                new ManufactureCost
                {
                    Date = DateTime.Now.AddDays(-30),
                    MaterialCost = 20m,
                    HandlingCost = 15m
                }
            }
        };
    }
}