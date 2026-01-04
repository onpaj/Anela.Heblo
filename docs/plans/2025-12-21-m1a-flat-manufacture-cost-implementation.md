# M1_A Flat Manufacture Cost Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement M1_A (Flat Manufacture Cost) calculation replacing the stub in FlatManufactureCostProvider with real business logic that distributes manufacturing costs across products using ManufactureDifficulty metric.

**Architecture:** M1_A uses a 12-month rolling window to calculate flat manufacturing costs. It fetches total manufacturing costs from ILedgerService (VYROBA department), gets manufacture history for all products, calculates weighted manufacturing points using historical ManufactureDifficulty values, and distributes costs proportionally. The calculation is cached in FlatManufactureCostCache and refreshed periodically.

**Tech Stack:** .NET 8, EF Core, Xunit, Moq, IMemoryCache

---

## Task 1: Add Required Dependencies to FlatManufactureCostProvider

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs:9-33`

**Step 1: Add dependency injection for required services**

Update the constructor to inject ILedgerService, IManufactureHistoryClient, and IManufactureDifficultyRepository:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.CostProviders;

/// <summary>
/// Flat manufacture cost provider (M1_A) - Distributes manufacturing costs across products using ManufactureDifficulty.
/// Business logic layer with cache fallback.
/// </summary>
public class FlatManufactureCostProvider : IFlatManufactureCostProvider
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private readonly IFlatManufactureCostCache _cache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly IManufactureDifficultyRepository _difficultyRepository;
    private readonly ILogger<FlatManufactureCostProvider> _logger;
    private readonly CostCacheOptions _options;

    public FlatManufactureCostProvider(
        IFlatManufactureCostCache cache,
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        IManufactureHistoryClient manufactureHistoryClient,
        IManufactureDifficultyRepository difficultyRepository,
        ILogger<FlatManufactureCostProvider> logger,
        IOptions<CostCacheOptions> options)
    {
        _cache = cache;
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
        _manufactureHistoryClient = manufactureHistoryClient;
        _difficultyRepository = difficultyRepository;
        _logger = logger;
        _options = options.Value;
    }
```

**Step 2: Build to verify no compilation errors**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: BUILD SUCCEEDED

**Step 3: Commit dependency injection changes**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs
git commit -m "feat(m1a): add required dependencies to FlatManufactureCostProvider"
```

---

## Task 2: Create Helper Method to Get Historical Difficulty

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs:177-end`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs` (create)

**Step 1: Write failing test for GetHistoricalDifficulty**

Create test file:

```csharp
using Anela.Heblo.Application.Features.Catalog.Cache;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

public class FlatManufactureCostProviderTests
{
    private const int DefaultDifficultyValue = 1;

