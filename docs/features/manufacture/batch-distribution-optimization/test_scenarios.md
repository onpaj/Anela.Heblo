# Batch Distribution Optimization - Test Scenarios

## Unit Test Scenarios

### Domain Model Tests

#### ProductBatch Value Object Tests

```csharp
[TestFixture]
public class ProductBatchTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateSuccessfully()
    {
        // Arrange
        var productCode = "SOAP-001";
        var productName = "Liquid Soap";
        var totalWeight = 1000.0;
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("SOAP-001-250", "Soap 250ml", 250, 0.3, 10, 50),
            ProductVariant.Create("SOAP-001-500", "Soap 500ml", 500, 0.6, 15, 30)
        };

        // Act
        var batch = ProductBatch.Create(productCode, productName, totalWeight, variants);

        // Assert
        Assert.That(batch.ProductCode, Is.EqualTo(productCode));
        Assert.That(batch.ProductName, Is.EqualTo(productName));
        Assert.That(batch.TotalWeight, Is.EqualTo(totalWeight));
        Assert.That(batch.Variants.Count, Is.EqualTo(2));
        Assert.That(batch.ValidVariants.Count, Is.EqualTo(2));
    }

    [Test]
    public void Create_WithEmptyProductCode_ShouldThrowBusinessException()
    {
        // Arrange
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("SOAP-001-250", "Soap 250ml", 250, 0.3, 10, 50)
        };

        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            ProductBatch.Create("", "Soap", 1000.0, variants));
    }

    [Test]
    public void Create_WithZeroWeight_ShouldThrowBusinessException()
    {
        // Arrange
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("SOAP-001-250", "Soap 250ml", 250, 0.3, 10, 50)
        };

        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            ProductBatch.Create("SOAP-001", "Soap", 0, variants));
    }

    [Test]
    public void Create_WithEmptyVariants_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            ProductBatch.Create("SOAP-001", "Soap", 1000.0, new List<ProductVariant>()));
    }

    [Test]
    public void ValidVariants_ShouldFilterVariantsWithPositiveSalesAndWeight()
    {
        // Arrange
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("VALID-1", "Valid 1", 250, 0.3, 10, 50),    // Valid
            ProductVariant.Create("INVALID-1", "Invalid 1", 250, 0.3, 0, 50), // Zero sales
            ProductVariant.Create("INVALID-2", "Invalid 2", 250, 0, 10, 50),  // Zero weight
            ProductVariant.Create("VALID-2", "Valid 2", 500, 0.6, 15, 30)     // Valid
        };

        var batch = ProductBatch.Create("TEST-001", "Test", 1000.0, variants);

        // Act
        var validVariants = batch.ValidVariants;

        // Assert
        Assert.That(validVariants.Count, Is.EqualTo(2));
        Assert.That(validVariants.All(v => v.DailySales > 0 && v.Weight > 0), Is.True);
    }

    [Test]
    public void GetTotalProducedWeight_ShouldSumAllVariantWeights()
    {
        // Arrange
        var batch = CreateTestBatch();
        batch.Variants[0].SuggestedAmount = 100; // 100 * 0.3 = 30kg
        batch.Variants[1].SuggestedAmount = 50;  // 50 * 0.6 = 30kg

        // Act
        var totalWeight = batch.GetTotalProducedWeight();

        // Assert
        Assert.That(totalWeight, Is.EqualTo(60.0));
    }

    [Test]
    public void GetRemainingCapacity_ShouldCalculateCorrectly()
    {
        // Arrange
        var batch = CreateTestBatch();
        batch.Variants[0].SuggestedAmount = 100; // 30kg
        batch.Variants[1].SuggestedAmount = 50;  // 30kg
        // Total: 60kg, Remaining: 1000 - 60 = 940kg

        // Act
        var remaining = batch.GetRemainingCapacity();

        // Assert
        Assert.That(remaining, Is.EqualTo(940.0));
    }

    [Test]
    public void IsWithinCapacity_WhenUnderLimit_ShouldReturnTrue()
    {
        // Arrange
        var batch = CreateTestBatch();
        batch.Variants[0].SuggestedAmount = 100; // 30kg
        batch.Variants[1].SuggestedAmount = 50;  // 30kg (Total: 60kg < 1000kg)

        // Act & Assert
        Assert.That(batch.IsWithinCapacity(), Is.True);
    }

    [Test]
    public void IsWithinCapacity_WhenOverLimit_ShouldReturnFalse()
    {
        // Arrange
        var batch = CreateTestBatch();
        batch.Variants[0].SuggestedAmount = 2000; // 600kg
        batch.Variants[1].SuggestedAmount = 1000; // 600kg (Total: 1200kg > 1000kg)

        // Act & Assert
        Assert.That(batch.IsWithinCapacity(), Is.False);
    }

    private ProductBatch CreateTestBatch()
    {
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("SOAP-001-250", "Soap 250ml", 250, 0.3, 10, 50),
            ProductVariant.Create("SOAP-001-500", "Soap 500ml", 500, 0.6, 15, 30)
        };
        return ProductBatch.Create("SOAP-001", "Liquid Soap", 1000.0, variants);
    }
}
```

#### ProductVariant Value Object Tests

