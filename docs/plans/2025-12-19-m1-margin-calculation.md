# M1 Margin Calculation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Update MarginCalculationService to calculate M1_A (economic baseline) and M1_B (actual monthly cost) using Complexity Point-weighted formulas per specification.

**Architecture:** Split M1 into two distinct metrics (M1_A baseline, M1_B actual), calculate company-wide produced CP across all products, apply rolling 12-month window for M1_A and per-month calculation for M1_B.

**Tech Stack:** .NET 8, C#, EF Core, Clean Architecture

**Related Documentation:**
- Design: `docs/features/M1_margin_calculation_design.md`
- Specification: `docs/features/M1_production_costs_specification.md`

---

## Task 1: Update MonthlyMarginData Domain Model

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/MonthlyMarginData.cs`

**Step 1: Update MonthlyMarginData to split M1 into M1_A and M1_B**

Replace the `M1` property with two new properties:

```csharp
// In MonthlyMarginData class, replace:
public MarginLevel M1 { get; set; }

// With:
public MarginLevel M1_A { get; set; }  // Economic baseline (always present)
public MarginLevel? M1_B { get; set; } // Actual monthly cost (nullable - only when produced)
```

**Step 2: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/MonthlyMarginData.cs
git commit -m "feat: split M1 into M1_A (baseline) and M1_B (actual) in MonthlyMarginData"
```

---

## Task 2: Update MarginData Domain Model

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/MarginData.cs`

**Step 1: Update MarginData to include M1_A and M1_B**

Replace the `M1` property:

```csharp
// In MarginData class, replace:
public MarginLevel M1 { get; set; }

// With:
public MarginLevel M1_A { get; set; }  // Average M1_A across months
public MarginLevel M1_B { get; set; }  // Average M1_B (only months with production)
```

**Step 2: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/MarginData.cs
git commit -m "feat: add M1_A and M1_B to MarginData averages"
```

---

## Task 3: Add Company-Wide CP Calculation Method

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs`

**Step 1: Add CalculateCompanyWideProducedCPAsync method**

Add this new private method to MarginCalculationService:

```csharp
private async Task<Dictionary<DateTime, decimal>> CalculateCompanyWideProducedCPAsync(
    IEnumerable<CatalogAggregate> allProducts,
    DateOnly dateFrom,
    DateOnly dateTo,
    CancellationToken cancellationToken)
{
    var producedCPByMonth = new Dictionary<DateTime, decimal>();

    foreach (var product in allProducts)
    {
        if (product.ManufactureHistory == null || !product.ManufactureHistory.Any())
            continue;

        // Get production records in date range
        var productionRecords = product.ManufactureHistory
            .Where(h => h.Date >= dateFrom.ToDateTime(TimeOnly.MinValue)
                     && h.Date <= dateTo.ToDateTime(TimeOnly.MinValue))
            .ToList();

        foreach (var record in productionRecords)
        {
            // Get CP valid at production date
            var complexityPoints = product.ManufactureDifficulty;
            if (!complexityPoints.HasValue)
            {
                _logger.LogWarning(
                    "Product {ProductCode} has no ManufactureDifficulty, skipping production record from {Date}",
                    product.ProductCode, record.Date);
                continue;
            }

            // Calculate produced CP for this record
            var producedCP = record.Amount * (decimal)complexityPoints.Value;

            // Aggregate by month (first day of month as key)
            var monthKey = new DateTime(record.Date.Year, record.Date.Month, 1);
            if (!producedCPByMonth.ContainsKey(monthKey))
                producedCPByMonth[monthKey] = 0;

            producedCPByMonth[monthKey] += producedCP;
        }
    }

    return producedCPByMonth;
}
```

**Step 2: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs
git commit -m "feat: add company-wide produced CP calculation method"
```

---

