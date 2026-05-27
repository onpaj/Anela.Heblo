using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public sealed class CatalogAnalyticsSourceAdapterTests
{
    private static CatalogAggregate CreateCatalogAggregate(
        string productCode = "TEST001",
        string productName = "Test Product",
        ProductType type = ProductType.Product)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productName,
            Type = type,
            SalesHistory = new List<CatalogSaleRecord>(),
            PurchaseHistory = new List<CatalogPurchaseRecord>(),
            Margins = new MonthlyMarginHistory(),
            EshopPrice = null
        };
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_TranslatesAnalyticsProductTypeToCatalogProductTypeAtBoundary()
    {
        // Arrange
        var productTypes = new[] { AnalyticsProductType.Product, AnalyticsProductType.Goods };
        var product = CreateCatalogAggregate("PROD001", "Test", ProductType.Product);

        var repoMock = new Mock<ICatalogRepository>();
        ProductType[]? capturedProductTypes = null;
        repoMock
            .Setup(r => r.GetProductsWithSalesInPeriod(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ProductType[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, ProductType[], CancellationToken>(
                (_, _, types, _) => capturedProductTypes = types)
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var adapter = new CatalogAnalyticsSourceAdapter(repoMock.Object);
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        // Act
        var result = new List<AnalyticsProduct>();
        await foreach (var item in adapter.StreamProductsWithSalesAsync(fromDate, toDate, productTypes))
        {
            result.Add(item);
        }

        // Assert
        capturedProductTypes.Should().NotBeNull();
        capturedProductTypes.Should().HaveCount(2);
        capturedProductTypes.Should().Contain(new[] { ProductType.Product, ProductType.Goods });
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_MapsM0M1M2AmountsFromLatestMarginEntryInPeriod()
    {
        // Arrange
        var product = CreateCatalogAggregate("PROD001", "Test", ProductType.Product);
        product.Margins.MonthlyData = new Dictionary<DateTime, MarginData>
        {
            [new DateTime(2024, 1, 1)] = new MarginData
            {
                M0 = new MarginLevel(10m, 10m, 0m, 5m),
                M1_A = new MarginLevel(15m, 20m, 0m, 7m),
                M2 = new MarginLevel(20m, 30m, 0m, 0m)
            },
            [new DateTime(2024, 6, 1)] = new MarginData
            {
                M0 = new MarginLevel(12m, 12m, 0m, 6m),
                M1_A = new MarginLevel(18m, 22m, 0m, 8m),
                M2 = new MarginLevel(22m, 32m, 0m, 0m)
            }
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock
            .Setup(r => r.GetProductsWithSalesInPeriod(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ProductType[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var adapter = new CatalogAnalyticsSourceAdapter(repoMock.Object);
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        // Act
        var result = new List<AnalyticsProduct>();
        await foreach (var item in adapter.StreamProductsWithSalesAsync(
            fromDate, toDate, new[] { AnalyticsProductType.Product }))
        {
            result.Add(item);
        }

        // Assert
        result.Should().HaveCount(1);
        result[0].M0Amount.Should().Be(12m); // Latest entry
        result[0].M1Amount.Should().Be(22m); // M1_A.Amount
        result[0].M2Amount.Should().Be(32m);
        result[0].MaterialCost.Should().Be(6m); // M0.CostLevel
        result[0].HandlingCost.Should().Be(8m); // M1_A.CostLevel
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_FallsBackToAveragesWhenNoMonthlyMarginDataInPeriod()
    {
        // Arrange
        var product = CreateCatalogAggregate("PROD001", "Test", ProductType.Product);
        product.Margins = new MonthlyMarginHistory { MonthlyData = new Dictionary<DateTime, MarginData>() };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock
            .Setup(r => r.GetProductsWithSalesInPeriod(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ProductType[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var adapter = new CatalogAnalyticsSourceAdapter(repoMock.Object);
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        // Act
        var result = new List<AnalyticsProduct>();
        await foreach (var item in adapter.StreamProductsWithSalesAsync(
            fromDate, toDate, new[] { AnalyticsProductType.Product }))
        {
            result.Add(item);
        }

        // Assert
        result.Should().HaveCount(1);
        result[0].MarginAmount.Should().Be(0m); // Empty Averages returns Zero
        result[0].M0Amount.Should().Be(0m);
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_FiltersSalesHistoryToTheRequestedPeriod()
    {
        // Arrange
        var product = CreateCatalogAggregate("PROD001", "Test", ProductType.Product);
        product.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2023, 12, 31), AmountB2B = 5, AmountB2C = 3, SumB2B = 50, SumB2C = 30 },
            new CatalogSaleRecord { Date = new DateTime(2024, 6, 15), AmountB2B = 10, AmountB2C = 8, SumB2B = 100, SumB2C = 80 },
            new CatalogSaleRecord { Date = new DateTime(2025, 1, 1), AmountB2B = 2, AmountB2C = 1, SumB2B = 20, SumB2C = 10 }
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock
            .Setup(r => r.GetProductsWithSalesInPeriod(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ProductType[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var adapter = new CatalogAnalyticsSourceAdapter(repoMock.Object);
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        // Act
        var result = new List<AnalyticsProduct>();
        await foreach (var item in adapter.StreamProductsWithSalesAsync(
            fromDate, toDate, new[] { AnalyticsProductType.Product }))
        {
            result.Add(item);
        }

        // Assert
        result.Should().HaveCount(1);
        result[0].SalesHistory.Should().HaveCount(1);
        result[0].SalesHistory[0].Date.Should().Be(new DateTime(2024, 6, 15));
        result[0].SalesHistory[0].AmountB2B.Should().Be(10);
        result[0].SalesHistory[0].AmountB2C.Should().Be(8);
    }

    [Fact]
    public async Task StreamProductsWithSalesAsync_TakesLatestPurchasePrice()
    {
        // Arrange
        var product = CreateCatalogAggregate("PROD001", "Test", ProductType.Product);
        product.PurchaseHistory = new List<CatalogPurchaseRecord>
        {
            new CatalogPurchaseRecord { Date = new DateTime(2024, 1, 1), PricePerPiece = 10m, Amount = 100, PriceTotal = 1000 },
            new CatalogPurchaseRecord { Date = new DateTime(2024, 6, 15), PricePerPiece = 15m, Amount = 50, PriceTotal = 750 },
            new CatalogPurchaseRecord { Date = new DateTime(2024, 12, 1), PricePerPiece = 12m, Amount = 75, PriceTotal = 900 }
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock
            .Setup(r => r.GetProductsWithSalesInPeriod(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ProductType[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var adapter = new CatalogAnalyticsSourceAdapter(repoMock.Object);
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        // Act
        var result = new List<AnalyticsProduct>();
        await foreach (var item in adapter.StreamProductsWithSalesAsync(
            fromDate, toDate, new[] { AnalyticsProductType.Product }))
        {
            result.Add(item);
        }

        // Assert
        result.Should().HaveCount(1);
        result[0].PurchasePrice.Should().Be(12m); // Latest date
    }

    [Fact]
    public async Task GetProductAnalysisDataAsync_ReturnsNullWhenRepositoryReturnsNull()
    {
        // Arrange
        var repoMock = new Mock<ICatalogRepository>();
        repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        var adapter = new CatalogAnalyticsSourceAdapter(repoMock.Object);

        // Act
        var result = await adapter.GetProductAnalysisDataAsync(
            "NONEXISTENT",
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProductAnalysisDataAsync_PreservesUnfilteredSalesHistory()
    {
        // Arrange
        var product = CreateCatalogAggregate("PROD001", "Test", ProductType.Product);
        product.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2023, 6, 15), AmountB2B = 5, AmountB2C = 3, SumB2B = 50, SumB2C = 30 },
            new CatalogSaleRecord { Date = new DateTime(2024, 6, 15), AmountB2B = 10, AmountB2C = 8, SumB2B = 100, SumB2C = 80 },
            new CatalogSaleRecord { Date = new DateTime(2025, 6, 15), AmountB2B = 2, AmountB2C = 1, SumB2B = 20, SumB2C = 10 }
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock
            .Setup(r => r.GetByIdAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var adapter = new CatalogAnalyticsSourceAdapter(repoMock.Object);
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        // Act
        var result = await adapter.GetProductAnalysisDataAsync("PROD001", fromDate, toDate);

        // Assert
        result.Should().NotBeNull();
        result!.SalesHistory.Should().HaveCount(3); // All 3 records, not filtered by period
        result.SalesHistory.Should().Satisfy(
            s => s.Date == new DateTime(2023, 6, 15),
            s => s.Date == new DateTime(2024, 6, 15),
            s => s.Date == new DateTime(2025, 6, 15));
    }

    [Fact]
    public async Task GetProductAnalysisDataAsync_MapsPricingAndMarginFields()
    {
        // Arrange
        var product = CreateCatalogAggregate("PROD001", "Test Product", ProductType.Goods);
        product.EshopPrice = new ProductPriceEshop { PriceWithoutVat = 100m };
        product.PurchaseHistory = new List<CatalogPurchaseRecord>
        {
            new CatalogPurchaseRecord { Date = new DateTime(2024, 6, 15), PricePerPiece = 50m, Amount = 10, PriceTotal = 500 }
        };
        product.Margins.MonthlyData = new Dictionary<DateTime, MarginData>
        {
            [new DateTime(2024, 6, 1)] = new MarginData
            {
                M0 = new MarginLevel(20m, 20m, 0m, 25m),
                M1_A = new MarginLevel(25m, 25m, 0m, 30m),
                M2 = new MarginLevel(30m, 30m, 0m, 0m)
            }
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock
            .Setup(r => r.GetByIdAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var adapter = new CatalogAnalyticsSourceAdapter(repoMock.Object);

        // Act
        var result = await adapter.GetProductAnalysisDataAsync(
            "PROD001",
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31));

        // Assert
        result.Should().NotBeNull();
        result!.ProductCode.Should().Be("PROD001");
        result.ProductName.Should().Be("Test Product");
        result.Type.Should().Be(AnalyticsProductType.Goods);
        result.SellingPrice.Should().Be(100m);
        result.EshopPriceWithoutVat.Should().Be(100m);
        result.PurchasePrice.Should().Be(50m);
        result.MarginAmount.Should().Be(20m);
        result.M0Amount.Should().Be(20m);
        result.M1Amount.Should().Be(25m);
        result.M2Amount.Should().Be(30m);
    }
}