    [Fact]
    public async Task GetHistoricalDifficulty_WithExistingSetting_ReturnsDifficultyValue()
    {
        // Arrange
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        var productCode = "TEST001";
        var referenceDate = new DateTime(2025, 6, 15);
        var expectedDifficulty = 5;

        difficultyRepoMock.Setup(r => r.FindAsync(productCode, referenceDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting
            {
                ProductCode = productCode,
                DifficultyValue = expectedDifficulty,
                ValidFrom = new DateTime(2025, 1, 1),
                ValidTo = null
            });

        var provider = CreateProvider(difficultyRepository: difficultyRepoMock.Object);

        // Act
        var result = await provider.GetHistoricalDifficultyAsync(productCode, referenceDate);

        // Assert
        Assert.Equal(expectedDifficulty, result);
    }

    [Fact]
    public async Task GetHistoricalDifficulty_WithNoSetting_ReturnsDefaultValue()
    {
        // Arrange
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        var productCode = "TEST002";
        var referenceDate = new DateTime(2025, 6, 15);

        difficultyRepoMock.Setup(r => r.FindAsync(productCode, referenceDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureDifficultySetting?)null);

        var provider = CreateProvider(difficultyRepository: difficultyRepoMock.Object);

        // Act
        var result = await provider.GetHistoricalDifficultyAsync(productCode, referenceDate);

        // Assert
        Assert.Equal(DefaultDifficultyValue, result);
    }

    private FlatManufactureCostProvider CreateProvider(
        IFlatManufactureCostCache? cache = null,
        ICatalogRepository? catalogRepository = null,
        ILedgerService? ledgerService = null,
        IManufactureHistoryClient? manufactureHistoryClient = null,
        IManufactureDifficultyRepository? difficultyRepository = null,
        ILogger<FlatManufactureCostProvider>? logger = null,
        CostCacheOptions? options = null)
    {
        return new FlatManufactureCostProvider(
            cache ?? new FlatManufactureCostCache(new MemoryCache(new MemoryCacheOptions())),
            catalogRepository ?? Mock.Of<ICatalogRepository>(),
            ledgerService ?? Mock.Of<ILedgerService>(),
            manufactureHistoryClient ?? Mock.Of<IManufactureHistoryClient>(),
            difficultyRepository ?? Mock.Of<IManufactureDifficultyRepository>(),
            logger ?? Mock.Of<ILogger<FlatManufactureCostProvider>>(),
            Options.Create(options ?? new CostCacheOptions())
        );
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FlatManufactureCostProviderTests.GetHistoricalDifficulty" -v n`
Expected: FAIL with "method GetHistoricalDifficultyAsync does not exist"

**Step 3: Implement GetHistoricalDifficultyAsync method**

Add method to FlatManufactureCostProvider.cs after FilterByProductCodes:

```csharp
    /// <summary>
    /// Default difficulty value when no setting exists for a product.
    /// </summary>
    private const int DefaultDifficultyValue = 1;

    /// <summary>
    /// Gets the historical manufacturing difficulty for a product at a specific date.
    /// Returns DefaultDifficultyValue if no setting exists.
    /// </summary>
    private async Task<int> GetHistoricalDifficultyAsync(string productCode, DateTime referenceDate, CancellationToken ct = default)
    {
        var setting = await _difficultyRepository.FindAsync(productCode, referenceDate, ct);
        return setting?.DifficultyValue ?? DefaultDifficultyValue;
    }
```

**Step 4: Make method public for testing (temporary)**

Change method visibility to public:

```csharp
    public async Task<int> GetHistoricalDifficultyAsync(string productCode, DateTime referenceDate, CancellationToken ct = default)
```

**Step 5: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FlatManufactureCostProviderTests.GetHistoricalDifficulty" -v n`
Expected: PASS (2 tests)

**Step 6: Commit helper method**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs
git commit -m "feat(m1a): add GetHistoricalDifficultyAsync helper method"
```

---

## Task 3: Implement Core M1_A Calculation Logic

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs:149-164`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs`

**Step 1: Write failing test for M1_A calculation with simple scenario**

Add test to FlatManufactureCostProviderTests.cs:

```csharp
    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithSingleProduct_DistributesCostsCorrectly()
    {
        // Arrange
        var productCode = "PROD001";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 3, 31);

        // Manufacturing costs: 1000 CZK in Jan, 2000 CZK in Feb, 1500 CZK in Mar
        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime from, DateTime to, string dept, CancellationToken ct) =>
            {
                var costs = new List<CostStatistics>();
                var currentMonth = new DateTime(from.Year, from.Month, 1);
                var endMonth = new DateTime(to.Year, to.Month, 1);

                while (currentMonth <= endMonth)
                {
                    decimal cost = currentMonth.Month switch
                    {
                        1 => 1000m,
                        2 => 2000m,
                        3 => 1500m,
                        _ => 0m
                    };
                    costs.Add(new CostStatistics
                    {
                        Date = currentMonth,
                        Cost = cost,
                        Department = "VYROBA"
                    });
                    currentMonth = currentMonth.AddMonths(1);
                }
                return costs;
            });