## Task 4: Add M1_A Calculation Method

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs`

**Step 1: Add CalculateM1_A_PerMonth method**

Add this new private method:

```csharp
private Dictionary<DateTime, decimal> CalculateM1_A_PerMonth(
    double productComplexityPoints,
    Dictionary<DateTime, decimal> companyWideProducedCP,
    List<MonthlyCost> m1Costs,
    DateOnly dateFrom,
    DateOnly dateTo)
{
    var m1_A_ByMonth = new Dictionary<DateTime, decimal>();

    // Generate list of months in the date range
    var currentDate = new DateTime(dateFrom.Year, dateFrom.Month, 1);
    var endDateTime = new DateTime(dateTo.Year, dateTo.Month, 1);

    while (currentDate <= endDateTime)
    {
        // Define 12-month reference period ending at current month
        var referenceStart = currentDate.AddMonths(-11);
        var referenceEnd = currentDate;

        // Sum M1 costs in reference period
        var totalM1Costs = m1Costs
            .Where(c => c.Month >= referenceStart && c.Month <= referenceEnd)
            .Sum(c => c.Cost);

        // Sum produced CP in reference period
        var totalProducedCP = companyWideProducedCP
            .Where(kvp => kvp.Key >= referenceStart && kvp.Key <= referenceEnd)
            .Sum(kvp => kvp.Value);

        // Calculate M1_A
        decimal m1_A;
        if (totalProducedCP > 0)
        {
            var costPerCP = totalM1Costs / totalProducedCP;
            m1_A = (decimal)productComplexityPoints * costPerCP;
        }
        else
        {
            _logger.LogWarning(
                "No production data in reference period ending {Month}, M1_A set to 0",
                currentDate);
            m1_A = 0;
        }

        m1_A_ByMonth[currentDate] = m1_A;
        currentDate = currentDate.AddMonths(1);
    }

    return m1_A_ByMonth;
}
```

**Step 2: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs
git commit -m "feat: add M1_A (economic baseline) calculation with 12-month rolling window"
```

---

## Task 5: Add M1_B Calculation Method

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs`

**Step 1: Add CalculateM1_B_PerMonth method**

Add this new private method:

```csharp
private Dictionary<DateTime, decimal?> CalculateM1_B_PerMonth(
    CatalogAggregate product,
    double productComplexityPoints,
    Dictionary<DateTime, decimal> companyWideProducedCP,
    List<MonthlyCost> m1Costs)
{
    var m1_B_ByMonth = new Dictionary<DateTime, decimal?>();

    // Check which months this product was produced
    var productionMonths = product.ManufactureHistory
        ?.GroupBy(h => new DateTime(h.Date.Year, h.Date.Month, 1))
        .Select(g => g.Key)
        .ToHashSet() ?? new HashSet<DateTime>();

    // For each month in company-wide data
    foreach (var monthKey in companyWideProducedCP.Keys)
    {
        // Check if this product was produced in this month
        if (!productionMonths.Contains(monthKey))
        {
            m1_B_ByMonth[monthKey] = null; // Not produced
            continue;
        }

        // Get M1 costs for this month
        var m1CostForMonth = m1Costs.FirstOrDefault(c => c.Month == monthKey)?.Cost ?? 0;

        // Get total produced CP for this month
        var totalProducedCP = companyWideProducedCP[monthKey];

        // Calculate M1_B
        if (totalProducedCP > 0)
        {
            var m1_B_per_CP = m1CostForMonth / totalProducedCP;
            var m1_B = (decimal)productComplexityPoints * m1_B_per_CP;
            m1_B_ByMonth[monthKey] = m1_B;
        }
        else
        {
            m1_B_ByMonth[monthKey] = null; // No production company-wide
        }
    }

    return m1_B_ByMonth;
}
```

**Step 2: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs
git commit -m "feat: add M1_B (actual monthly cost) calculation"
```

---

## Task 6: Update GetMarginAsync Signature

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Services/IMarginCalculationService.cs`

**Step 1: Update interface signature**

In `IMarginCalculationService.cs`:

```csharp
// Update method signature to include allProducts parameter
Task<MonthlyMarginHistory> GetMarginAsync(
    CatalogAggregate product,
    IEnumerable<CatalogAggregate> allProducts, // NEW parameter
    DateOnly dateFrom,
    DateOnly dateTo,
    CancellationToken cancellationToken = default);
