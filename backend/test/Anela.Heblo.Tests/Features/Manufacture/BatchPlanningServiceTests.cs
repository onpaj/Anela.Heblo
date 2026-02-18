using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using Anela.Heblo.Application.Shared;
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
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<IBatchDistributionCalculator> _batchDistributionCalculatorMock;
    private readonly Mock<ILogger<BatchPlanningService>> _loggerMock;
    private readonly BatchPlanningService _service;

    public BatchPlanningServiceTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _manufactureClientMock = new Mock<IManufactureClient>();
        _batchDistributionCalculatorMock = new Mock<IBatchDistributionCalculator>();
        _loggerMock = new Mock<ILogger<BatchPlanningService>>();
        _service = new BatchPlanningService(_catalogRepositoryMock.Object, _manufactureClientMock.Object, _batchDistributionCalculatorMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CalculateBatchPlan_SemiproductNotFound_ThrowsArgumentException()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            ProductCode = "NONEXISTENT",
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
            ProductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0
        };

        var semiproduct = CreateSemiproduct("SEMI001", 1000);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync("SEMI001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(semiproduct);

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("SEMI001", It.IsAny<CancellationToken>()))
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
            ProductCode = "SEMI001",
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
        Assert.Equal(1500, result.TotalVolumeAvailable); // MMQ (1000) * Multiplier (1.5) = 1500
        Assert.Equal(BatchPlanControlMode.MmqMultiplier, result.Summary.UsedControlMode);
        Assert.Equal(1.5, result.Summary.EffectiveMmqMultiplier);
        Assert.Equal(2, result.ProductSizes.Count);

        // Check that products are optimized (quantities will be determined by BatchDistributionCalculator)
        var product1 = result.ProductSizes.First(p => p.ProductCode == "PROD001");
        Assert.True(product1.WasOptimized);
    }

    [Fact]
    public async Task CalculateBatchPlan_TotalWeightMode_CalculatesCorrectly()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            ProductCode = "SEMI001",
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
            ProductCode = "SEMI001",
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
            ProductCode = "SEMI001",
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

    [Fact]
    public async Task CalculateBatchPlan_FixedProductsExceedAvailableVolume_ReturnsErrorWithData()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            ProductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            ProductConstraints = new List<ProductSizeConstraint>
            {
                new ProductSizeConstraint { ProductCode = "PROD001", IsFixed = true, FixedQuantity = 200 }, // Requires 200 * 50 = 10,000g
                new ProductSizeConstraint { ProductCode = "PROD002", IsFixed = true, FixedQuantity = 100 }  // Requires 100 * 25 = 2,500g
                // Total: 12,500g required, but only 1,000g available
            }
        };

        var semiproduct = CreateSemiproductWithNetWeight("SEMI001", 1000); // Only 1000g available
        var templates = CreateManufactureTemplatesWithNetWeight();
        var products = CreateProductsWithNetWeight();

        SetupRepositoryMocks(semiproduct, templates, products);

        // Act
        var result = await _service.CalculateBatchPlan(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success); // Should indicate error
        Assert.Equal(ErrorCodes.FixedProductsExceedAvailableVolume, result.ErrorCode);

        // Should have error parameters
        Assert.NotNull(result.Params);
        Assert.True(result.Params.ContainsKey("volumeUsedByFixed"));
        Assert.True(result.Params.ContainsKey("availableVolume"));
        Assert.True(result.Params.ContainsKey("deficit"));
        Assert.Equal("1000.00", result.Params["availableVolume"]);
        Assert.Equal("12500.00", result.Params["volumeUsedByFixed"]);
        Assert.Equal("11500.00", result.Params["deficit"]);

        // Should still return data for UI display
        Assert.NotNull(result.ProductSizes);
        Assert.Equal(2, result.ProductSizes.Count);
        Assert.NotNull(result.Summary);
        Assert.NotNull(result.Semiproduct);

        // Fixed products should have their fixed quantities set
        var fixedProduct1 = result.ProductSizes.First(p => p.ProductCode == "PROD001");
        Assert.True(fixedProduct1.IsFixed);
        Assert.Equal(200, fixedProduct1.RecommendedUnitsToProduceHumanReadable);
        Assert.Equal(200, fixedProduct1.UserFixedQuantity);
        Assert.Equal(10000, fixedProduct1.TotalVolumeRequired); // 200 * 50g

        var fixedProduct2 = result.ProductSizes.First(p => p.ProductCode == "PROD002");
        Assert.True(fixedProduct2.IsFixed);
        Assert.Equal(100, fixedProduct2.RecommendedUnitsToProduceHumanReadable);
        Assert.Equal(100, fixedProduct2.UserFixedQuantity);
        Assert.Equal(2500, fixedProduct2.TotalVolumeRequired); // 100 * 25g
    }

    [Fact]
    public async Task CalculateBatchPlan_FixedProductsWithinLimits_Succeeds()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            ProductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            ProductConstraints = new List<ProductSizeConstraint>
            {
                new ProductSizeConstraint { ProductCode = "PROD001", IsFixed = true, FixedQuantity = 10 }, // Requires 10 * 50 = 500g
                new ProductSizeConstraint { ProductCode = "PROD002", IsFixed = true, FixedQuantity = 5 }   // Requires 5 * 25 = 125g
                // Total: 625g required, 1000g available, 375g remaining for flexible products
            }
        };

        var semiproduct = CreateSemiproductWithNetWeight("SEMI001", 1000); // 1000g available
        var templates = CreateManufactureTemplatesWithNetWeight();
        var products = CreateProductsWithNetWeight();

        SetupRepositoryMocks(semiproduct, templates, products);

        // Act
        var result = await _service.CalculateBatchPlan(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success); // Should succeed
        Assert.Null(result.ErrorCode); // No error

        // Should have 2 fixed products  
        Assert.Equal(2, result.Summary.FixedProductsCount);
        Assert.Equal(0, result.Summary.OptimizedProductsCount); // No flexible products in this test

        var fixedProduct1 = result.ProductSizes.First(p => p.ProductCode == "PROD001");
        Assert.True(fixedProduct1.IsFixed);
        Assert.Equal(10, fixedProduct1.RecommendedUnitsToProduceHumanReadable);
        Assert.Equal(500, fixedProduct1.TotalVolumeRequired);

        var fixedProduct2 = result.ProductSizes.First(p => p.ProductCode == "PROD002");
        Assert.True(fixedProduct2.IsFixed);
        Assert.Equal(5, fixedProduct2.RecommendedUnitsToProduceHumanReadable);
        Assert.Equal(125, fixedProduct2.TotalVolumeRequired);
    }

    private CatalogAggregate CreateSemiproduct(string code, double stock)
    {
        return new CatalogAggregate
        {
            ProductCode = code,
            ProductName = $"Semiproduct {code}",
            Stock = new StockData { Erp = (decimal)stock },
            MinimalManufactureQuantity = 1000 // Set MMQ to enable volume calculation
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
                NetWeight = 10.0, // Set weight for volume calculations
                MinimalManufactureQuantity = 10,
                SalesHistory = CreateSalesHistory(2) // 2 units per day
            },
            new CatalogAggregate
            {
                ProductCode = "PROD002",
                ProductName = "Product 2",
                Stock = new StockData { Erp = 25 },
                NetWeight = 5.0, // Set weight for volume calculations
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

        _manufactureClientMock.Setup(x => x.FindByIngredientAsync("SEMI001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        foreach (var product in products)
        {
            _catalogRepositoryMock.Setup(x => x.GetByIdAsync(product.ProductCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(product);
        }

        // Setup BatchDistributionCalculator to set reasonable suggested amounts
        _batchDistributionCalculatorMock.Setup(x => x.OptimizeBatch(It.IsAny<ProductBatch>(), It.IsAny<bool>()))
            .Callback<ProductBatch, bool>((batch, minimizeResidue) =>
            {
                // Distribute all available weight among variants proportionally
                var totalWeight = batch.TotalWeight;
                var totalValidVariants = batch.ValidVariants.Count;

                foreach (var variant in batch.Variants)
                {
                    if (totalValidVariants > 0 && variant.Weight > 0)
                    {
                        // Distribute weight evenly among valid variants
                        var targetWeight = totalWeight / totalValidVariants;
                        var suggestedAmount = Math.Max(1, (int)(targetWeight / variant.Weight));
                        variant.SuggestedAmount = suggestedAmount;
                    }
                    else
                    {
                        variant.SuggestedAmount = 1; // Minimum fallback
                    }
                }

                // Adjust to use all available weight if needed
                var actualUsedWeight = batch.Variants.Sum(v => v.SuggestedAmount * v.Weight);
                if (actualUsedWeight < totalWeight * 0.9) // If using less than 90% of available
                {
                    // Increase the first variant to use more weight
                    var firstVariant = batch.Variants.FirstOrDefault();
                    if (firstVariant != null && firstVariant.Weight > 0)
                    {
                        var additionalWeight = totalWeight - actualUsedWeight;
                        var additionalUnits = (int)(additionalWeight / firstVariant.Weight);
                        firstVariant.SuggestedAmount += Math.Max(0, additionalUnits);
                    }
                }
            });
    }

    private CatalogAggregate CreateSemiproductWithNetWeight(string code, double stock)
    {
        return new CatalogAggregate
        {
            ProductCode = code,
            ProductName = $"Semiproduct {code}",
            Stock = new StockData { Erp = (decimal)stock },
            MinimalManufactureQuantity = 1000
        };
    }

    private List<ManufactureTemplate> CreateManufactureTemplatesWithNetWeight()
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
                    new Ingredient { ProductCode = "SEMI001", ProductName = "Semiproduct", Amount = 50.0 } // 50g per unit
                }
            },
            new ManufactureTemplate
            {
                ProductCode = "PROD002",
                ProductName = "Product 2",
                Amount = 50.0,
                Ingredients = new List<Ingredient>
                {
                    new Ingredient { ProductCode = "SEMI001", ProductName = "Semiproduct", Amount = 25.0 } // 25g per unit
                }
            }
        };
    }

    private List<CatalogAggregate> CreateProductsWithNetWeight()
    {
        return new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PROD001",
                ProductName = "Product 1",
                Stock = new StockData { Erp = 50 },
                NetWeight = 50.0, // 50g per unit
                MinimalManufactureQuantity = 10,
                SalesHistory = CreateSalesHistory(2) // 2 units per day
            },
            new CatalogAggregate
            {
                ProductCode = "PROD002",
                ProductName = "Product 2",
                Stock = new StockData { Erp = 25 },
                NetWeight = 25.0, // 25g per unit  
                MinimalManufactureQuantity = 20,
                SalesHistory = CreateSalesHistory(1) // 1 unit per day
            }
        };
    }
}