```csharp
[TestFixture]
public class ProductVariantTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateSuccessfully()
    {
        // Arrange
        var productCode = "SOAP-001-250";
        var productName = "Soap 250ml";
        var volume = 250.0;
        var weight = 0.3;
        var dailySales = 10.0;
        var currentStock = 50.0;

        // Act
        var variant = ProductVariant.Create(productCode, productName, volume, weight, dailySales, currentStock);

        // Assert
        Assert.That(variant.ProductCode, Is.EqualTo(productCode));
        Assert.That(variant.ProductName, Is.EqualTo(productName));
        Assert.That(variant.Volume, Is.EqualTo(volume));
        Assert.That(variant.Weight, Is.EqualTo(weight));
        Assert.That(variant.DailySales, Is.EqualTo(dailySales));
        Assert.That(variant.CurrentStock, Is.EqualTo(currentStock));
        Assert.That(variant.SuggestedAmount, Is.EqualTo(0));
    }

    [Test]
    public void Create_WithEmptyProductCode_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            ProductVariant.Create("", "Soap", 250, 0.3, 10, 50));
    }

    [Test]
    public void Create_WithZeroWeight_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            ProductVariant.Create("SOAP-001", "Soap", 250, 0, 10, 50));
    }

    [Test]
    public void Create_WithNegativeDailySales_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            ProductVariant.Create("SOAP-001", "Soap", 250, 0.3, -10, 50));
    }

    [Test]
    public void Create_WithNegativeCurrentStock_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            ProductVariant.Create("SOAP-001", "Soap", 250, 0.3, 10, -50));
    }

    [Test]
    public void UpstockCalculations_ShouldCalculateCorrectly()
    {
        // Arrange
        var variant = ProductVariant.Create("SOAP-001", "Soap", 250, 0.3, 10, 50);
        variant.SuggestedAmount = 100;

        // Act & Assert
        Assert.That(variant.UpstockSuggested, Is.EqualTo(10.0)); // 100 / 10
        Assert.That(variant.UpstockTotal, Is.EqualTo(15.0));     // (100 + 50) / 10
        Assert.That(variant.UpstockCurrent, Is.EqualTo(5.0));    // 50 / 10
    }

    [Test]
    public void UpstockCalculations_WithZeroDailySales_ShouldReturnZero()
    {
        // Arrange
        var variant = ProductVariant.Create("SOAP-001", "Soap", 250, 0.3, 0, 50);
        variant.SuggestedAmount = 100;

        // Act & Assert
        Assert.That(variant.UpstockSuggested, Is.EqualTo(0));
        Assert.That(variant.UpstockTotal, Is.EqualTo(0));
        Assert.That(variant.UpstockCurrent, Is.EqualTo(0));
    }

    [Test]
    public void GetRequiredQuantityForDays_ShouldCalculateCorrectly()
    {
        // Arrange
        var variant = ProductVariant.Create("SOAP-001", "Soap", 250, 0.3, 10, 50);

        // Act
        var required7Days = variant.GetRequiredQuantityForDays(7);   // 70 - 50 = 20
        var required3Days = variant.GetRequiredQuantityForDays(3);   // 30 - 50 = 0 (max with 0)

        // Assert
        Assert.That(required7Days, Is.EqualTo(20));
        Assert.That(required3Days, Is.EqualTo(0));
    }

    [Test]
    public void GetTotalStockAfterProduction_ShouldSumCurrentAndSuggested()
    {
        // Arrange
        var variant = ProductVariant.Create("SOAP-001", "Soap", 250, 0.3, 10, 50);
        variant.SuggestedAmount = 100;

        // Act
        var total = variant.GetTotalStockAfterProduction();

        // Assert
        Assert.That(total, Is.EqualTo(150)); // 50 + 100
    }

    [Test]
    public void GetDaysCoverageAfterProduction_ShouldCalculateCorrectly()
    {
        // Arrange
        var variant = ProductVariant.Create("SOAP-001", "Soap", 250, 0.3, 10, 50);
        variant.SuggestedAmount = 100;

        // Act
        var coverage = variant.GetDaysCoverageAfterProduction();

        // Assert
        Assert.That(coverage, Is.EqualTo(15.0)); // 150 / 10
    }
}
```

### Algorithm Tests

#### BatchDistributionCalculator Tests