```

**Step 2: Update implementation signature and add company-wide CP calculation**

In `MarginCalculationService.cs`, update the `GetMarginAsync` method:

```csharp
public async Task<MonthlyMarginHistory> GetMarginAsync(
    CatalogAggregate product,
    IEnumerable<CatalogAggregate> allProducts, // NEW parameter
    DateOnly dateFrom,
    DateOnly dateTo,
    CancellationToken cancellationToken = default)
{
    try
    {
        var sellingPrice = product.PriceWithoutVat ?? 0;

        if (sellingPrice <= 0 || string.IsNullOrEmpty(product.ProductCode))
        {
            return new MonthlyMarginHistory();
        }

        // Calculate company-wide produced CP (NEW)
        var companyWideProducedCP = await CalculateCompanyWideProducedCPAsync(
            allProducts, dateFrom, dateTo, cancellationToken);

        // Load cost data for the specified period
        var costData = await LoadCostDataAsync(product, dateFrom, dateTo, cancellationToken);

        // Calculate monthly margin history using the loaded data
        var monthlyHistory = CalculateMarginHistoryFromData(
            product, sellingPrice, costData, companyWideProducedCP, dateFrom, dateTo);

        return monthlyHistory;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error calculating monthly margin history for product {ProductCode}", product.ProductCode);
        return new MonthlyMarginHistory();
    }
}
```

**Step 3: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build fails (callers not updated yet - expected)

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/Services/IMarginCalculationService.cs
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs
git commit -m "feat: update GetMarginAsync signature to accept allProducts parameter"
```

---

## Task 7: Update CalculateMarginHistoryFromData Method

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs`

**Step 1: Update method signature and implementation**

```csharp
private MonthlyMarginHistory CalculateMarginHistoryFromData(
    CatalogAggregate product,
    decimal sellingPrice,
    CostData costData,
    Dictionary<DateTime, decimal> companyWideProducedCP, // NEW parameter
    DateOnly dateFrom,
    DateOnly dateTo)
{
    var monthlyData = new List<MonthlyMarginData>();

    // Get product complexity points
    var productCP = product.ManufactureDifficulty ?? 0;

    // Calculate M1_A and M1_B for all months
    var m1_A_ByMonth = CalculateM1_A_PerMonth(
        productCP, companyWideProducedCP, costData.ManufactureCosts, dateFrom, dateTo);

    var m1_B_ByMonth = CalculateM1_B_PerMonth(
        product, productCP, companyWideProducedCP, costData.ManufactureCosts);

    // Generate list of months in the date range
    var currentDate = new DateTime(dateFrom.Year, dateFrom.Month, 1);
    var endDateTime = new DateTime(dateTo.Year, dateTo.Month, 1);

    while (currentDate <= endDateTime)
    {
        var monthlyMargin = CalculateMarginForMonth(
            currentDate, sellingPrice, costData, m1_A_ByMonth, m1_B_ByMonth);
        monthlyData.Add(monthlyMargin);
        currentDate = currentDate.AddMonths(1);
    }

    var averages = CalculateMarginAverages(monthlyData);

    return new MonthlyMarginHistory
    {
        MonthlyData = monthlyData,
        Averages = averages
    };
}
```

**Step 2: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build fails (CalculateMarginForMonth signature needs update)

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs
git commit -m "feat: integrate M1_A and M1_B calculations into margin history"
```

---

## Task 8: Update CalculateMarginForMonth Method

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs`

**Step 1: Update method signature and implementation**

Replace the existing `CalculateMarginForMonth` method:

```csharp
private MonthlyMarginData CalculateMarginForMonth(
    DateTime month,
    decimal sellingPrice,
    CostData costData,
    Dictionary<DateTime, decimal> m1_A_ByMonth,
    Dictionary<DateTime, decimal?> m1_B_ByMonth)
{
    // Get costs for specific month or closest available month
    var materialCost = GetCostForMonth(month, costData.MaterialCosts);
    var salesCost = GetCostForMonth(month, costData.SalesCosts);
    var overheadCost = GetCostForMonth(month, costData.OverheadCosts);

    // Get M1_A and M1_B for this month
    var m1_A_Cost = m1_A_ByMonth.GetValueOrDefault(month, 0);
    var m1_B_Cost = m1_B_ByMonth.GetValueOrDefault(month, null);

    // Use M1_A for cost breakdown (cumulative totals)
    var costBreakdown = new CostBreakdown(materialCost, m1_A_Cost, salesCost, overheadCost);

    return new MonthlyMarginData
    {
        Month = month,
        M0 = MarginLevel.Create(sellingPrice, costBreakdown.M0CostTotal, materialCost),
        M1_A = MarginLevel.Create(sellingPrice, costBreakdown.M1CostTotal, m1_A_Cost),
        M1_B = m1_B_Cost.HasValue
            ? MarginLevel.Create(sellingPrice, costBreakdown.M0CostTotal + m1_B_Cost.Value, m1_B_Cost.Value)
            : null,
        M2 = MarginLevel.Create(sellingPrice, costBreakdown.M2CostTotal, salesCost),
        M3 = MarginLevel.Create(sellingPrice, costBreakdown.M3CostTotal, overheadCost),
        CostsForMonth = costBreakdown
    };
}
```

**Step 2: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build fails (CalculateMarginAverages needs update)

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs
git commit -m "feat: update CalculateMarginForMonth to use M1_A and M1_B"
```