        // Manufacture history: 10 pieces in Jan, 20 pieces in Feb, 15 pieces in Mar
        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime from, DateTime to, string? code, CancellationToken ct) =>
            {
                return new List<ManufactureHistoryRecord>
                {
                    new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = productCode, DocumentNumber = "DOC001", PricePerPiece = 0, PriceTotal = 0 },
                    new() { Date = new DateTime(2025, 2, 15), Amount = 20, ProductCode = productCode, DocumentNumber = "DOC002", PricePerPiece = 0, PriceTotal = 0 },
                    new() { Date = new DateTime(2025, 3, 15), Amount = 15, ProductCode = productCode, DocumentNumber = "DOC003", PricePerPiece = 0, PriceTotal = 0 }
                };
            });

        // Difficulty: constant value of 2 for all periods
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        difficultyRepoMock.Setup(r => r.FindAsync(productCode, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting
            {
                ProductCode = productCode,
                DifficultyValue = 2,
                ValidFrom = new DateTime(2024, 1, 1),
                ValidTo = null
            });

        var product = new CatalogAggregate
        {
            ProductCode = productCode
        };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object,
            difficultyRepository: difficultyRepoMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo);

        // Assert
        // Total weighted points = (10 * 2) + (20 * 2) + (15 * 2) = 90
        // Total costs = 1000 + 2000 + 1500 = 4500
        // Cost per point = 4500 / 90 = 50
        // Cost per piece = 50 * 2 = 100 (difficulty is 2)
        Assert.Equal(3, result.Count);
        Assert.All(result, cost => Assert.Equal(100m, cost.Cost));
    }
```

**Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FlatManufactureCostProviderTests.CalculateFlatManufacturingCosts_WithSingleProduct" -v n`
Expected: FAIL with "method CalculateFlatManufacturingCostsAsync does not exist"

**Step 3: Implement CalculateFlatManufacturingCostsAsync method (replace stub)**

Replace the CalculateFlatManufacturingCosts method (lines 149-164) with:

```csharp
    /// <summary>
    /// Calculates flat manufacturing costs (M1_A) for a product over a date range.
    /// Uses rolling window approach with ManufactureDifficulty weighting.
    /// </summary>
    private async Task<List<MonthlyCost>> CalculateFlatManufacturingCostsAsync(
        CatalogAggregate product,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(product.ProductCode))
        {
            return new List<MonthlyCost>();
        }

        // Step 1: Get total manufacturing costs for the period (VYROBA department)
        var costsFrom = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var costsTo = new DateTime(dateTo.Year, dateTo.Month, DateTime.DaysInMonth(dateTo.Year, dateTo.Month), 23, 59, 59);

        var manufacturingCosts = await _ledgerService.GetDirectCosts(
            costsFrom,
            costsTo,
            "VYROBA",
            ct);

        // Step 2: Get manufacture history for ALL products in the period
        var allManufactureHistory = await _manufactureHistoryClient.GetHistoryAsync(
            costsFrom,
            costsTo,
            null, // null = all products
            ct);

        if (!allManufactureHistory.Any())
        {
            _logger.LogWarning(
                "No manufacture history found for period {DateFrom} to {DateTo}",
                dateFrom,
                dateTo);
            return new List<MonthlyCost>();
        }

        // Step 3: Group costs and history by month
        var costsByMonth = manufacturingCosts
            .GroupBy(c => new DateTime(c.Date.Year, c.Date.Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Cost));

        var historyByMonth = allManufactureHistory
            .GroupBy(h => new DateTime(h.Date.Year, h.Date.Month, 1))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Step 4: Calculate cost per manufacturing point for each month
        var monthlyCosts = new List<MonthlyCost>();
        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentMonth <= endMonth)
        {
            if (!costsByMonth.TryGetValue(currentMonth, out var monthCosts) || monthCosts == 0)
            {
                // No costs for this month - zero cost
                monthlyCosts.Add(new MonthlyCost(currentMonth, 0m));
                currentMonth = currentMonth.AddMonths(1);
                continue;
            }

            if (!historyByMonth.TryGetValue(currentMonth, out var monthHistory) || !monthHistory.Any())
            {
                // No production in this month - zero cost
                monthlyCosts.Add(new MonthlyCost(currentMonth, 0m));
                currentMonth = currentMonth.AddMonths(1);
                continue;
            }

            // Calculate total weighted manufacturing points for this month (all products)
            var totalWeightedPoints = 0.0;
            foreach (var record in monthHistory)
            {
                var difficulty = await GetHistoricalDifficultyAsync(record.ProductCode, record.Date, ct);
                totalWeightedPoints += record.Amount * difficulty;
            }

            if (totalWeightedPoints == 0)
            {
                monthlyCosts.Add(new MonthlyCost(currentMonth, 0m));
                currentMonth = currentMonth.AddMonths(1);
                continue;
            }

            // Calculate cost per point for this month
            var costPerPoint = monthCosts / (decimal)totalWeightedPoints;

            // Calculate cost for this specific product
            var productDifficulty = await GetHistoricalDifficultyAsync(product.ProductCode, currentMonth, ct);
            var productCost = costPerPoint * productDifficulty;

            monthlyCosts.Add(new MonthlyCost(currentMonth, productCost));
            currentMonth = currentMonth.AddMonths(1);
        }

        return monthlyCosts;
    }
```