```csharp
[TestFixture]
public class BatchDistributionCalculatorTests
{
    private BatchDistributionCalculator _calculator;
    private Mock<ILogger<BatchDistributionCalculator>> _mockLogger;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<BatchDistributionCalculator>>();
        _calculator = new BatchDistributionCalculator(_mockLogger.Object);
    }

    [Test]
    public void OptimizeBatch_WithValidInput_ShouldOptimizeCorrectly()
    {
        // Arrange
        var batch = CreateTestBatch();

        // Act
        _calculator.OptimizeBatch(batch, minimizeResidue: true);

        // Assert
        Assert.That(batch.Variants.All(v => v.SuggestedAmount >= 0), Is.True);
        Assert.That(batch.IsWithinCapacity(), Is.True);
        
        var totalWeight = batch.GetTotalProducedWeight();
        Assert.That(totalWeight, Is.LessThanOrEqualTo(batch.TotalWeight));
        Assert.That(totalWeight, Is.GreaterThan(0));
    }

    [Test]
    public void OptimizeBatch_WithNoValidVariants_ShouldHandleGracefully()
    {
        // Arrange
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("INVALID-1", "Invalid 1", 250, 0.3, 0, 50), // Zero sales
            ProductVariant.Create("INVALID-2", "Invalid 2", 250, 0, 10, 50)   // Zero weight
        };
        var batch = ProductBatch.Create("TEST-001", "Test", 1000.0, variants);

        // Act
        _calculator.OptimizeBatch(batch);

        // Assert
        Assert.That(batch.Variants.All(v => v.SuggestedAmount == 0), Is.True);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("No valid variants")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Test]
    public void OptimizeBatch_ShouldMaximizeDaysCoverage()
    {
        // Arrange
        var batch = CreateTestBatch();

        // Act
        _calculator.OptimizeBatch(batch);

        // Assert
        // Verify that production is optimized for maximum days coverage
        var averageDaysCoverage = batch.ValidVariants
            .Where(v => v.DailySales > 0)
            .Average(v => v.GetDaysCoverageAfterProduction());

        Assert.That(averageDaysCoverage, Is.GreaterThan(0));
        
        // Verify weight constraint is respected
        Assert.That(batch.GetTotalProducedWeight(), Is.LessThanOrEqualTo(batch.TotalWeight));
    }

    [Test]
    public void OptimizeBatch2_WithValidInput_ShouldOptimizeForUtilization()
    {
        // Arrange
        var batch = CreateTestBatch();

        // Act
        _calculator.OptimizeBatch2(batch);

        // Assert
        Assert.That(batch.IsWithinCapacity(), Is.True);
        
        // This algorithm should achieve high weight utilization
        var utilizationPercentage = (batch.GetTotalProducedWeight() / batch.TotalWeight) * 100;
        Assert.That(utilizationPercentage, Is.GreaterThan(50)); // Should use significant portion
    }

    [Test]
    public void OptimizeBatch_WithMinimizeResidue_ShouldDistributeRemainingWeight()
    {
        // Arrange
        var batch = CreateLowDemandBatch(); // Batch where residue distribution matters

        // Act
        _calculator.OptimizeBatch(batch, minimizeResidue: true);

        // Assert
        var remainingWeight = batch.GetRemainingCapacity();
        var utilizationPercentage = (batch.GetTotalProducedWeight() / batch.TotalWeight) * 100;
        
        // Should achieve better utilization with residue minimization
        Assert.That(utilizationPercentage, Is.GreaterThan(80));
        
        // Remaining weight should be small relative to smallest variant weight
        var smallestVariantWeight = batch.ValidVariants.Min(v => v.Weight);
        Assert.That(remainingWeight, Is.LessThan(smallestVariantWeight * 2));
    }

    [Test]
    public void OptimizeBatch_WithoutMinimizeResidue_ShouldNotDistributeRemainingWeight()
    {
        // Arrange
        var batch = CreateLowDemandBatch();

        // Act
        _calculator.OptimizeBatch(batch, minimizeResidue: false);

        // Assert
        // Without residue minimization, should have more remaining capacity
        var remainingWeight = batch.GetRemainingCapacity();
        Assert.That(remainingWeight, Is.GreaterThan(0));
        
        // Production should be based purely on optimal days calculation
        foreach (var variant in batch.ValidVariants)
        {
            var expectedDays = CalculateExpectedDays(batch.ValidVariants, batch.TotalWeight);
            var expectedQuantity = Math.Max(expectedDays * variant.DailySales - variant.CurrentStock, 0);
            var actualQuantity = variant.SuggestedAmount;
            
            // Should be close to expected quantity (allowing for floor operations)
            Assert.That(actualQuantity, Is.EqualTo(Math.Floor(expectedQuantity)).Within(1));
        }
    }

    [Test]
    public void BothAlgorithms_ShouldProduceFeasibleSolutions()
    {
        // Arrange
        var batch1 = CreateTestBatch();
        var batch2 = CreateTestBatch();

        // Act
        _calculator.OptimizeBatch(batch1);
        _calculator.OptimizeBatch2(batch2);

        // Assert
        // Both should respect weight constraints
        Assert.That(batch1.IsWithinCapacity(), Is.True);
        Assert.That(batch2.IsWithinCapacity(), Is.True);

        // Both should produce positive quantities for valid variants
        Assert.That(batch1.ValidVariants.Sum(v => v.SuggestedAmount), Is.GreaterThan(0));
        Assert.That(batch2.ValidVariants.Sum(v => v.SuggestedAmount), Is.GreaterThan(0));
    }

    [Test]
    public void OptimizeBatch_WithLargeCapacity_ShouldHandleCorrectly()
    {
        // Arrange
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("SOAP-001-250", "Soap 250ml", 250, 0.3, 10, 0),
            ProductVariant.Create("SOAP-001-500", "Soap 500ml", 500, 0.6, 5, 0)
        };
        var batch = ProductBatch.Create("SOAP-001", "Soap", 10000.0, variants); // Very large capacity

        // Act
        _calculator.OptimizeBatch(batch);

        // Assert
        // Should optimize for very long production coverage
        var daysCoverage = batch.ValidVariants
            .Where(v => v.DailySales > 0)
            .Average(v => v.GetDaysCoverageAfterProduction());
        
        Assert.That(daysCoverage, Is.GreaterThan(100)); // Should cover many days
        Assert.That(batch.IsWithinCapacity(), Is.True);
    }

    [Test]
    public void OptimizeBatch_WithTightConstraints_ShouldFindOptimalSolution()
    {
        // Arrange - Create scenario where weight is very limiting
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("HEAVY-1", "Heavy Product 1", 1000, 5.0, 20, 0), // Heavy, high demand
            ProductVariant.Create("LIGHT-1", "Light Product 1", 250, 0.5, 10, 0)  // Light, medium demand
        };
        var batch = ProductBatch.Create("MIXED-001", "Mixed", 100.0, variants); // Tight capacity

        // Act
        _calculator.OptimizeBatch(batch);

        // Assert
        Assert.That(batch.IsWithinCapacity(), Is.True);
        
        // Should prioritize light products to maximize quantity
        var heavyVariant = batch.Variants.First(v => v.ProductCode == "HEAVY-1");
        var lightVariant = batch.Variants.First(v => v.ProductCode == "LIGHT-1");
        
        // Light variant should get significant allocation due to weight efficiency
        Assert.That(lightVariant.SuggestedAmount, Is.GreaterThan(0));
        
        // Total weight should be used efficiently
        var utilizationPercentage = (batch.GetTotalProducedWeight() / batch.TotalWeight) * 100;
        Assert.That(utilizationPercentage, Is.GreaterThan(90));
    }

    private ProductBatch CreateTestBatch()
    {
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("SOAP-001-250", "Soap 250ml", 250, 0.3, 10, 50),
            ProductVariant.Create("SOAP-001-500", "Soap 500ml", 500, 0.6, 15, 30),
            ProductVariant.Create("SOAP-001-1000", "Soap 1000ml", 1000, 1.2, 8, 20)
        };
        return ProductBatch.Create("SOAP-001", "Liquid Soap", 1000.0, variants);
    }

    private ProductBatch CreateLowDemandBatch()
    {
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("SOAP-001-250", "Soap 250ml", 250, 0.3, 2, 10),  // Low demand
            ProductVariant.Create("SOAP-001-500", "Soap 500ml", 500, 0.6, 3, 5),   // Low demand
        };
        return ProductBatch.Create("SOAP-001", "Liquid Soap", 1000.0, variants);
    }

    private double CalculateExpectedDays(List<ProductVariant> variants, double totalWeight)
    {
        // Simplified binary search simulation for testing
        double low = 0, high = 1000;
        
        while (high - low > 0.1)
        {
            double mid = (low + high) / 2;
            double requiredWeight = 0;
            
            foreach (var v in variants)
            {
                double needed = Math.Max(mid * v.DailySales - v.CurrentStock, 0);
                requiredWeight += Math.Ceiling(needed) * v.Weight;
            }
            
            if (requiredWeight <= totalWeight)
                low = mid;
            else
                high = mid;
        }
        
        return low;
    }
}
```

### Application Service Tests

#### ManufactureAppService Batch Optimization Tests