---

## Task 9: Update CalculateMarginAverages Method

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs`

**Step 1: Update method to average M1_A and M1_B separately**

Replace the existing `CalculateMarginAverages` method:

```csharp
private MarginData CalculateMarginAverages(List<MonthlyMarginData> monthlyData)
{
    var validData = monthlyData.Where(m => m.M0.Percentage > 0).ToList();

    if (!validData.Any())
    {
        return new MarginData();
    }

    // For M1_B, only average months where it's not null (product was produced)
    var validM1_B_Data = validData.Where(m => m.M1_B != null).ToList();

    return new MarginData
    {
        M0 = new MarginLevel(
            validData.Average(m => m.M0.Percentage),
            validData.Average(m => m.M0.Amount),
            validData.Average(m => m.M0.CostTotal),
            validData.Average(m => m.M0.CostLevel)
        ),
        M1_A = new MarginLevel(
            validData.Average(m => m.M1_A.Percentage),
            validData.Average(m => m.M1_A.Amount),
            validData.Average(m => m.M1_A.CostTotal),
            validData.Average(m => m.M1_A.CostLevel)
        ),
        M1_B = validM1_B_Data.Any()
            ? new MarginLevel(
                validM1_B_Data.Average(m => m.M1_B!.Percentage),
                validM1_B_Data.Average(m => m.M1_B!.Amount),
                validM1_B_Data.Average(m => m.M1_B!.CostTotal),
                validM1_B_Data.Average(m => m.M1_B!.CostLevel)
            )
            : new MarginLevel(0, 0, 0, 0),
        M2 = new MarginLevel(
            validData.Average(m => m.M2.Percentage),
            validData.Average(m => m.M2.Amount),
            validData.Average(m => m.M2.CostTotal),
            validData.Average(m => m.M2.CostLevel)
        ),
        M3 = new MarginLevel(
            validData.Average(m => m.M3.Percentage),
            validData.Average(m => m.M3.Amount),
            validData.Average(m => m.M3.CostTotal),
            validData.Average(m => m.M3.CostLevel)
        )
    };
}
```

**Step 2: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build fails (callers need updating)

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs
git commit -m "feat: update margin averages to handle M1_A and M1_B separately"
```

---

## Task 10: Find All Callers of GetMarginAsync

**Files:**
- N/A (search task)

**Step 1: Search for all usages of GetMarginAsync**

Run: `grep -r "GetMarginAsync" backend/src --include="*.cs" -n`

Expected output: List of files calling GetMarginAsync

**Step 2: Document findings**

Create a list of all files that need updating. Common locations:
- Controllers
- Handlers (MediatR)
- Services

**Step 3: No commit (search only)**

---

## Task 11: Update Caller - CatalogAggregate

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs`

**Step 1: Find the RefreshMargin method**

Locate where `_marginCalculationService.GetMarginAsync` is called.

**Step 2: Update the call to include allProducts**

The caller needs to provide all products. This likely requires updating the method signature to accept `IEnumerable<CatalogAggregate> allProducts`:

```csharp
// Update RefreshMargin or similar method
public async Task RefreshMarginAsync(
    IEnumerable<CatalogAggregate> allProducts, // NEW parameter
    IMarginCalculationService marginCalculationService,
    DateOnly dateFrom,
    DateOnly dateTo,
    CancellationToken cancellationToken = default)
{
    Margin = await marginCalculationService.GetMarginAsync(
        this,
        allProducts, // NEW argument
        dateFrom,
        dateTo,
        cancellationToken);
}
```

**Step 3: Verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeds or shows remaining callers

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs
git commit -m "feat: update CatalogAggregate to pass allProducts to GetMarginAsync"
```

