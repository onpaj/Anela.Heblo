### task: add-group-margin-aggregator

Adds `GetGroupAggregatedMarginData` to `IMarginCalculator`/`MarginCalculator` as a verbatim move of the handler's current `CalculateGroupMarginData` logic, and creates the public `GroupMarginData` class in its own file. This task does **not** touch the handler yet — the handler's private `CalculateGroupMarginData` method and its internal `GroupMarginData` class continue to exist untouched until the next task, so the codebase compiles and all existing tests keep passing at every step of this task.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/GroupMarginData.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these three test methods to `backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs`, right after the existing `CalculateForProduct_EnumeratesSequenceExactlyOnce` test (before the closing `}` of the class, i.e. before the `GetSalesWithCounter` helper — insert above it):

```csharp
    [Fact]
    public void GetGroupAggregatedMarginData_EmptyList_ReturnsDefaultGroupMarginData()
    {
        var result = _calculator.GetGroupAggregatedMarginData(new List<AnalyticsProduct>());

        result.M0Amount.Should().Be(0m);
        result.M1Amount.Should().Be(0m);
        result.M2Amount.Should().Be(0m);
        result.M0Percentage.Should().Be(0m);
        result.M1Percentage.Should().Be(0m);
        result.M2Percentage.Should().Be(0m);
        result.SellingPrice.Should().Be(0m);
        result.PurchasePrice.Should().Be(0m);
    }

    [Fact]
    public void GetGroupAggregatedMarginData_ZeroTotalSales_ReturnsSimpleAverage()
    {
        var products = new List<AnalyticsProduct>
        {
            new()
            {
                ProductCode = "A",
                ProductName = "Product A",
                Type = AnalyticsProductType.Product,
                MarginAmount = 10m,
                M0Amount = 10m,
                M1Amount = 20m,
                M2Amount = 30m,
                M0Percentage = 5m,
                M1Percentage = 15m,
                M2Percentage = 25m,
                SellingPrice = 100m,
                PurchasePrice = 40m,
                SalesHistory = []
            },
            new()
            {
                ProductCode = "B",
                ProductName = "Product B",
                Type = AnalyticsProductType.Product,
                MarginAmount = 20m,
                M0Amount = 30m,
                M1Amount = 40m,
                M2Amount = 50m,
                M0Percentage = 15m,
                M1Percentage = 25m,
                M2Percentage = 35m,
                SellingPrice = 200m,
                PurchasePrice = 60m,
                SalesHistory = []
            }
        };

        var result = _calculator.GetGroupAggregatedMarginData(products);

        result.M0Amount.Should().Be(20m); // (10+30)/2
        result.M1Amount.Should().Be(30m); // (20+40)/2
        result.M2Amount.Should().Be(40m); // (30+50)/2
        result.M0Percentage.Should().Be(10m); // (5+15)/2
        result.M1Percentage.Should().Be(20m); // (15+25)/2
        result.M2Percentage.Should().Be(30m); // (25+35)/2
        result.SellingPrice.Should().Be(150m); // (100+200)/2
        result.PurchasePrice.Should().Be(50m); // (40+60)/2
    }

    [Fact]
    public void GetGroupAggregatedMarginData_MultipleProductsWithSales_ReturnsWeightedAverage()
    {
        var products = new List<AnalyticsProduct>
        {
            new()
            {
                ProductCode = "A",
                ProductName = "Product A",
                Type = AnalyticsProductType.Product,
                MarginAmount = 10m,
                M0Amount = 10m,
                M1Amount = 20m,
                M2Amount = 30m,
                M0Percentage = 5m,
                M1Percentage = 15m,
                M2Percentage = 25m,
                SellingPrice = 100m,
                PurchasePrice = 40m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = default, AmountB2B = 10, AmountB2C = 0 } // 10 units
                }
            },
            new()
            {
                ProductCode = "B",
                ProductName = "Product B",
                Type = AnalyticsProductType.Product,
                MarginAmount = 20m,
                M0Amount = 30m,
                M1Amount = 40m,
                M2Amount = 50m,
                M0Percentage = 15m,
                M1Percentage = 25m,
                M2Percentage = 35m,
                SellingPrice = 200m,
                PurchasePrice = 60m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = default, AmountB2B = 30, AmountB2C = 0 } // 30 units
                }
            }
        };

        // totalSales = 40 units; weight A = 10/40, weight B = 30/40
        var result = _calculator.GetGroupAggregatedMarginData(products);

        result.M0Amount.Should().Be(25m); // (10*10 + 30*30) / 40
        result.M1Amount.Should().Be(35m); // (20*10 + 40*30) / 40
        result.M2Amount.Should().Be(45m); // (30*10 + 50*30) / 40
        result.M0Percentage.Should().Be(12.5m); // (5*10 + 15*30) / 40
        result.M1Percentage.Should().Be(22.5m); // (15*10 + 25*30) / 40
        result.M2Percentage.Should().Be(32.5m); // (25*10 + 35*30) / 40
        result.SellingPrice.Should().Be(175m); // (100*10 + 200*30) / 40
        result.PurchasePrice.Should().Be(55m); // (40*10 + 60*30) / 40
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MarginCalculatorTests" --project backend/test/Anela.Heblo.Tests` (or simply `dotnet test --filter "FullyQualifiedName~MarginCalculatorTests"` from the repo root, where `Anela.Heblo.sln` lives).