```csharp
[TestFixture]
public class ManufactureAppServiceBatchOptimizationTests
{
    private Mock<IBatchDistributionCalculator> _mockCalculator;
    private Mock<ICatalogRepository> _mockCatalogRepository;
    private Mock<ILogger<ManufactureAppService>> _mockLogger;
    private ManufactureAppService _service;

    [SetUp]
    public void SetUp()
    {
        _mockCalculator = new Mock<IBatchDistributionCalculator>();
        _mockCatalogRepository = new Mock<ICatalogRepository>();
        _mockLogger = new Mock<ILogger<ManufactureAppService>>();

        _service = new ManufactureAppService(
            null, // manufacture repository
            _mockCatalogRepository.Object,
            _mockCalculator.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task GetBatchDistributionAsync_WithValidRequest_ShouldReturnOptimizedResult()
    {
        // Arrange
        var request = new BatchDistributionRequestDto
        {
            ProductCode = "SOAP-001",
            ProductName = "Liquid Soap",
            TotalWeight = 1000.0,
            Strategy = OptimizationStrategy.MaximizeDays,
            MinimizeResidue = true,
            Variants = new List<ProductVariantDto>
            {
                new ProductVariantDto
                {
                    ProductCode = "SOAP-001-250",
                    ProductName = "Soap 250ml",
                    Volume = 250,
                    Weight = 0.3,
                    DailySales = 10,
                    CurrentStock = 50
                },
                new ProductVariantDto
                {
                    ProductCode = "SOAP-001-500",
                    ProductName = "Soap 500ml",
                    Volume = 500,
                    Weight = 0.6,
                    DailySales = 15,
                    CurrentStock = 30
                }
            }
        };

        _mockCalculator
            .Setup(x => x.OptimizeBatch(It.IsAny<ProductBatch>(), true))
            .Callback<ProductBatch, bool>((batch, minimize) =>
            {
                // Simulate optimization results
                batch.Variants[0].SuggestedAmount = 100;
                batch.Variants[1].SuggestedAmount = 50;
            });

        // Act
        var result = await _service.GetBatchDistributionAsync(request);

        // Assert
        Assert.That(result.ProductCode, Is.EqualTo("SOAP-001"));
        Assert.That(result.ProductName, Is.EqualTo("Liquid Soap"));
        Assert.That(result.TotalWeight, Is.EqualTo(1000.0));
        Assert.That(result.UsedWeight, Is.EqualTo(60.0)); // 100*0.3 + 50*0.6
        Assert.That(result.RemainingWeight, Is.EqualTo(940.0)); // 1000 - 60
        Assert.That(result.UtilizationPercentage, Is.EqualTo(6.0)); // 60/1000 * 100
        Assert.That(result.Variants.Count, Is.EqualTo(2));

        _mockCalculator.Verify(x => x.OptimizeBatch(It.IsAny<ProductBatch>(), true), Times.Once);
    }

    [Test]
    public async Task GetBatchDistributionAsync_WithMaximizeUtilizationStrategy_ShouldUseCorrectAlgorithm()
    {
        // Arrange
        var request = CreateTestRequest();
        request.Strategy = OptimizationStrategy.MaximizeUtilization;

        _mockCalculator
            .Setup(x => x.OptimizeBatch2(It.IsAny<ProductBatch>()))
            .Callback<ProductBatch>(batch =>
            {
                batch.Variants[0].SuggestedAmount = 200;
                batch.Variants[1].SuggestedAmount = 100;
            });

        // Act
        var result = await _service.GetBatchDistributionAsync(request);

        // Assert
        _mockCalculator.Verify(x => x.OptimizeBatch2(It.IsAny<ProductBatch>()), Times.Once);
        _mockCalculator.Verify(x => x.OptimizeBatch(It.IsAny<ProductBatch>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task GetBatchDistributionAsync_WithBalancedStrategy_ShouldUseBothAlgorithms()
    {
        // Arrange
        var request = CreateTestRequest();
        request.Strategy = OptimizationStrategy.BalancedApproach;

        var callCount = 0;
        _mockCalculator
            .Setup(x => x.OptimizeBatch(It.IsAny<ProductBatch>(), It.IsAny<bool>()))
            .Callback<ProductBatch, bool>((batch, minimize) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call - primary algorithm
                    batch.Variants[0].SuggestedAmount = 80;
                    batch.Variants[1].SuggestedAmount = 40;
                }
            });

        _mockCalculator
            .Setup(x => x.OptimizeBatch2(It.IsAny<ProductBatch>()))
            .Callback<ProductBatch>(batch =>
            {
                // Alternative algorithm
                batch.Variants[0].SuggestedAmount = 120;
                batch.Variants[1].SuggestedAmount = 60;
            });

        // Act
        var result = await _service.GetBatchDistributionAsync(request);

        // Assert
        _mockCalculator.Verify(x => x.OptimizeBatch(It.IsAny<ProductBatch>(), true), Times.Once);
        _mockCalculator.Verify(x => x.OptimizeBatch2(It.IsAny<ProductBatch>()), Times.Once);
    }

    [Test]
    public void GetBatchDistributionAsync_WithInvalidWeight_ShouldThrowException()
    {
        // Arrange
        var request = CreateTestRequest();
        request.TotalWeight = 0;

        // Act & Assert
        Assert.ThrowsAsync<UserFriendlyException>(() => _service.GetBatchDistributionAsync(request));
    }

    [Test]
    public void GetBatchDistributionAsync_WithNoVariants_ShouldThrowException()
    {
        // Arrange
        var request = CreateTestRequest();
        request.Variants.Clear();

        // Act & Assert
        Assert.ThrowsAsync<UserFriendlyException>(() => _service.GetBatchDistributionAsync(request));
    }

    [Test]
    public async Task GetBatchDistributionAsync_WithOptimizationFailure_ShouldThrowUserFriendlyException()
    {
        // Arrange
        var request = CreateTestRequest();

        _mockCalculator
            .Setup(x => x.OptimizeBatch(It.IsAny<ProductBatch>(), It.IsAny<bool>()))
            .Throws(new InvalidOperationException("Optimization failed"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UserFriendlyException>(() => _service.GetBatchDistributionAsync(request));
        Assert.That(ex.Message, Does.Contain("Optimization failed"));

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to optimize batch")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Test]
    public async Task GetBatchDistributionAsync_ShouldCalculateCorrectMetrics()
    {
        // Arrange
        var request = CreateTestRequest();

        _mockCalculator
            .Setup(x => x.OptimizeBatch(It.IsAny<ProductBatch>(), It.IsAny<bool>()))
            .Callback<ProductBatch, bool>((batch, minimize) =>
            {
                batch.Variants[0].SuggestedAmount = 100; // 30kg
                batch.Variants[1].SuggestedAmount = 50;  // 30kg
            });

        // Act
        var result = await _service.GetBatchDistributionAsync(request);

        // Assert
        Assert.That(result.UsedWeight, Is.EqualTo(60.0));
        Assert.That(result.RemainingWeight, Is.EqualTo(940.0));
        Assert.That(result.UtilizationPercentage, Is.EqualTo(6.0));
        
        Assert.That(result.Metrics, Is.Not.Null);
        Assert.That(result.Metrics.TotalDailyConsumption, Is.GreaterThan(0));
        Assert.That(result.Metrics.WastePercentage, Is.EqualTo(94.0)); // 940/1000 * 100
        
        // Verify variant DTOs have correct calculated values
        var variant1 = result.Variants.First(v => v.ProductCode == "SOAP-001-250");
        Assert.That(variant1.SuggestedAmount, Is.EqualTo(100));
        Assert.That(variant1.TotalWeightProduced, Is.EqualTo(30.0)); // 100 * 0.3
        Assert.That(variant1.DaysCoverageAfterProduction, Is.EqualTo(15.0)); // (100 + 50) / 10
    }

    private BatchDistributionRequestDto CreateTestRequest()
    {
        return new BatchDistributionRequestDto
        {
            ProductCode = "SOAP-001",
            ProductName = "Liquid Soap",
            TotalWeight = 1000.0,
            Strategy = OptimizationStrategy.MaximizeDays,
            MinimizeResidue = true,
            Variants = new List<ProductVariantDto>
            {
                new ProductVariantDto
                {
                    ProductCode = "SOAP-001-250",
                    ProductName = "Soap 250ml",
                    Volume = 250,
                    Weight = 0.3,
                    DailySales = 10,
                    CurrentStock = 50
                },
                new ProductVariantDto
                {
                    ProductCode = "SOAP-001-500",
                    ProductName = "Soap 500ml",
                    Volume = 500,
                    Weight = 0.6,
                    DailySales = 15,
                    CurrentStock = 30
                }
            }
        };
    }
}
```