---

## Task 12: Update Remaining Callers

**Files:**
- Modify: All files found in Task 10

**Step 1: For each caller file**

Update the call to `GetMarginAsync` to:
1. Load all products (via repository or existing method)
2. Pass allProducts as second parameter

Example pattern:

```csharp
// Before:
var margin = await _marginCalculationService.GetMarginAsync(
    product, dateFrom, dateTo, cancellationToken);

// After:
var allProducts = await _catalogRepository.GetAllAsync(cancellationToken);
var margin = await _marginCalculationService.GetMarginAsync(
    product, allProducts, dateFrom, dateTo, cancellationToken);
```

**Step 2: Verify compilation after each update**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeds

**Step 3: Commit each update**

```bash
git add [modified file]
git commit -m "feat: update [caller] to pass allProducts to GetMarginAsync"
```

---

## Task 13: Write Unit Test - Company-Wide CP Calculation

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs` (if doesn't exist)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs` (if exists)

**Step 1: Write failing test for company-wide CP calculation**

```csharp
[Fact]
public async Task CalculateCompanyWideProducedCP_WithMultipleProducts_AggregatesCorrectly()
{
    // Arrange
    var product1 = CreateTestProduct("PROD1", complexityPoints: 5.0, productionRecords: new[]
    {
        new { Date = new DateTime(2025, 1, 15), Amount = 100m }
    });

    var product2 = CreateTestProduct("PROD2", complexityPoints: 8.0, productionRecords: new[]
    {
        new { Date = new DateTime(2025, 1, 20), Amount = 50m }
    });

    var allProducts = new[] { product1, product2 };
    var dateFrom = new DateOnly(2025, 1, 1);
    var dateTo = new DateOnly(2025, 1, 31);

    // Use reflection to access private method
    var method = typeof(MarginCalculationService)
        .GetMethod("CalculateCompanyWideProducedCPAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

    // Act
    var result = await (Task<Dictionary<DateTime, decimal>>)method.Invoke(
        _service, new object[] { allProducts, dateFrom, dateTo, CancellationToken.None });

    // Assert
    var jan2025 = new DateTime(2025, 1, 1);
    Assert.Contains(jan2025, result.Keys);

    // Expected: (100 * 5.0) + (50 * 8.0) = 500 + 400 = 900
    Assert.Equal(900m, result[jan2025]);
}

private CatalogAggregate CreateTestProduct(
    string productCode,
    double complexityPoints,
    IEnumerable<dynamic> productionRecords)
{
    // Helper to create test product with manufacture history
    // Implementation depends on your test utilities
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~CalculateCompanyWideProducedCP"`
Expected: FAIL (test infrastructure might need setup)

**Step 3: Fix test infrastructure if needed**

Add necessary mocks, test helpers, etc.

**Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~CalculateCompanyWideProducedCP"`
Expected: PASS

**Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs
git commit -m "test: add unit test for company-wide CP calculation"
```

---

## Task 14: Write Unit Test - M1_A Calculation

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs`

**Step 1: Write test for M1_A with full 12-month data**

