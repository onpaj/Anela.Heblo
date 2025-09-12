using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class BatchPlanningServiceTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IManufactureRepository> _manufactureRepositoryMock;
    private readonly Mock<ILogger<BatchPlanningService>> _loggerMock;
    private readonly BatchPlanningService _service;

    public BatchPlanningServiceTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _manufactureRepositoryMock = new Mock<IManufactureRepository>();
        _loggerMock = new Mock<ILogger<BatchPlanningService>>();
        _service = new BatchPlanningService(_catalogRepositoryMock.Object, _manufactureRepositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CalculateBatchPlan_SemiproductNotFound_ThrowsArgumentException()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "NONEXISTENT",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0
        };

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync("NONEXISTENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.CalculateBatchPlan(request, CancellationToken.None));
        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public async Task CalculateBatchPlan_NoProductsUsingIngredientsFound_ThrowsArgumentException()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0
        };

        var semiproduct = CreateSemiproduct("SEMI001", 1000);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync("SEMI001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(semiproduct);

        _manufactureRepositoryMock.Setup(x => x.FindByIngredientAsync("SEMI001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureTemplate>());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.CalculateBatchPlan(request, CancellationToken.None));
        Assert.Contains("No products found", exception.Message);
    }

    [Fact]
    public async Task CalculateBatchPlan_MMQMultiplierMode_CalculatesCorrectly()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.5,
            FromDate = DateTime.Now.AddDays(-30),
            ToDate = DateTime.Now
        };

        var semiproduct = CreateSemiproduct("SEMI001", 1000);
        var templates = CreateManufactureTemplates();
        var products = CreateProducts();

        SetupRepositoryMocks(semiproduct, templates, products);

        // Act
        var result = await _service.CalculateBatchPlan(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("SEMI001", result.Semiproduct.ProductCode);
        Assert.Equal(1000, result.TotalVolumeAvailable);
        Assert.Equal(BatchPlanControlMode.MmqMultiplier, result.Summary.UsedControlMode);
        Assert.Equal(1.5, result.Summary.EffectiveMmqMultiplier);
        Assert.Equal(2, result.ProductSizes.Count);
        
        // Check that products are optimized with MMQ multiplier
        var product1 = result.ProductSizes.First(p => p.ProductCode == "PROD001");
        Assert.Equal(15, product1.RecommendedUnitsToProduceHumanReadable); // MMQ (10) * 1.5
        Assert.True(product1.WasOptimized);
    }

    [Fact]
    public async Task CalculateBatchPlan_TotalWeightMode_CalculatesCorrectly()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.TotalWeight,
            TotalWeightToUse = 500,
            FromDate = DateTime.Now.AddDays(-30),
            ToDate = DateTime.Now
        };

        var semiproduct = CreateSemiproduct("SEMI001", 1000);
        var templates = CreateManufactureTemplates();
        var products = CreateProducts();

        SetupRepositoryMocks(semiproduct, templates, products);

        // Act
        var result = await _service.CalculateBatchPlan(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(BatchPlanControlMode.TotalWeight, result.Summary.UsedControlMode);
        Assert.True(result.Summary.TotalVolumeUsed <= 500);
        Assert.Equal(500, result.Summary.ActualTotalWeight);
    }

    [Fact]
    public async Task CalculateBatchPlan_TargetCoverageMode_CalculatesCorrectly()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.TargetDaysCoverage,
            TargetDaysCoverage = 30,
            FromDate = DateTime.Now.AddDays(-30),
            ToDate = DateTime.Now
        };

        var semiproduct = CreateSemiproduct("SEMI001", 1000);
        var templates = CreateManufactureTemplates();
        var products = CreateProducts();

        SetupRepositoryMocks(semiproduct, templates, products);

        // Act
        var result = await _service.CalculateBatchPlan(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(BatchPlanControlMode.TargetDaysCoverage, result.Summary.UsedControlMode);
        Assert.Equal(30, result.TargetDaysCoverage);
    }

    [Fact]
    public async Task CalculateBatchPlan_WithFixedProducts_RespectConstraints()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            ProductConstraints = new List<ProductSizeConstraint>
            {
                new ProductSizeConstraint { ProductCode = "PROD001", IsFixed = true, FixedQuantity = 100 }
            }
        };

        var semiproduct = CreateSemiproduct("SEMI001", 1000);
        var templates = CreateManufactureTemplates();
        var products = CreateProducts();

        SetupRepositoryMocks(semiproduct, templates, products);

        // Act
        var result = await _service.CalculateBatchPlan(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Summary.FixedProductsCount);
        Assert.Equal(1, result.Summary.OptimizedProductsCount);
        
        var fixedProduct = result.ProductSizes.First(p => p.ProductCode == "PROD001");
        Assert.True(fixedProduct.IsFixed);
        Assert.Equal(100, fixedProduct.RecommendedUnitsToProduceHumanReadable);
        Assert.False(fixedProduct.WasOptimized);
    }

    private CatalogAggregate CreateSemiproduct(string code, double stock)
    {
        return new CatalogAggregate
        {
            ProductCode = code,
            ProductName = $"Semiproduct {code}",
            Stock = new StockData { Erp = (decimal)stock }
        };
    }

    private List<ManufactureTemplate> CreateManufactureTemplates()
    {
        return new List<ManufactureTemplate>
        {
            new ManufactureTemplate
            {
                ProductCode = "PROD001",
                ProductName = "Product 1",
                Amount = 100.0,
                Ingredients = new List<Ingredient>
                {
                    new Ingredient { ProductCode = "SEMI001", ProductName = "Semiproduct", Amount = 10.0 }
                }
            },
            new ManufactureTemplate
            {
                ProductCode = "PROD002", 
                ProductName = "Product 2",
                Amount = 50.0,
                Ingredients = new List<Ingredient>
                {
                    new Ingredient { ProductCode = "SEMI001", ProductName = "Semiproduct", Amount = 5.0 }
                }
            }
        };
    }

    private List<CatalogAggregate> CreateProducts()
    {
        return new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PROD001",
                ProductName = "Product 1",
                Stock = new StockData { Erp = 50 },
                MinimalManufactureQuantity = 10,
                SalesHistory = CreateSalesHistory(2) // 2 units per day
            },
            new CatalogAggregate
            {
                ProductCode = "PROD002",
                ProductName = "Product 2", 
                Stock = new StockData { Erp = 25 },
                MinimalManufactureQuantity = 20,
                SalesHistory = CreateSalesHistory(1) // 1 unit per day
            }
        };
    }

    private List<Anela.Heblo.Domain.Features.Catalog.Sales.CatalogSaleRecord> CreateSalesHistory(double dailyAmount)
    {
        var sales = new List<Anela.Heblo.Domain.Features.Catalog.Sales.CatalogSaleRecord>();
        var startDate = DateTime.Now.AddDays(-30);
        
        for (int i = 0; i < 30; i++)
        {
            sales.Add(new Anela.Heblo.Domain.Features.Catalog.Sales.CatalogSaleRecord
            {
                Date = startDate.AddDays(i),
                AmountB2B = dailyAmount / 2,
                AmountB2C = dailyAmount / 2
            });
        }
        
        return sales;
    }

    private void SetupRepositoryMocks(CatalogAggregate semiproduct, List<ManufactureTemplate> templates, List<CatalogAggregate> products)
    {
        _catalogRepositoryMock.Setup(x => x.GetByIdAsync("SEMI001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(semiproduct);

        _manufactureRepositoryMock.Setup(x => x.FindByIngredientAsync("SEMI001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        foreach (var product in products)
        {
            _catalogRepositoryMock.Setup(x => x.GetByIdAsync(product.ProductCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(product);
        }
    }
}