## Integration Test Scenarios

### Algorithm Performance Tests

```csharp
[TestFixture]
public class BatchOptimizationPerformanceTests
{
    private BatchDistributionCalculator _calculator;

    [SetUp]
    public void SetUp()
    {
        var logger = new NullLogger<BatchDistributionCalculator>();
        _calculator = new BatchDistributionCalculator(logger);
    }

    [Test]
    public void OptimizeBatch_WithLargeNumberOfVariants_ShouldCompleteQuickly()
    {
        // Arrange
        var variants = GenerateVariants(50); // 50 variants
        var batch = ProductBatch.Create("LARGE-BATCH", "Large Batch", 10000.0, variants);
        var stopwatch = Stopwatch.StartNew();

        // Act
        _calculator.OptimizeBatch(batch);

        // Assert
        stopwatch.Stop();
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(2000)); // Should complete within 2 seconds
        Assert.That(batch.IsWithinCapacity(), Is.True);
    }

    [Test]
    public void OptimizeBatch_WithComplexConstraints_ShouldFindValidSolution()
    {
        // Arrange - Mixed high/low demand, various weights
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("HIGH-DEMAND", "High Demand", 100, 0.1, 100, 0),    // High demand, light
            ProductVariant.Create("LOW-DEMAND", "Low Demand", 1000, 2.0, 2, 0),       // Low demand, heavy
            ProductVariant.Create("MEDIUM-1", "Medium 1", 500, 0.5, 20, 10),          // Medium demand
            ProductVariant.Create("MEDIUM-2", "Medium 2", 750, 0.8, 25, 15),          // Medium demand
            ProductVariant.Create("SPECIAL", "Special", 250, 0.3, 5, 100)             // Low demand, high stock
        };
        var batch = ProductBatch.Create("COMPLEX", "Complex Batch", 1000.0, variants);

        // Act
        _calculator.OptimizeBatch(batch);

        // Assert
        Assert.That(batch.IsWithinCapacity(), Is.True);
        
        // High demand variants should get priority
        var highDemandVariant = batch.Variants.First(v => v.ProductCode == "HIGH-DEMAND");
        var lowDemandVariant = batch.Variants.First(v => v.ProductCode == "LOW-DEMAND");
        
        Assert.That(highDemandVariant.SuggestedAmount, Is.GreaterThan(lowDemandVariant.SuggestedAmount));
        
        // Total production should use significant portion of capacity
        var utilizationPercentage = (batch.GetTotalProducedWeight() / batch.TotalWeight) * 100;
        Assert.That(utilizationPercentage, Is.GreaterThan(70));
    }

    [Test]
    public void BothAlgorithms_WithSameInput_ShouldProduceDifferentButValidResults()
    {
        // Arrange
        var batch1 = CreateComplexBatch();
        var batch2 = CreateComplexBatch();

        // Act
        _calculator.OptimizeBatch(batch1);
        _calculator.OptimizeBatch2(batch2);

        // Assert
        Assert.That(batch1.IsWithinCapacity(), Is.True);
        Assert.That(batch2.IsWithinCapacity(), Is.True);

        // Results might be different but both should be valid
        var utilization1 = (batch1.GetTotalProducedWeight() / batch1.TotalWeight) * 100;
        var utilization2 = (batch2.GetTotalProducedWeight() / batch2.TotalWeight) * 100;

        Assert.That(utilization1, Is.GreaterThan(0));
        Assert.That(utilization2, Is.GreaterThan(0));

        // At least one should achieve reasonable utilization
        Assert.That(Math.Max(utilization1, utilization2), Is.GreaterThan(50));
    }

    private List<ProductVariant> GenerateVariants(int count)
    {
        var variants = new List<ProductVariant>();
        var random = new Random(42); // Fixed seed for reproducibility

        for (int i = 1; i <= count; i++)
        {
            var weight = 0.1 + (random.NextDouble() * 2.0); // 0.1 - 2.1 kg
            var dailySales = 1 + (random.NextDouble() * 50); // 1 - 51 units/day
            var currentStock = random.NextDouble() * 100; // 0 - 100 units

            variants.Add(ProductVariant.Create(
                $"VARIANT-{i:000}",
                $"Variant {i}",
                250 + (i * 10), // Volume
                weight,
                dailySales,
                currentStock));
        }

        return variants;
    }

    private ProductBatch CreateComplexBatch()
    {
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("VAR-001", "Variant 1", 100, 0.2, 50, 20),
            ProductVariant.Create("VAR-002", "Variant 2", 250, 0.4, 30, 40),
            ProductVariant.Create("VAR-003", "Variant 3", 500, 0.8, 20, 10),
            ProductVariant.Create("VAR-004", "Variant 4", 1000, 1.5, 15, 5),
            ProductVariant.Create("VAR-005", "Variant 5", 200, 0.3, 40, 60)
        };
        return ProductBatch.Create("COMPLEX", "Complex Batch", 2000.0, variants);
    }
}
```

### Data Validation Tests