```csharp
[Fact]
public void CalculateM1_A_WithFullYearData_ReturnsCorrectBaseline()
{
    // Arrange
    var productCP = 5.0;

    // Company-wide produced CP for 12 months
    var companyWideProducedCP = new Dictionary<DateTime, decimal>();
    for (int i = 1; i <= 12; i++)
    {
        companyWideProducedCP[new DateTime(2024, i, 1)] = 1000m; // 1000 CP per month
    }

    // M1 costs for 12 months
    var m1Costs = new List<MonthlyCost>();
    for (int i = 1; i <= 12; i++)
    {
        m1Costs.Add(new MonthlyCost
        {
            Month = new DateTime(2024, i, 1),
            Cost = 10000m // 10,000 CZK per month
        });
    }

    var dateFrom = new DateOnly(2024, 12, 1);
    var dateTo = new DateOnly(2024, 12, 31);

    // Use reflection to access private method
    var method = typeof(MarginCalculationService)
        .GetMethod("CalculateM1_A_PerMonth",
            BindingFlags.NonPublic | BindingFlags.Instance);

    // Act
    var result = (Dictionary<DateTime, decimal>)method.Invoke(
        _service, new object[] { productCP, companyWideProducedCP, m1Costs, dateFrom, dateTo });

    // Assert
    var dec2024 = new DateTime(2024, 12, 1);
    Assert.Contains(dec2024, result.Keys);

    // Expected: cost_per_CP = (10,000 * 12) / (1,000 * 12) = 120,000 / 12,000 = 10
    // M1_A = 5.0 * 10 = 50
    Assert.Equal(50m, result[dec2024]);
}
```

**Step 2: Run test**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~CalculateM1_A"`
Expected: PASS

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs
git commit -m "test: add unit test for M1_A baseline calculation"
```

---

## Task 15: Write Unit Test - M1_B Calculation

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs`

**Step 1: Write test for M1_B when product is produced**

```csharp
[Fact]
public void CalculateM1_B_WhenProductProduced_ReturnsActualCost()
{
    // Arrange
    var product = CreateTestProduct("PROD1", complexityPoints: 5.0, productionRecords: new[]
    {
        new { Date = new DateTime(2025, 1, 15), Amount = 100m }
    });

    var productCP = 5.0;

    var companyWideProducedCP = new Dictionary<DateTime, decimal>
    {
        { new DateTime(2025, 1, 1), 800m } // Total company CP for January
    };

    var m1Costs = new List<MonthlyCost>
    {
        new MonthlyCost { Month = new DateTime(2025, 1, 1), Cost = 8000m }
    };

    // Use reflection to access private method
    var method = typeof(MarginCalculationService)
        .GetMethod("CalculateM1_B_PerMonth",
            BindingFlags.NonPublic | BindingFlags.Instance);

    // Act
    var result = (Dictionary<DateTime, decimal?>)method.Invoke(
        _service, new object[] { product, productCP, companyWideProducedCP, m1Costs });

    // Assert
    var jan2025 = new DateTime(2025, 1, 1);
    Assert.Contains(jan2025, result.Keys);
    Assert.NotNull(result[jan2025]);

    // Expected: M1_B_per_CP = 8000 / 800 = 10
    // M1_B = 5.0 * 10 = 50
    Assert.Equal(50m, result[jan2025]!.Value);
}

[Fact]
public void CalculateM1_B_WhenProductNotProduced_ReturnsNull()
{
    // Arrange
    var product = CreateTestProduct("PROD1", complexityPoints: 5.0, productionRecords: Array.Empty<dynamic>());

    var productCP = 5.0;

    var companyWideProducedCP = new Dictionary<DateTime, decimal>
    {
        { new DateTime(2025, 1, 1), 800m }
    };

    var m1Costs = new List<MonthlyCost>
    {
        new MonthlyCost { Month = new DateTime(2025, 1, 1), Cost = 8000m }
    };

    // Use reflection
    var method = typeof(MarginCalculationService)
        .GetMethod("CalculateM1_B_PerMonth",
            BindingFlags.NonPublic | BindingFlags.Instance);

    // Act
    var result = (Dictionary<DateTime, decimal?>)method.Invoke(
        _service, new object[] { product, productCP, companyWideProducedCP, m1Costs });

    // Assert
    var jan2025 = new DateTime(2025, 1, 1);
    Assert.Contains(jan2025, result.Keys);
    Assert.Null(result[jan2025]);
}
```

**Step 2: Run tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~CalculateM1_B"`
Expected: PASS

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs
git commit -m "test: add unit tests for M1_B actual cost calculation"
```

---

## Task 16: Write Integration Test - Full Margin Calculation

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs`

**Step 1: Write integration test with real repositories**

