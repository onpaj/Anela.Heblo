using Anela.Heblo.Application.Features.Catalog.DashboardTiles;
using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DashboardTiles;

public class LowStockAlertTileTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<IOptions<DataSourceOptions>> _dataSourceOptionsMock;
    private readonly LowStockAlertTile _tile;
    private readonly DateTime _fixedDateTime = new DateTime(2025, 10, 20, 10, 0, 0, DateTimeKind.Utc);
    private readonly DataSourceOptions _dataSourceOptions;

    public LowStockAlertTileTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedDateTime);
        
        _dataSourceOptions = new DataSourceOptions 
        { 
            SalesHistoryDays = 365,
            ResupplyThresholdMultiplier = 1.3 
        };
        _dataSourceOptionsMock = new Mock<IOptions<DataSourceOptions>>();
        _dataSourceOptionsMock.Setup(x => x.Value).Returns(_dataSourceOptions);
        
        _tile = new LowStockAlertTile(_catalogRepositoryMock.Object, _timeProviderMock.Object, _dataSourceOptionsMock.Object);
    }
    
    [Fact]
    public async Task LoadDataAsync_WithProductsHavingReserveStock_ReturnsCorrectData()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            // Low eshop stock, has reserve stock, has sales history => should be included
            CreateProductWithStock("PROD001", "Test Product 1", eshopStock: 5, reserveStock: 10, transportStock: 3, ProductType.Product, CreateSalesHistory(3650)), // Daily avg = 10, threshold = 13, 5 <= 13 AND reserve > 0
            
            // High eshop stock, has reserve stock => should NOT be included
            CreateProductWithStock("PROD002", "Test Product 2", eshopStock: 50, reserveStock: 5, transportStock: 2, ProductType.Product, CreateSalesHistory(365)), // Daily avg = 1, threshold = 1.3, 50 > 1.3
            
            // Low eshop stock, NO reserve stock => should NOT be included
            CreateProductWithStock("PROD003", "Test Product 3", eshopStock: 1, reserveStock: 0, transportStock: 1, ProductType.Product, CreateSalesHistory(365))
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        result.Should().NotBeNull();
        
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        
        var productsJson = doc.RootElement.GetProperty("data").GetProperty("products").GetRawText();
        var products_result = JsonSerializer.Deserialize<List<LowStockProductData>>(productsJson);
        
        products_result.Should().NotBeNull();
        products_result.Should().HaveCount(1); // Only PROD001 should be included
        
        var product = products_result.First();
        product.ProductCode.Should().Be("PROD001");
        product.ProductName.Should().Be("Test Product 1");
        product.EshopStock.Should().Be(5);
        product.ReserveStock.Should().Be(10);
        product.TransportStock.Should().Be(3);
    }

    [Fact]
    public async Task LoadDataAsync_WithNoReserveStock_ReturnsEmptyList()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            // High eshop stock, no reserve stock => should NOT be included
            CreateProductWithStock("PROD001", "Test Product 1", eshopStock: 100, reserveStock: 0, transportStock: 5, ProductType.Product, CreateSalesHistory(365)),
            // Low eshop stock, no reserve stock => should NOT be included
            CreateProductWithStock("PROD002", "Test Product 2", eshopStock: 1, reserveStock: 0, transportStock: 2, ProductType.Product, CreateSalesHistory(365))
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        
        var productsJson = doc.RootElement.GetProperty("data").GetProperty("products").GetRawText();
        var products_result = JsonSerializer.Deserialize<List<LowStockProductData>>(productsJson);
        
        products_result.Should().NotBeNull();
        products_result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadDataAsync_FiltersByProductType_OnlyProductsAndGoods()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateProductWithStock("PROD001", "Product", eshopStock: 1, reserveStock: 5, transportStock: 1, ProductType.Product, CreateSalesHistory(365)), // 1 <= 1*1.3 = 1.3 AND reserve > 0 => included
            CreateProductWithStock("GOODS001", "Goods", eshopStock: 1, reserveStock: 3, transportStock: 2, ProductType.Goods, CreateSalesHistory(365)), // 1 <= 1*1.3 = 1.3 AND reserve > 0 => included  
            CreateProductWithStock("MAT001", "Material", eshopStock: 1, reserveStock: 2, transportStock: 1, ProductType.Material, CreateSalesHistory(365)), // Should be filtered out
            CreateProductWithStock("SEMI001", "Semi-Product", eshopStock: 1, reserveStock: 4, transportStock: 3, ProductType.SemiProduct, CreateSalesHistory(365)) // Should be filtered out
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        
        var productsJson = doc.RootElement.GetProperty("data").GetProperty("products").GetRawText();
        var products_result = JsonSerializer.Deserialize<List<LowStockProductData>>(productsJson);
        
        products_result.Should().HaveCount(2); // Only Product and Goods types
        products_result.Should().Contain(p => p.ProductCode == "PROD001");
        products_result.Should().Contain(p => p.ProductCode == "GOODS001");
        products_result.Should().NotContain(p => p.ProductCode == "MAT001");
        products_result.Should().NotContain(p => p.ProductCode == "SEMI001");
    }

    [Fact]
    public async Task LoadDataAsync_SortsProductsByLowestStock()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateProductWithStock("PROD001", "Product 1", eshopStock: 10, reserveStock: 8, transportStock: 2, ProductType.Product, CreateSalesHistory(5475)), // 10 <= 15*1.3=19.5 AND reserve > 0 => included
            CreateProductWithStock("PROD002", "Product 2", eshopStock: 5, reserveStock: 12, transportStock: 1, ProductType.Product, CreateSalesHistory(5475)), // 5 <= 15*1.3=19.5 AND reserve > 0 => included
            CreateProductWithStock("PROD003", "Product 3", eshopStock: 15, reserveStock: 6, transportStock: 3, ProductType.Product, CreateSalesHistory(5475)) // 15 <= 15*1.3=19.5 AND reserve > 0 => included
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        
        var productsJson = doc.RootElement.GetProperty("data").GetProperty("products").GetRawText();
        var products_result = JsonSerializer.Deserialize<List<LowStockProductData>>(productsJson);
        
        products_result.Should().HaveCount(3);
        products_result[0].ProductCode.Should().Be("PROD002"); // Lowest stock (5)
        products_result[1].ProductCode.Should().Be("PROD001"); // Middle stock (10)
        products_result[2].ProductCode.Should().Be("PROD003"); // Highest stock (15)
    }

    [Fact]
    public async Task LoadDataAsync_WithNoSalesHistory_IncludesProductsWithReserveStock()
    {
        // Arrange - products with no sales history should be included only if they have reserve stock
        var products = new List<CatalogAggregate>
        {
            CreateProductWithStock("PROD001", "Product with no sales and reserve", eshopStock: 5, reserveStock: 10, transportStock: 2, ProductType.Product, new List<CatalogSaleRecord>()), // No sales history, has reserve - included
            CreateProductWithStock("PROD002", "Product with no sales, no reserve", eshopStock: 5, reserveStock: 0, transportStock: 2, ProductType.Product, new List<CatalogSaleRecord>()), // No sales history, no reserve - excluded
            CreateProductWithStock("PROD003", "Product with sales", eshopStock: 1, reserveStock: 8, transportStock: 1, ProductType.Product, CreateSalesHistory(1095)) // Stock = 1 <= threshold = 3*1.3 = 3.9 AND reserve > 0 - included
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        
        var productsJson = doc.RootElement.GetProperty("data").GetProperty("products").GetRawText();
        var products_result = JsonSerializer.Deserialize<List<LowStockProductData>>(productsJson);
        
        products_result.Should().HaveCount(2); // Only products with reserve stock
        
        var noHistoryProduct = products_result.First(p => p.ProductCode == "PROD001");
        noHistoryProduct.AverageDailySales.Should().Be(0);
        noHistoryProduct.DaysOfStockRemaining.Should().Be(decimal.MaxValue);
        noHistoryProduct.ReserveStock.Should().Be(10);
        
        var withHistoryProduct = products_result.First(p => p.ProductCode == "PROD003");
        withHistoryProduct.AverageDailySales.Should().Be(3); // 1095/365 = 3
        withHistoryProduct.ReserveStock.Should().Be(8);
        
        products_result.Should().NotContain(p => p.ProductCode == "PROD002"); // No reserve stock
    }

    [Fact]
    public async Task LoadDataAsync_VerifiesStockDataDisplayCorrectly()
    {
        // Arrange - test specific stock values for frontend display
        var products = new List<CatalogAggregate>
        {
            CreateProductWithStock("PROD001", "Test Product", eshopStock: 15, reserveStock: 25, transportStock: 8, ProductType.Product, CreateSalesHistory(7300)), // Daily avg = 20, threshold = 26, 15 <= 26 AND reserve > 0
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        
        var productsJson = doc.RootElement.GetProperty("data").GetProperty("products").GetRawText();
        var products_result = JsonSerializer.Deserialize<List<LowStockProductData>>(productsJson);
        
        products_result.Should().HaveCount(1);
        
        var product = products_result.First();
        product.ProductCode.Should().Be("PROD001");
        product.ProductName.Should().Be("Test Product");
        product.EshopStock.Should().Be(15); // S (skladem)
        product.ReserveStock.Should().Be(25); // R (rezerva)
        product.TransportStock.Should().Be(8); // T (transport)
    }

    [Fact]
    public async Task LoadDataAsync_HandlesExceptions_ReturnsErrorStatus()
    {
        // Arrange
        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetString().Should().Be("Database error");
    }

    [Fact]
    public async Task LoadDataAsync_VerifiesDrillDownConfiguration()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateProductWithStock("PROD001", "Test Product", eshopStock: 1, reserveStock: 5, transportStock: 2, ProductType.Product, CreateSalesHistory(365))
        };

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        
        // Verify drill-down configuration
        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
        drillDown.GetProperty("tooltip").GetString().Should().Be("Zobrazit inventuru produktů s nízkou zásobou");
        
        var filters = drillDown.GetProperty("filters");
        filters.GetProperty("sortBy").GetString().Should().Be("eshop");
        filters.GetProperty("sortDescending").GetBoolean().Should().BeFalse();
        filters.GetProperty("type").GetString().Should().Be("Product");
    }

    private CatalogAggregate CreateProductWithStock(string code, string name, decimal eshopStock, decimal reserveStock, decimal transportStock,
        ProductType type, List<CatalogSaleRecord> salesHistory)
    {
        var product = new CatalogAggregate
        {
            ProductCode = code,
            ProductName = name,
            Type = type,
            Stock = new StockData
            {
                Eshop = eshopStock,
                Reserve = reserveStock,
                Transport = transportStock
            },
            SalesHistory = salesHistory
        };

        return product;
    }

    private List<CatalogSaleRecord> CreateSalesHistory(double totalSalesForYear)
    {
        var salesHistory = new List<CatalogSaleRecord>();
        var startDate = _fixedDateTime.AddDays(-365);
        
        // Distribute sales evenly across the year
        for (int i = 0; i < 365; i++)
        {
            salesHistory.Add(new CatalogSaleRecord
            {
                Date = startDate.AddDays(i),
                ProductCode = "PROD001",
                ProductName = "Test Product",
                AmountTotal = totalSalesForYear / 365.0,
                AmountB2B = 0,
                AmountB2C = totalSalesForYear / 365.0
            });
        }

        return salesHistory;
    }
}