```csharp
[TestFixture]
public class BatchOptimizationValidationTests
{
    [Test]
    public void OptimizeBatch_ResultValidation_ShouldMeetAllConstraints()
    {
        // Arrange
        var calculator = new BatchDistributionCalculator(new NullLogger<BatchDistributionCalculator>());
        var batch = CreateValidationTestBatch();

        // Act
        calculator.OptimizeBatch(batch);

        // Assert - Comprehensive validation
        ValidateBatchConstraints(batch);
        ValidateVariantConstraints(batch);
        ValidateBusinessRules(batch);
    }

    private void ValidateBatchConstraints(ProductBatch batch)
    {
        // Weight constraint
        var totalProducedWeight = batch.GetTotalProducedWeight();
        Assert.That(totalProducedWeight, Is.LessThanOrEqualTo(batch.TotalWeight),
            "Total produced weight exceeds batch capacity");

        // Non-negative production
        Assert.That(batch.Variants.All(v => v.SuggestedAmount >= 0),
            "All suggested amounts must be non-negative");

        // Meaningful production (unless no demand)
        var hasValidDemand = batch.ValidVariants.Any();
        if (hasValidDemand)
        {
            Assert.That(totalProducedWeight, Is.GreaterThan(0),
                "Should produce something when there is valid demand");
        }
    }

    private void ValidateVariantConstraints(ProductBatch batch)
    {
        foreach (var variant in batch.Variants)
        {
            // Individual weight constraint (no single variant should exceed total capacity)
            var variantWeight = variant.SuggestedAmount * variant.Weight;
            Assert.That(variantWeight, Is.LessThanOrEqualTo(batch.TotalWeight),
                $"Variant {variant.ProductCode} weight exceeds batch capacity");

            // Reasonable production quantities
            if (variant.DailySales > 0)
            {
                var daysCoverage = variant.GetDaysCoverageAfterProduction();
                Assert.That(daysCoverage, Is.LessThan(1000), // Shouldn't produce for more than ~3 years
                    $"Variant {variant.ProductCode} has unreasonable production quantity");
            }
        }
    }

    private void ValidateBusinessRules(ProductBatch batch)
    {
        var validVariants = batch.ValidVariants;
        
        if (!validVariants.Any())
            return; // No business rules to validate if no valid variants

        // Demand-based prioritization
        var highDemandVariants = validVariants.Where(v => v.DailySales >= validVariants.Average(x => x.DailySales)).ToList();
        var lowDemandVariants = validVariants.Where(v => v.DailySales < validVariants.Average(x => x.DailySales)).ToList();

        if (highDemandVariants.Any() && lowDemandVariants.Any())
        {
            var avgHighDemandCoverage = highDemandVariants.Average(v => v.GetDaysCoverageAfterProduction());
            var avgLowDemandCoverage = lowDemandVariants.Average(v => v.GetDaysCoverageAfterProduction());

            // High demand variants should generally have reasonable coverage
            // (This is a soft rule - might not always hold due to weight constraints)
            if (avgHighDemandCoverage > 0)
            {
                Assert.That(avgHighDemandCoverage, Is.GreaterThan(1),
                    "High demand variants should have at least 1 day coverage");
            }
        }

        // Efficiency check - batch should achieve reasonable utilization unless severely constrained
        var utilization = (batch.GetTotalProducedWeight() / batch.TotalWeight) * 100;
        var minWeight = validVariants.Min(v => v.Weight);
        var maxPossibleUnits = (int)(batch.TotalWeight / minWeight);
        
        if (maxPossibleUnits > 1) // If we can produce at least 2 units of something
        {
            Assert.That(utilization, Is.GreaterThan(1),
                "Should achieve at least 1% utilization when production is feasible");
        }
    }

    private ProductBatch CreateValidationTestBatch()
    {
        var variants = new List<ProductVariant>
        {
            ProductVariant.Create("LIGHT-HIGH", "Light High Demand", 250, 0.2, 30, 10),
            ProductVariant.Create("HEAVY-LOW", "Heavy Low Demand", 1000, 2.0, 3, 50),
            ProductVariant.Create("MEDIUM", "Medium Product", 500, 0.8, 15, 25),
            ProductVariant.Create("ZERO-STOCK", "Zero Stock", 300, 0.5, 20, 0),
            ProductVariant.Create("HIGH-STOCK", "High Stock", 400, 0.6, 10, 100)
        };
        return ProductBatch.Create("VALIDATION", "Validation Batch", 1500.0, variants);
    }
}
```

## End-to-End Test Scenarios

### Complete Optimization Workflow