```csharp
[Fact]
public async Task GetMarginAsync_WithRealData_CalculatesM1Correctly()
{
    // Arrange
    var product = await SetupTestProductWithHistory();
    var allProducts = await _catalogRepository.GetAllAsync(CancellationToken.None);

    var dateFrom = new DateOnly(2024, 1, 1);
    var dateTo = new DateOnly(2024, 12, 31);

    // Act
    var result = await _service.GetMarginAsync(
        product, allProducts, dateFrom, dateTo, CancellationToken.None);

    // Assert
    Assert.NotNull(result);
    Assert.NotEmpty(result.MonthlyData);

    foreach (var monthData in result.MonthlyData)
    {
        Assert.NotNull(monthData.M1_A); // M1_A always present
        // M1_B may be null if product not produced that month
    }

    // Verify averages
    Assert.NotNull(result.Averages.M1_A);
    Assert.NotNull(result.Averages.M1_B);
}
```

**Step 2: Run test**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetMarginAsync_WithRealData"`
Expected: PASS

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs
git commit -m "test: add integration test for full M1 margin calculation"
```

---

## Task 17: Write Specification Compliance Test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs`

**Step 1: Write test verifying long-term M1_B approximates M1_A**

```csharp
[Fact]
public async Task LongTermAverage_M1_B_ApproximatesM1_A()
{
    // Arrange - 12 months of consistent production
    var product = await SetupTestProductWithConsistentProduction();
    var allProducts = new[] { product };

    var dateFrom = new DateOnly(2024, 1, 1);
    var dateTo = new DateOnly(2024, 12, 31);

    // Act
    var result = await _service.GetMarginAsync(
        product, allProducts, dateFrom, dateTo, CancellationToken.None);

    // Assert
    var avgM1_A = result.Averages.M1_A.CostLevel;
    var avgM1_B = result.Averages.M1_B.CostLevel;

    // Spec: "long-term average M1_B ≈ M1_A"
    // Allow 5% tolerance
    var percentDifference = Math.Abs(avgM1_A - avgM1_B) / avgM1_A * 100;
    Assert.True(percentDifference < 5,
        $"M1_B average ({avgM1_B}) should approximate M1_A average ({avgM1_A}) within 5%");
}
```

**Step 2: Run test**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~LongTermAverage"`
Expected: PASS

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs
git commit -m "test: add spec compliance test for M1_B ≈ M1_A over time"
```

---

## Task 18: Run Full Test Suite

**Files:**
- N/A (verification task)

**Step 1: Run all tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests`
Expected: All tests PASS

**Step 2: If failures occur**

Fix failing tests one by one, committing each fix:
```bash
git add [fixed file]
git commit -m "fix: [description of fix]"
```

**Step 3: No commit (verification only)**

---

## Task 19: Build Backend

**Files:**
- N/A (verification task)

**Step 1: Clean and rebuild**

Run: `dotnet clean backend/Anela.Heblo.sln && dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeds with 0 errors

**Step 2: If build errors occur**

Fix each error, committing fixes:
```bash
git add [fixed file]
git commit -m "fix: [description of build fix]"
```

**Step 3: No commit (verification only)**

---

## Task 20: Update Documentation

**Files:**
- Modify: `docs/features/M1_margin_calculation_design.md`

**Step 1: Add implementation completion note**

Add to top of design document:

```markdown
**Status:** ✅ Implemented (2025-12-19)
**Implementation Commit Range:** [first-commit]..[last-commit]
```

**Step 2: Commit documentation update**

```bash
git add docs/features/M1_margin_calculation_design.md
git commit -m "docs: mark M1 margin calculation as implemented"
```

---

## Task 21: Final Verification

**Files:**
- N/A (verification task)

**Step 1: Verify all changes committed**

Run: `git status`
Expected: Working directory clean

**Step 2: Review commit history**

Run: `git log --oneline -20`
Expected: Clean, descriptive commit messages following conventional commits

**Step 3: Final build and test**

Run: `dotnet build backend/Anela.Heblo.sln && dotnet test backend/test/Anela.Heblo.Tests`
Expected: Build succeeds, all tests pass

**Step 4: Done! 🎉**

---

## Notes

- **Follow @superpowers:test-driven-development** when writing tests
- **Follow @superpowers:verification-before-completion** before claiming tasks done
- **Use @superpowers:systematic-debugging** if tests fail unexpectedly
- Each task should take 2-5 minutes
- Commit frequently with descriptive messages
- If stuck, use @superpowers:brainstorming to explore alternatives