**Step 4: Make method public and async (update signature)**

Update method name in ComputeAllCostsAsync and ComputeCostsAsync to call the new async version:

```csharp
// In ComputeAllCostsAsync (line 106)
var monthlyCosts = await CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo, ct);

// In ComputeCostsAsync (line 142)
var monthlyCosts = await CalculateFlatManufacturingCostsAsync(product, from, to, ct);
```

Make the method public for testing:

```csharp
public async Task<List<MonthlyCost>> CalculateFlatManufacturingCostsAsync(
```

**Step 5: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FlatManufactureCostProviderTests.CalculateFlatManufacturingCosts_WithSingleProduct" -v n`
Expected: PASS

**Step 6: Commit M1_A calculation implementation**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs
git commit -m "feat(m1a): implement core M1_A calculation with difficulty weighting"
```

---

## Task 4: Test Multiple Products Scenario

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs`

**Step 1: Write test for multiple products with different difficulties**

Add test:

```csharp
    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithMultipleProducts_DistributesCostsProportionally()
    {
        // Arrange
        var product1Code = "PROD001";
        var product2Code = "PROD002";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        // Total cost: 9000 CZK in January
        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = new DateTime(2025, 1, 1), Cost = 9000m, Department = "VYROBA" }
            });

        // Manufacture history:
        // PROD001: 10 pieces with difficulty 2 = 20 weighted points
        // PROD002: 20 pieces with difficulty 4 = 80 weighted points
        // Total: 100 weighted points
        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = product1Code, DocumentNumber = "DOC001", PricePerPiece = 0, PriceTotal = 0 },
                new() { Date = new DateTime(2025, 1, 15), Amount = 20, ProductCode = product2Code, DocumentNumber = "DOC002", PricePerPiece = 0, PriceTotal = 0 }
            });

        // Difficulty settings
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        difficultyRepoMock.Setup(r => r.FindAsync(product1Code, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting { ProductCode = product1Code, DifficultyValue = 2 });
        difficultyRepoMock.Setup(r => r.FindAsync(product2Code, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting { ProductCode = product2Code, DifficultyValue = 4 });

        var product1 = new CatalogAggregate { ProductCode = product1Code };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object,
            difficultyRepository: difficultyRepoMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product1, dateFrom, dateTo);

        // Assert
        // Cost per point = 9000 / 100 = 90
        // PROD001 cost = 90 * 2 = 180
        Assert.Single(result);
        Assert.Equal(180m, result[0].Cost);
    }
```