```csharp
[TestFixture]
public class BatchOptimizationE2ETests
{
    private ManufactureAppService _service;
    private BatchDistributionCalculator _calculator;

    [SetUp]
    public void SetUp()
    {
        var logger = new NullLogger<BatchDistributionCalculator>();
        _calculator = new BatchDistributionCalculator(logger);

        var mockCatalogRepository = new Mock<ICatalogRepository>();
        var serviceLogger = new NullLogger<ManufactureAppService>();

        _service = new ManufactureAppService(
            null,
            mockCatalogRepository.Object,
            _calculator,
            serviceLogger);
    }

    [Test]
    public async Task CompleteOptimizationWorkflow_ShouldProduceValidResults()
    {
        // Arrange - Real-world scenario
        var request = new BatchDistributionRequestDto
        {
            ProductCode = "SOAP-FAMILY",
            ProductName = "Soap Product Family",
            TotalWeight = 2000.0, // 2 ton batch
            Strategy = OptimizationStrategy.MaximizeDays,
            MinimizeResidue = true,
            Variants = new List<ProductVariantDto>
            {
                new ProductVariantDto { ProductCode = "SOAP-100", ProductName = "Soap 100ml", Volume = 100, Weight = 0.15, DailySales = 25, CurrentStock = 80 },
                new ProductVariantDto { ProductCode = "SOAP-250", ProductName = "Soap 250ml", Volume = 250, Weight = 0.3, DailySales = 40, CurrentStock = 120 },
                new ProductVariantDto { ProductCode = "SOAP-500", ProductName = "Soap 500ml", Volume = 500, Weight = 0.6, DailySales = 30, CurrentStock = 60 },
                new ProductVariantDto { ProductCode = "SOAP-1000", ProductName = "Soap 1000ml", Volume = 1000, Weight = 1.2, DailySales = 15, CurrentStock = 30 },
                new ProductVariantDto { ProductCode = "SOAP-5000", ProductName = "Soap 5000ml", Volume = 5000, Weight = 5.5, DailySales = 3, CurrentStock = 10 }
            }
        };

        // Act
        var result = await _service.GetBatchDistributionAsync(request);

        // Assert - Comprehensive validation
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ProductCode, Is.EqualTo("SOAP-FAMILY"));
        Assert.That(result.TotalWeight, Is.EqualTo(2000.0));
        Assert.That(result.UsedWeight, Is.LessThanOrEqualTo(2000.0));
        Assert.That(result.RemainingWeight, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.UtilizationPercentage, Is.InRange(0, 100));

        // Verify all variants are present
        Assert.That(result.Variants.Count, Is.EqualTo(5));

        // Verify business logic
        var highDemandVariant = result.Variants.First(v => v.ProductCode == "SOAP-250"); // Highest demand
        var lowDemandVariant = result.Variants.First(v => v.ProductCode == "SOAP-5000"); // Lowest demand

        // High demand variant should get reasonable production
        Assert.That(highDemandVariant.SuggestedAmount, Is.GreaterThanOrEqualTo(0));

        // Verify metrics are calculated
        Assert.That(result.Metrics, Is.Not.Null);
        Assert.That(result.Metrics.TotalDailyConsumption, Is.GreaterThan(0));
        Assert.That(result.Metrics.OptimizationEfficiency, Is.InRange(0, 100));

        // Log results for manual verification
        Console.WriteLine($"Optimization Results:");
        Console.WriteLine($"Total Weight: {result.TotalWeight}kg");
        Console.WriteLine($"Used Weight: {result.UsedWeight}kg");
        Console.WriteLine($"Utilization: {result.UtilizationPercentage:F1}%");
        Console.WriteLine($"Days Coverage: {result.OptimalDaysCoverage:F1}");
        Console.WriteLine();

        foreach (var variant in result.Variants.OrderByDescending(v => v.SuggestedAmount))
        {
            Console.WriteLine($"{variant.ProductCode}: {variant.SuggestedAmount} units " +
                            $"({variant.TotalWeightProduced:F1}kg, {variant.DaysCoverageAfterProduction:F1} days coverage)");
        }
    }

    [Test]
    public async Task CompareOptimizationStrategies_ShouldShowDifferentResults()
    {
        // Arrange
        var baseRequest = CreateComparisonTestRequest();

        // Act - Run all three strategies
        var maxDaysRequest = CloneRequest(baseRequest);
        maxDaysRequest.Strategy = OptimizationStrategy.MaximizeDays;
        var maxDaysResult = await _service.GetBatchDistributionAsync(maxDaysRequest);

        var maxUtilizationRequest = CloneRequest(baseRequest);
        maxUtilizationRequest.Strategy = OptimizationStrategy.MaximizeUtilization;
        var maxUtilizationResult = await _service.GetBatchDistributionAsync(maxUtilizationRequest);

        var balancedRequest = CloneRequest(baseRequest);
        balancedRequest.Strategy = OptimizationStrategy.BalancedApproach;
        var balancedResult = await _service.GetBatchDistributionAsync(balancedRequest);

        // Assert - All should be valid but potentially different
        Assert.That(maxDaysResult.UtilizationPercentage, Is.LessThanOrEqualTo(100));
        Assert.That(maxUtilizationResult.UtilizationPercentage, Is.LessThanOrEqualTo(100));
        Assert.That(balancedResult.UtilizationPercentage, Is.LessThanOrEqualTo(100));

        // Max utilization should generally achieve higher utilization
        // (though not guaranteed due to discrete constraints)
        Console.WriteLine($"Max Days Utilization: {maxDaysResult.UtilizationPercentage:F1}%");
        Console.WriteLine($"Max Utilization: {maxUtilizationResult.UtilizationPercentage:F1}%");
        Console.WriteLine($"Balanced: {balancedResult.UtilizationPercentage:F1}%");

        // At least one strategy should achieve reasonable results
        var utilizationResults = new[] { 
            maxDaysResult.UtilizationPercentage, 
            maxUtilizationResult.UtilizationPercentage, 
            balancedResult.UtilizationPercentage 
        };
        Assert.That(utilizationResults.Max(), Is.GreaterThan(30));
    }

    private BatchDistributionRequestDto CreateComparisonTestRequest()
    {
        return new BatchDistributionRequestDto
        {
            ProductCode = "COMPARISON-TEST",
            ProductName = "Comparison Test",
            TotalWeight = 1000.0,
            MinimizeResidue = true,
            Variants = new List<ProductVariantDto>
            {
                new ProductVariantDto { ProductCode = "A", ProductName = "Product A", Volume = 200, Weight = 0.4, DailySales = 20, CurrentStock = 30 },
                new ProductVariantDto { ProductCode = "B", ProductName = "Product B", Volume = 500, Weight = 0.8, DailySales = 15, CurrentStock = 20 },
                new ProductVariantDto { ProductCode = "C", ProductName = "Product C", Volume = 1000, Weight = 1.5, DailySales = 8, CurrentStock = 15 }
            }
        };
    }

    private BatchDistributionRequestDto CloneRequest(BatchDistributionRequestDto original)
    {
        return new BatchDistributionRequestDto
        {
            ProductCode = original.ProductCode,
            ProductName = original.ProductName,
            TotalWeight = original.TotalWeight,
            MinimizeResidue = original.MinimizeResidue,
            Variants = original.Variants.Select(v => new ProductVariantDto
            {
                ProductCode = v.ProductCode,
                ProductName = v.ProductName,
                Volume = v.Volume,
                Weight = v.Weight,
                DailySales = v.DailySales,
                CurrentStock = v.CurrentStock
            }).ToList()
        };
    }
}
```

## Performance Test Scenarios

### Algorithm Performance Tests