Expected: **build error** — `CS1061: 'MarginCalculator' does not contain a definition for 'GetGroupAggregatedMarginData'` (the method doesn't exist yet).

- [ ] **Step 3: Create `GroupMarginData.cs`**

Create `backend/src/Anela.Heblo.Application/Features/Analytics/Services/GroupMarginData.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Analytics.Services;

/// <summary>
/// Helper class for aggregated margin data calculation
/// </summary>
public class GroupMarginData
{
    public decimal M0Amount { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M2Amount { get; set; }
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
}
```

- [ ] **Step 4: Add the method to `IMarginCalculator` and implement it in `MarginCalculator`**

In `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`, change the end of the `IMarginCalculator` interface from:

```csharp
    AnalysisMarginData CalculateForProduct(
        AnalyticsProduct product,
        IEnumerable<SalesDataPoint> salesInPeriod);
}
```

to:

```csharp
    AnalysisMarginData CalculateForProduct(
        AnalyticsProduct product,
        IEnumerable<SalesDataPoint> salesInPeriod);

    /// <summary>
    /// Calculates aggregated margin data for a group of products, weighted by sales volume.
    /// Falls back to a simple average when the group has zero total sales.
    /// </summary>
    GroupMarginData GetGroupAggregatedMarginData(List<AnalyticsProduct> products);
}
```

Then change the end of the `MarginCalculator` class from:

```csharp
        return new AnalysisMarginData
        {
            Revenue = revenue,
            Cost = cost,
            Margin = margin,
            MarginPercentage = marginPercentage,
            UnitsSold = units,
        };
    }
}
```

to:

```csharp
        return new AnalysisMarginData
        {
            Revenue = revenue,
            Cost = cost,
            Margin = margin,
            MarginPercentage = marginPercentage,
            UnitsSold = units,
        };
    }

    /// <summary>
    /// Calculates aggregated margin data for a group of products
    /// </summary>
    public GroupMarginData GetGroupAggregatedMarginData(List<AnalyticsProduct> products)
    {
        if (!products.Any())
            return new GroupMarginData();

        // For groups, we calculate weighted averages based on sales volume
        var totalSales = products.Sum(p => p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C));

        if (totalSales == 0)
        {
            // If no sales, use simple average
            return new GroupMarginData
            {
                M0Amount = products.Average(p => p.M0Amount),
                M1Amount = products.Average(p => p.M1Amount),
                M2Amount = products.Average(p => p.M2Amount),
                M0Percentage = products.Average(p => p.M0Percentage),
                M1Percentage = products.Average(p => p.M1Percentage),
                M2Percentage = products.Average(p => p.M2Percentage),
                SellingPrice = products.Average(p => p.SellingPrice),
                PurchasePrice = products.Average(p => p.PurchasePrice)
            };
        }

        // Weighted average by sales volume
        return new GroupMarginData
        {
            M0Amount = products.Sum(p => p.M0Amount * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M1Amount = products.Sum(p => p.M1Amount * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M2Amount = products.Sum(p => p.M2Amount * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M0Percentage = products.Sum(p => p.M0Percentage * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M1Percentage = products.Sum(p => p.M1Percentage * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            M2Percentage = products.Sum(p => p.M2Percentage * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            SellingPrice = products.Sum(p => p.SellingPrice * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales,
            PurchasePrice = products.Sum(p => p.PurchasePrice * (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)) / (decimal)totalSales
        };
    }
}
```

Note: `GroupMarginData` and `AnalyticsProduct` are already visible in this file without new `using` statements — `GroupMarginData` is in the same namespace (`Anela.Heblo.Application.Features.Analytics.Services`), and `AnalyticsProduct`/`SalesDataPoint` come from the existing `using Anela.Heblo.Domain.Features.Analytics;` at the top of the file.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MarginCalculatorTests"`

Expected: PASS — all `MarginCalculatorTests` tests pass, including the 3 new ones.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/GroupMarginData.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs
git commit -m "feat(analytics): add GetGroupAggregatedMarginData to IMarginCalculator"
```

---