**Step 2: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FlatManufactureCostProviderTests.CalculateFlatManufacturingCosts_WithMultipleProducts" -v n`
Expected: PASS

**Step 3: Commit multiple products test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs
git commit -m "test(m1a): add test for multiple products cost distribution"
```

---

## Task 5: Test Edge Cases

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs`

**Step 1: Write tests for edge cases**

Add tests:

```csharp
    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithNoManufactureHistory_ReturnsEmptyList()
    {
        // Arrange
        var productCode = "PROD001";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = new DateTime(2025, 1, 1), Cost = 1000m, Department = "VYROBA" }
            });

        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>());

        var product = new CatalogAggregate { ProductCode = productCode };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithNoManufacturingCosts_ReturnsZeroCosts()
    {
        // Arrange
        var productCode = "PROD001";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = productCode, DocumentNumber = "DOC001", PricePerPiece = 0, PriceTotal = 0 }
            });

        var product = new CatalogAggregate { ProductCode = productCode };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo);

        // Assert
        Assert.Single(result);
        Assert.Equal(0m, result[0].Cost);
    }

    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithProductNotManufacturedInPeriod_ReturnsZeroCosts()
    {
        // Arrange
        var productCode = "PROD001";
        var otherProductCode = "PROD002";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = new DateTime(2025, 1, 1), Cost = 1000m, Department = "VYROBA" }
            });

        // Only other product was manufactured
        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = otherProductCode, DocumentNumber = "DOC001", PricePerPiece = 0, PriceTotal = 0 }
            });

        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        difficultyRepoMock.Setup(r => r.FindAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting { DifficultyValue = 1 });

        var product = new CatalogAggregate { ProductCode = productCode };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object,
            difficultyRepository: difficultyRepoMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo);

        // Assert
        // Product was not manufactured but costs still distributed = 0 cost
        Assert.Single(result);
        Assert.Equal(0m, result[0].Cost);
    }
```

**Step 2: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FlatManufactureCostProviderTests" -v n`
Expected: PASS (all tests)

**Step 3: Commit edge case tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs
git commit -m "test(m1a): add edge case tests for M1_A calculation"
```

---

## Task 6: Update DI Registration in CatalogModule

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`

**Step 1: Verify DI registration includes all required services**

Check that CatalogModule.cs registers:
- ILedgerService (should already be registered globally)
- IManufactureHistoryClient (should already be registered)
- IManufactureDifficultyRepository (should already be registered)
- FlatManufactureCostProvider with all dependencies

**Step 2: Build solution to verify DI resolution works**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: BUILD SUCCEEDED

**Step 3: If build fails, update DI registrations**

Ensure proper service lifetimes in CatalogModule.cs:

```csharp
// Cost providers (Scoped - uses scoped dependencies like repositories)
services.AddScoped<IFlatManufactureCostProvider, FlatManufactureCostProvider>();
```

**Step 4: Run build again to verify**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: BUILD SUCCEEDED

**Step 5: Commit DI registration (if changed)**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat(m1a): verify DI registration for FlatManufactureCostProvider"
```

---

## Task 7: Change Method Visibility to Internal

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs`
- Modify: `backend/test/Anela.Heblo.Tests/AssemblyInfo.cs` (create if needed)

**Step 1: Make test methods accessible via InternalsVisibleTo**

Create or update AssemblyInfo.cs in Application project:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]
```

**Step 2: Change method visibility from public to internal**

Update method signatures:

```csharp
// Change from public to internal
internal async Task<int> GetHistoricalDifficultyAsync(string productCode, DateTime referenceDate, CancellationToken ct = default)