```csharp
[TestFixture]
public class BatchOptimizationPerformanceTests
{
    [Test]
    public void OptimizeBatch_PerformanceBenchmark_ShouldMeetRequirements()
    {
        // Arrange
        var calculator = new BatchDistributionCalculator(new NullLogger<BatchDistributionCalculator>());
        var testCases = new[]
        {
            (Variants: 5, Weight: 1000.0, Name: "Small"),
            (Variants: 20, Weight: 5000.0, Name: "Medium"),
            (Variants: 50, Weight: 10000.0, Name: "Large")
        };

        foreach (var testCase in testCases)
        {
            var variants = GeneratePerformanceVariants(testCase.Variants);
            var batch = ProductBatch.Create($"PERF-{testCase.Name}", testCase.Name, testCase.Weight, variants);

            // Act
            var stopwatch = Stopwatch.StartNew();
            calculator.OptimizeBatch(batch);
            stopwatch.Stop();

            // Assert
            var maxAllowedTime = testCase.Variants <= 10 ? 100 : // Small: 100ms
                                testCase.Variants <= 30 ? 500 : // Medium: 500ms
                                2000; // Large: 2000ms

            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(maxAllowedTime),
                $"{testCase.Name} batch optimization took too long: {stopwatch.ElapsedMilliseconds}ms");

            Console.WriteLine($"{testCase.Name} batch ({testCase.Variants} variants): {stopwatch.ElapsedMilliseconds}ms");
        }
    }

    [Test]
    public void OptimizeBatch_MemoryUsage_ShouldBeReasonable()
    {
        // Arrange
        var calculator = new BatchDistributionCalculator(new NullLogger<BatchDistributionCalculator>());
        var variants = GeneratePerformanceVariants(100); // Large number of variants
        var batch = ProductBatch.Create("MEMORY-TEST", "Memory Test", 20000.0, variants);

        var initialMemory = GC.GetTotalMemory(true);

        // Act
        calculator.OptimizeBatch(batch);

        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = finalMemory - initialMemory;

        // Assert
        // Should not use more than 10MB for optimization
        Assert.That(memoryUsed, Is.LessThan(10 * 1024 * 1024),
            $"Memory usage too high: {memoryUsed / 1024 / 1024}MB");
    }

    private List<ProductVariant> GeneratePerformanceVariants(int count)
    {
        var variants = new List<ProductVariant>();
        var random = new Random(123); // Fixed seed

        for (int i = 0; i < count; i++)
        {
            variants.Add(ProductVariant.Create(
                $"PERF-{i:000}",
                $"Performance Variant {i}",
                100 + (i * 50), // Volume
                0.1 + (random.NextDouble() * 2), // Weight: 0.1-2.1kg
                1 + (random.NextDouble() * 30), // Sales: 1-31 units/day
                random.NextDouble() * 50)); // Stock: 0-50 units
        }

        return variants;
    }
}
```

## Test Data Builders

### Batch Test Builder

```csharp
public class BatchTestBuilder
{
    private string _productCode = "TEST-BATCH";
    private string _productName = "Test Batch";
    private double _totalWeight = 1000.0;
    private List<ProductVariant> _variants = new();

    public BatchTestBuilder WithProductCode(string code)
    {
        _productCode = code;
        return this;
    }

    public BatchTestBuilder WithProductName(string name)
    {
        _productName = name;
        return this;
    }

    public BatchTestBuilder WithTotalWeight(double weight)
    {
        _totalWeight = weight;
        return this;
    }

    public BatchTestBuilder WithVariant(string code, string name, double volume, double weight, double sales, double stock)
    {
        _variants.Add(ProductVariant.Create(code, name, volume, weight, sales, stock));
        return this;
    }

    public BatchTestBuilder WithStandardVariants()
    {
        return WithVariant("VAR-250", "Variant 250ml", 250, 0.3, 10, 50)
               .WithVariant("VAR-500", "Variant 500ml", 500, 0.6, 15, 30)
               .WithVariant("VAR-1000", "Variant 1000ml", 1000, 1.2, 8, 20);
    }

    public BatchTestBuilder WithHighDemandVariants()
    {
        return WithVariant("HIGH-1", "High Demand 1", 250, 0.3, 50, 10)
               .WithVariant("HIGH-2", "High Demand 2", 500, 0.6, 40, 15);
    }

    public BatchTestBuilder WithMixedDemandVariants()
    {
        return WithVariant("HIGH", "High Demand", 250, 0.3, 30, 20)
               .WithVariant("MEDIUM", "Medium Demand", 500, 0.6, 15, 40)
               .WithVariant("LOW", "Low Demand", 1000, 1.2, 5, 60);
    }

    public ProductBatch Build()
    {
        if (!_variants.Any())
        {
            WithStandardVariants();
        }

        return ProductBatch.Create(_productCode, _productName, _totalWeight, _variants);
    }
}
```

## Test Configuration

### Test Database and Mocking Setup

```csharp
public class BatchOptimizationTestFixture
{
    public BatchDistributionCalculator Calculator { get; private set; }
    public Mock<ILogger<BatchDistributionCalculator>> MockLogger { get; private set; }
    public Mock<ICatalogRepository> MockCatalogRepository { get; private set; }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        MockLogger = new Mock<ILogger<BatchDistributionCalculator>>();
        MockCatalogRepository = new Mock<ICatalogRepository>();
        Calculator = new BatchDistributionCalculator(MockLogger.Object);
    }

    [SetUp]
    public void SetUp()
    {
        // Reset mocks for each test
        MockLogger.Reset();
        MockCatalogRepository.Reset();
    }

    protected ProductBatch CreateSimpleBatch() => new BatchTestBuilder().Build();
    protected ProductBatch CreateComplexBatch() => new BatchTestBuilder().WithMixedDemandVariants().WithTotalWeight(2000).Build();
    protected ProductBatch CreateConstrainedBatch() => new BatchTestBuilder().WithTotalWeight(100).Build();
}
```

## Continuous Integration Test Pipeline

### Test Categories and Execution

```bash
# Fast unit tests (< 1 second each)
dotnet test --filter Category=Unit --no-build --parallel

# Algorithm tests (may take longer)
dotnet test --filter Category=Algorithm --no-build

# Performance tests (manual execution)
dotnet test --filter Category=Performance --no-build --logger "console;verbosity=detailed"

# Integration tests
dotnet test --filter Category=Integration --no-build

# All tests with coverage
dotnet test --no-build --collect:"XPlat Code Coverage" --results-directory ./TestResults
```

### Performance Benchmarking

```csharp
// Example BenchmarkDotNet setup for performance testing
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class BatchOptimizationBenchmarks
{
    private BatchDistributionCalculator _calculator;
    private ProductBatch _smallBatch;
    private ProductBatch _largeBatch;

    [GlobalSetup]
    public void Setup()
    {
        _calculator = new BatchDistributionCalculator(NullLogger<BatchDistributionCalculator>.Instance);
        _smallBatch = new BatchTestBuilder().WithStandardVariants().Build();
        _largeBatch = new BatchTestBuilder().WithTotalWeight(10000).Build();
        
        // Add 50 variants to large batch
        for (int i = 0; i < 47; i++) // 3 already added by WithStandardVariants
        {
            _largeBatch.Variants.Add(ProductVariant.Create($"VAR-{i:000}", $"Variant {i}", 250 + i, 0.3 + (i * 0.1), 10 + i, 50 - i));
        }
    }

    [Benchmark]
    public void OptimizeSmallBatch() => _calculator.OptimizeBatch(_smallBatch);

    [Benchmark]
    public void OptimizeLargeBatch() => _calculator.OptimizeBatch(_largeBatch);

    [Benchmark]
    public void OptimizeSmallBatch_Alternative() => _calculator.OptimizeBatch2(_smallBatch);

    [Benchmark]
    public void OptimizeLargeBatch_Alternative() => _calculator.OptimizeBatch2(_largeBatch);
}
```