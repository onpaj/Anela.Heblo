using Anela.Heblo.Application.Features.Catalog.DashboardTiles;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DashboardTiles;

public class LowStockAlertTileTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly LowStockAlertTile _tile;
    private readonly DateTime _fixedDateTime = new DateTime(2025, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    public LowStockAlertTileTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedDateTime);
        _tile = new LowStockAlertTile(_catalogRepositoryMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public void Tile_ShouldHave_CorrectConfiguration()
    {
        // Assert
        _tile.Title.Should().Be("Produkty blížící se vyprodání");
        _tile.Description.Should().Be("Produkty s nízkou zásobou na základě průměrných prodejů");
        _tile.Size.Should().Be(Anela.Heblo.Xcc.Services.Dashboard.TileSize.Medium);
        _tile.Category.Should().Be(Anela.Heblo.Xcc.Services.Dashboard.TileCategory.Warehouse);
        _tile.DefaultEnabled.Should().BeTrue();
        _tile.AutoShow.Should().BeTrue();
        _tile.RequiredPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadDataAsync_WithLowStockProducts_ReturnsCorrectData()
    {
        // Arrange
        var oneYearAgo = _fixedDateTime.AddDays(-365);
        var products = CreateTestProducts();

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
        products_result.Should().HaveCount(1); // Only PROD001 should be in low stock
        
        var lowStockProduct = products_result.First();
        lowStockProduct.ProductCode.Should().Be("PROD001");
        lowStockProduct.ProductName.Should().Be("Test Product 1");
        lowStockProduct.EshopStock.Should().Be(5);
        lowStockProduct.ReserveStock.Should().Be(2);
        lowStockProduct.TransportStock.Should().Be(1);
    }

    [Fact]
    public async Task LoadDataAsync_WithNoLowStockProducts_ReturnsEmptyList()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateProduct("PROD001", "Test Product 1", 100, 
                ProductType.Product, CreateSalesHistory(365)) // High stock, low sales (daily avg = 1)
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
            CreateProduct("PROD001", "Product", 1, ProductType.Product, CreateSalesHistory(365)), // 1 <= 1*1.3 = 1.3 => included
            CreateProduct("GOODS001", "Goods", 1, ProductType.Goods, CreateSalesHistory(365)), // 1 <= 1*1.3 = 1.3 => included  
            CreateProduct("MAT001", "Material", 1, ProductType.Material, CreateSalesHistory(365)), // Should be filtered out
            CreateProduct("SEMI001", "Semi-Product", 1, ProductType.SemiProduct, CreateSalesHistory(365)) // Should be filtered out
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
            CreateProduct("PROD001", "Product 1", 10, ProductType.Product, CreateSalesHistory(5475)), // 10 <= 15*1.3=19.5 => included
            CreateProduct("PROD002", "Product 2", 5, ProductType.Product, CreateSalesHistory(5475)), // 5 <= 15*1.3=19.5 => included
            CreateProduct("PROD003", "Product 3", 15, ProductType.Product, CreateSalesHistory(5475)) // 15 <= 15*1.3=19.5 => included
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
    public async Task LoadDataAsync_WithNoSalesHistory_UsesZeroThreshold()
    {
        // Arrange - products with no sales history should be included (any stock is considered low)
        var products = new List<CatalogAggregate>
        {
            CreateProduct("PROD001", "Product with no sales", 5, ProductType.Product, new List<CatalogSaleRecord>()), // No sales history - included
            CreateProduct("PROD002", "Product with sales", 1, ProductType.Product, CreateSalesHistory(1095)) // Stock = 1 <= threshold = 3*1.3 = 3.9 - included
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
        
        products_result.Should().HaveCount(2); // Both products should be included
        
        var noHistoryProduct = products_result.First(p => p.ProductCode == "PROD001");
        noHistoryProduct.AverageDailySales.Should().Be(0);
        noHistoryProduct.DaysOfStockRemaining.Should().Be(decimal.MaxValue);
        
        var withHistoryProduct = products_result.First(p => p.ProductCode == "PROD002");
        withHistoryProduct.AverageDailySales.Should().Be(3); // 1095/365 = 3
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

    private List<CatalogAggregate> CreateTestProducts()
    {
        return new List<CatalogAggregate>
        {
            // Low stock product: stock 5 <= daily sales (3650/365=10) * 1.3 = 13
            CreateProduct("PROD001", "Test Product 1", 5, ProductType.Product, CreateSalesHistory(3650)),
            
            // High stock product: stock 50 > daily sales (365/365=1) * 1.3 = 1.3
            CreateProduct("PROD002", "Test Product 2", 50, ProductType.Product, CreateSalesHistory(365)),
            
            // Material (should be filtered out)
            CreateProduct("MAT001", "Test Material", 1, ProductType.Material, CreateSalesHistory(365))
        };
    }

    private CatalogAggregate CreateProduct(string code, string name, decimal eshopStock, 
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
                Reserve = 2,
                Transport = 1
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