internal async Task<List<MonthlyCost>> CalculateFlatManufacturingCostsAsync(
```

**Step 3: Run all tests to verify they still pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FlatManufactureCostProviderTests" -v n`
Expected: PASS (all tests)

**Step 4: Commit visibility changes**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs
git add backend/src/Anela.Heblo.Application/AssemblyInfo.cs
git commit -m "refactor(m1a): change test methods to internal visibility"
```

---

## Task 8: Run Full Test Suite

**Files:**
- N/A (verification only)

**Step 1: Run all backend tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v n`
Expected: PASS (all tests)

**Step 2: Run backend build**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: BUILD SUCCEEDED

**Step 3: Run dotnet format to ensure code quality**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: No formatting issues

**Step 4: Fix any formatting issues if needed**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: Files formatted

**Step 5: Commit formatting changes if any**

```bash
git add -A
git commit -m "style(m1a): apply dotnet format"
```

---

## Task 9: Documentation Update

**Files:**
- Modify: `docs/plans/2025-12-21-margin-cache-architecture.md` (if exists)
- Create: `docs/features/margins_v2/m1a_implementation_notes.md`

**Step 1: Create implementation notes**

Document the implementation details:

```markdown
# M1_A Implementation Notes

## Overview

Implemented flat manufacture cost calculation (M1_A) that distributes manufacturing costs across products using ManufactureDifficulty metric.

## Implementation Details

### Algorithm

1. **Get Manufacturing Costs**: Fetch total costs from ILedgerService for VYROBA department
2. **Get Manufacture History**: Fetch all product manufacture history for the period
3. **Calculate Weighted Points**: For each month:
   - Sum weighted manufacturing points: `Σ(amount × difficulty)` for all products
   - Get manufacturing costs for that month
   - Calculate cost per point: `totalCosts / totalWeightedPoints`
4. **Distribute Costs**: Calculate cost for specific product: `costPerPoint × productDifficulty`

### Key Components

- `FlatManufactureCostProvider`: Main implementation class
- `GetHistoricalDifficultyAsync()`: Helper to get difficulty at specific date
- `CalculateFlatManufacturingCostsAsync()`: Core calculation logic

### Dependencies

- `ILedgerService`: Provides manufacturing costs
- `IManufactureHistoryClient`: Provides manufacture history
- `IManufactureDifficultyRepository`: Provides difficulty settings
- `IFlatManufactureCostCache`: Caches computed results

### Edge Cases Handled

- No manufacture history → returns empty list
- No manufacturing costs → returns zero costs
- Product not manufactured in period → returns zero cost
- Missing difficulty setting → uses default value of 1

## Testing

### Test Coverage

- Single product cost distribution
- Multiple products with different difficulties
- Edge cases (no history, no costs, product not manufactured)
- Historical difficulty lookup

### Test Files

- `FlatManufactureCostProviderTests.cs`: Unit tests for M1_A calculation

## Configuration

Uses `CostCacheOptions.M1ARollingWindowMonths` (default: 12 months) for rolling window size.

## Performance Considerations

- Calculation is cached in `FlatManufactureCostCache`
- Refreshed periodically (configurable interval)
- Single semaphore prevents concurrent refreshes
```

**Step 2: Commit documentation**

```bash
git add docs/features/margins_v2/m1a_implementation_notes.md
git commit -m "docs(m1a): add M1_A implementation notes"
```

---

## Summary

**Total Tasks**: 9
**Estimated Time**: 2-3 hours
**Test Coverage**: Unit tests for all scenarios including edge cases
**Dependencies**: ILedgerService, IManufactureHistoryClient, IManufactureDifficultyRepository

**Key Deliverables**:
1. ✅ Fully implemented M1_A calculation
2. ✅ Comprehensive test suite
3. ✅ DI registration verified
4. ✅ Code formatted and documented
5. ✅ Implementation notes

**Next Steps**:
- Implement M1_B (Direct Manufacture Cost)
- Implement M2 (Sales Cost)
- Integration testing with MarginCalculationService
