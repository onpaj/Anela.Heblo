# Extract Margin Aggregation and Sorting Logic from GetProductMarginSummaryHandler — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `GetProductMarginSummaryHandler`'s two embedded business-logic methods — `CalculateGroupMarginData` and `ApplySorting` — into dedicated, independently testable DI services (`IMarginCalculator.GetGroupAggregatedMarginData` and a new `ITopProductSorter`), with zero behavior change.

**Architecture:** Pure internal refactor within `Anela.Heblo.Application/Features/Analytics`. `GroupMarginData` becomes a public class in its own file in `Services/`, alongside a verbatim-moved aggregation method added to the existing `IMarginCalculator`/`MarginCalculator`. Sorting is extracted into a new `ITopProductSorter`/`TopProductSorter` service registered as `Scoped` in `AnalyticsModule.cs`, following the exact pattern already used by `IMonthlyBreakdownGenerator`. `GetProductMarginSummaryHandler` shrinks to orchestration only, gaining one new constructor dependency (`ITopProductSorter`).

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions (confirmed from `GetProductMarginSummaryHandlerTests.cs` and `MarginCalculatorTests.cs` — this repo does not use NUnit).

---

## File Map

- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — add `GetGroupAggregatedMarginData` to interface + implementation.
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/GroupMarginData.cs` — public class, moved verbatim from the handler file.
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs` — `ITopProductSorter` + `TopProductSorter`.
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — register `ITopProductSorter`.
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — remove both private methods and the `GroupMarginData` class; call the two new services; gain a constructor parameter.
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs` — add tests for `GetGroupAggregatedMarginData`.
- Create: `backend/test/Anela.Heblo.Tests/Features/Analytics/TopProductSorterTests.cs` — tests for `TopProductSorter.Sort`.
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — update the two handler-construction call sites and the mocked-calculator test's stubs.

Confirmed via repo-wide search (`grep -rn "new GetProductMarginSummaryHandler" backend/`): the **only** two call sites constructing this handler directly are both in `GetProductMarginSummaryHandlerTests.cs` (constructor at the top of the test class, and inside `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator`). No other test file or production code constructs it directly (MediatR resolves it via DI in production).

---

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

### task: wire-group-margin-aggregator-into-handler

Removes the now-redundant `CalculateGroupMarginData` private method and the internal `GroupMarginData` class from `GetProductMarginSummaryHandler.cs`, and updates the call site in `GenerateTopProducts` to use `_marginCalculator.GetGroupAggregatedMarginData(products)` (added to `IMarginCalculator` in the previous task). Also updates the one test that mocks `IMarginCalculator` directly, so it stubs the new method rather than hitting a `NullReferenceException`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`

Context — `IMarginCalculator` (from the previous task) now exposes:

```csharp
public interface IMarginCalculator
{
    Task<MarginCalculationResult> CalculateAsync(...);
    string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode);
    string GetGroupDisplayName(string groupKey, ProductGroupingMode groupingMode, List<AnalyticsProduct> products);
    decimal GetMarginAmountForLevel(AnalyticsProduct product, MarginLevel marginLevel);
    AnalysisMarginData CalculateForProduct(AnalyticsProduct product, IEnumerable<SalesDataPoint> salesInPeriod);
    GroupMarginData GetGroupAggregatedMarginData(List<AnalyticsProduct> products);
}
```

- [ ] **Step 1: Write the failing test (mocked-calculator stub)**

The existing test `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator` in `GetProductMarginSummaryHandlerTests.cs` uses a `Mock<IMarginCalculator>`. Once `GenerateTopProducts` starts calling `_marginCalculator.GetGroupAggregatedMarginData(products)` (this task's next step), the unstubbed mock would return `null` and the handler would throw a `NullReferenceException` when reading `groupData.M0Amount`. Add a stub for it now, in the same test, right after the existing `GetGroupDisplayName` stub (find this block around line 234–239):

```csharp
        marginCalculatorMock
            .Setup(x => x.GetGroupDisplayName(
                It.IsAny<string>(),
                It.IsAny<ProductGroupingMode>(),
                It.IsAny<List<AnalyticsProduct>>()))
            .Returns<string, ProductGroupingMode, List<AnalyticsProduct>>((key, _, _) => key);
```

and change it to:

```csharp
        marginCalculatorMock
            .Setup(x => x.GetGroupDisplayName(
                It.IsAny<string>(),
                It.IsAny<ProductGroupingMode>(),
                It.IsAny<List<AnalyticsProduct>>()))
            .Returns<string, ProductGroupingMode, List<AnalyticsProduct>>((key, _, _) => key);

        marginCalculatorMock
            .Setup(x => x.GetGroupAggregatedMarginData(It.IsAny<List<AnalyticsProduct>>()))
            .Returns(new GroupMarginData
            {
                M0Amount = 50m,
                M1Amount = 50m,
                M2Amount = 50m,
                M0Percentage = 0m,
                M1Percentage = 0m,
                M2Percentage = 0m,
                SellingPrice = 100m,
                PurchasePrice = 50m
            });
```

(`GroupMarginData` is already visible here via the existing `using Anela.Heblo.Application.Features.Analytics.Services;` at the top of the test file.)

- [ ] **Step 2: Run tests to verify the target test still passes for now (baseline), then verify the handler doesn't yet use the new call site**

Run: `dotnet test --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"`

Expected at this point: PASS — the stub was added but the handler still calls the old private `CalculateGroupMarginData`, so this stub is currently unused (Moq doesn't fail on unused `Setup`s by default). This confirms the test file itself compiles correctly before the handler change. This is an intermediate checkpoint, not a red/green TDD step by itself — the actual behavior change is verified in Step 4.

- [ ] **Step 3: Update the handler**

In `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`:

Change the call site inside `GenerateTopProducts` from:

```csharp
                // Calculate aggregated margin data for the group
                var groupData = CalculateGroupMarginData(products);
```

to:

```csharp
                // Calculate aggregated margin data for the group
                var groupData = _marginCalculator.GetGroupAggregatedMarginData(products);
```

Delete the entire `CalculateGroupMarginData` private method (currently the method right after `GenerateTopProducts` and before `ApplySorting`):

```csharp
    /// <summary>
    /// Calculates aggregated margin data for a group of products
    /// </summary>
    private GroupMarginData CalculateGroupMarginData(List<AnalyticsProduct> products)
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

```

Delete the internal `GroupMarginData` class declared at the bottom of the file (after the closing brace of `GetProductMarginSummaryHandler`):

```csharp

/// <summary>
/// Helper class for aggregated margin data calculation
/// </summary>
internal class GroupMarginData
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

The file now ends right after the closing brace of `GetProductMarginSummaryHandler` (i.e. after the `CalculateTotalMarginForLevel` method and the class's final `}`), with no trailing class declaration.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"`

Expected: PASS — all tests in `GetProductMarginSummaryHandlerTests.cs` pass, including `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator` (now actually exercising the `GetGroupAggregatedMarginData` stub) and `Handle_ValidRequest_ReturnsCorrectResponse` (using the real `MarginCalculator`, verifying `TotalMargin` is unchanged at `3000m`).

Also run a full build to confirm nothing else references the deleted private method or internal class:

Run: `dotnet build`

Expected: Build succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs
git commit -m "refactor(analytics): use IMarginCalculator.GetGroupAggregatedMarginData in handler"
```

---

### task: add-top-product-sorter

Creates the new `ITopProductSorter`/`TopProductSorter` service as a verbatim move of the handler's current `ApplySorting` method, registers it in `AnalyticsModule.cs`, and adds a new test file covering all 13 named sort keys (both directions), the default (no `sortBy`) case, and the unrecognized-key fallback. This task does **not** touch the handler yet — `ApplySorting` continues to exist untouched in the handler until the next task.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/TopProductSorterTests.cs`

Context — `TopProductDto` (existing, in `Anela.Heblo.Application.Features.Analytics.Contracts`, unchanged by this refactor):

```csharp
public class TopProductDto
{
    public string GroupKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal TotalMargin { get; set; }
    public string ColorCode { get; set; } = string.Empty;
    public int Rank { get; set; }
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

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Features/Analytics/TopProductSorterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class TopProductSorterTests
{
    private readonly TopProductSorter _sorter = new();

    // Values are monotonically increasing Low -> Mid -> High across every sortable
    // field, and GroupKey/DisplayName are alphabetically ordered the same way, so
    // ascending order is always [Low, Mid, High] and descending is always
    // [High, Mid, Low], regardless of which field is being sorted on.
    private static List<TopProductDto> MakeProducts() => new()
    {
        new TopProductDto
        {
            GroupKey = "B-Mid",
            DisplayName = "B Product",
            TotalMargin = 200m,
            M0Amount = 20m,
            M1Amount = 25m,
            M2Amount = 30m,
            M0Percentage = 10m,
            M1Percentage = 15m,
            M2Percentage = 20m,
            SellingPrice = 150m,
            PurchasePrice = 70m
        },
        new TopProductDto
        {
            GroupKey = "A-Low",
            DisplayName = "A Product",
            TotalMargin = 100m,
            M0Amount = 10m,
            M1Amount = 15m,
            M2Amount = 20m,
            M0Percentage = 5m,
            M1Percentage = 10m,
            M2Percentage = 15m,
            SellingPrice = 100m,
            PurchasePrice = 50m
        },
        new TopProductDto
        {
            GroupKey = "C-High",
            DisplayName = "C Product",
            TotalMargin = 300m,
            M0Amount = 30m,
            M1Amount = 35m,
            M2Amount = 40m,
            M0Percentage = 15m,
            M1Percentage = 20m,
            M2Percentage = 25m,
            SellingPrice = 200m,
            PurchasePrice = 90m
        }
    };

    [Theory]
    [InlineData("groupkey")]
    [InlineData("productcode")]
    [InlineData("displayname")]
    [InlineData("productname")]
    [InlineData("totalmargin")]
    [InlineData("m0amount")]
    [InlineData("m1amount")]
    [InlineData("m2amount")]
    [InlineData("m0percentage")]
    [InlineData("m1percentage")]
    [InlineData("m2percentage")]
    [InlineData("sellingprice")]
    [InlineData("purchaseprice")]
    public void Sort_NamedKey_Ascending_OrdersLowMidHigh(string sortBy)
    {
        var result = _sorter.Sort(MakeProducts(), sortBy, sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }

    [Theory]
    [InlineData("groupkey")]
    [InlineData("productcode")]
    [InlineData("displayname")]
    [InlineData("productname")]
    [InlineData("totalmargin")]
    [InlineData("m0amount")]
    [InlineData("m1amount")]
    [InlineData("m2amount")]
    [InlineData("m0percentage")]
    [InlineData("m1percentage")]
    [InlineData("m2percentage")]
    [InlineData("sellingprice")]
    [InlineData("purchaseprice")]
    public void Sort_NamedKey_Descending_OrdersHighMidLow(string sortBy)
    {
        var result = _sorter.Sort(MakeProducts(), sortBy, sortDescending: true);

        result.Select(p => p.GroupKey).Should().ContainInOrder("C-High", "B-Mid", "A-Low");
    }

    [Fact]
    public void Sort_NullSortBy_DefaultsToTotalMarginDescending()
    {
        var result = _sorter.Sort(MakeProducts(), null, sortDescending: true);

        result.Select(p => p.GroupKey).Should().ContainInOrder("C-High", "B-Mid", "A-Low");
    }

    [Fact]
    public void Sort_EmptySortBy_DefaultsToTotalMarginAscending()
    {
        var result = _sorter.Sort(MakeProducts(), "", sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }

    [Fact]
    public void Sort_WhitespaceSortBy_DefaultsToTotalMargin()
    {
        var result = _sorter.Sort(MakeProducts(), "   ", sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }

    [Fact]
    public void Sort_UnrecognizedSortBy_FallsBackToTotalMargin()
    {
        var result = _sorter.Sort(MakeProducts(), "not-a-real-key", sortDescending: true);

        result.Select(p => p.GroupKey).Should().ContainInOrder("C-High", "B-Mid", "A-Low");
    }

    [Fact]
    public void Sort_UppercaseSortByKey_IsCaseInsensitive()
    {
        var result = _sorter.Sort(MakeProducts(), "TOTALMARGIN", sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TopProductSorterTests"`

Expected: **build error** — `CS0246: The type or namespace name 'TopProductSorter' could not be found` (neither the interface nor the class exists yet).

- [ ] **Step 3: Create `TopProductSorter.cs`**

Create `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface ITopProductSorter
{
    List<TopProductDto> Sort(List<TopProductDto> products, string? sortBy, bool sortDescending);
}

/// <summary>
/// Extracted sorting logic for the top products list
/// </summary>
public class TopProductSorter : ITopProductSorter
{
    /// <summary>
    /// Applies sorting to the top products list
    /// </summary>
    public List<TopProductDto> Sort(List<TopProductDto> products, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by TotalMargin descending
            return sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList();
        }

        return sortBy.ToLower() switch
        {
            "groupkey" or "productcode" => sortDescending
                ? products.OrderByDescending(p => p.GroupKey).ToList()
                : products.OrderBy(p => p.GroupKey).ToList(),
            "displayname" or "productname" => sortDescending
                ? products.OrderByDescending(p => p.DisplayName).ToList()
                : products.OrderBy(p => p.DisplayName).ToList(),
            "totalmargin" => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList(),
            // M0-M2 margin levels - amounts
            "m0amount" => sortDescending
                ? products.OrderByDescending(p => p.M0Amount).ToList()
                : products.OrderBy(p => p.M0Amount).ToList(),
            "m1amount" => sortDescending
                ? products.OrderByDescending(p => p.M1Amount).ToList()
                : products.OrderBy(p => p.M1Amount).ToList(),
            "m2amount" => sortDescending
                ? products.OrderByDescending(p => p.M2Amount).ToList()
                : products.OrderBy(p => p.M2Amount).ToList(),
            // M0-M2 margin levels - percentages
            "m0percentage" => sortDescending
                ? products.OrderByDescending(p => p.M0Percentage).ToList()
                : products.OrderBy(p => p.M0Percentage).ToList(),
            "m1percentage" => sortDescending
                ? products.OrderByDescending(p => p.M1Percentage).ToList()
                : products.OrderBy(p => p.M1Percentage).ToList(),
            "m2percentage" => sortDescending
                ? products.OrderByDescending(p => p.M2Percentage).ToList()
                : products.OrderBy(p => p.M2Percentage).ToList(),
            // Pricing
            "sellingprice" => sortDescending
                ? products.OrderByDescending(p => p.SellingPrice).ToList()
                : products.OrderBy(p => p.SellingPrice).ToList(),
            "purchaseprice" => sortDescending
                ? products.OrderByDescending(p => p.PurchasePrice).ToList()
                : products.OrderBy(p => p.PurchasePrice).ToList(),
            _ => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList()
        };
    }
}
```

- [ ] **Step 4: Register the service in `AnalyticsModule.cs`**

In `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`, change:

```csharp
        services.AddScoped<TimeWindowParser>();
        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
```

to:

```csharp
        services.AddScoped<TimeWindowParser>();
        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
        services.AddScoped<ITopProductSorter, TopProductSorter>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TopProductSorterTests"`

Expected: PASS — all 30 test cases pass (13 keys × 2 directions = 26, plus 4 default/fallback/case-insensitivity tests).

Then run a full build to confirm the DI registration compiles:

Run: `dotnet build`

Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/TopProductSorterTests.cs
git commit -m "feat(analytics): add ITopProductSorter service"
```

---

### task: wire-top-product-sorter-into-handler

Adds `ITopProductSorter` as a new constructor dependency on `GetProductMarginSummaryHandler`, removes the now-redundant `ApplySorting` private method, and updates the call site in `GenerateTopProducts`. Updates both handler-construction call sites in `GetProductMarginSummaryHandlerTests.cs` to pass a real (non-mocked) `TopProductSorter` instance, matching the existing pattern for `MarginCalculator`/`MonthlyBreakdownGenerator`. This is the final task — after it, `GetProductMarginSummaryHandler.cs` contains only orchestration (`Handle`, `GenerateTopProducts`, `CalculateTotalMarginForLevel`).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`

Context — after the previous two tasks, the handler file currently reads (relevant parts):

```csharp
public class GetProductMarginSummaryHandler : IRequestHandler<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TimeWindowParser _timeWindowParser;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
        TimeWindowParser timeWindowParser)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
        _timeWindowParser = timeWindowParser;
    }
    // ... Handle(...) unchanged ...

    private List<TopProductDto> GenerateTopProducts(MarginCalculationResult calculationResult, ProductGroupingMode groupingMode, string? sortBy, bool sortDescending, MarginLevel marginLevel)
    {
        var topProductsWithData = calculationResult.GroupTotals
            .Select(kvp => { /* ... unchanged, uses _marginCalculator.GetGroupAggregatedMarginData(products) ... */ })
            .ToList();

        // Apply sorting
        var sortedProducts = ApplySorting(topProductsWithData, sortBy, sortDescending);

        // Add rank after sorting
        for (int i = 0; i < sortedProducts.Count; i++)
        {
            sortedProducts[i].Rank = i + 1;
        }

        return sortedProducts;
    }

    private List<TopProductDto> ApplySorting(List<TopProductDto> products, string? sortBy, bool sortDescending)
    {
        // ... the full 13-branch switch, unchanged from before this refactor started ...
    }

    private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, MarginLevel marginLevel)
    {
        return products.Sum(p =>
            (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)
            * _marginCalculator.GetMarginAmountForLevel(p, marginLevel));
    }
}
```

- [ ] **Step 1: Write the failing test (constructor signature)**

In `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`, add a field and wire it into the constructor. Change:

```csharp
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TimeWindowParser _timeWindowParser;
    private readonly GetProductMarginSummaryHandler _handler;

    public GetProductMarginSummaryHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _marginCalculator = new MarginCalculator();
        _monthlyBreakdownGenerator = new MonthlyBreakdownGenerator(_marginCalculator);
        var timeProvider = new FakeTimeProvider(FrozenNow);
        _timeWindowParser = new TimeWindowParser(timeProvider);
        _handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            _marginCalculator,
            _monthlyBreakdownGenerator,
            _timeWindowParser);
    }
```

to:

```csharp
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TopProductSorter _topProductSorter;
    private readonly TimeWindowParser _timeWindowParser;
    private readonly GetProductMarginSummaryHandler _handler;

    public GetProductMarginSummaryHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _marginCalculator = new MarginCalculator();
        _monthlyBreakdownGenerator = new MonthlyBreakdownGenerator(_marginCalculator);
        _topProductSorter = new TopProductSorter();
        var timeProvider = new FakeTimeProvider(FrozenNow);
        _timeWindowParser = new TimeWindowParser(timeProvider);
        _handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            _marginCalculator,
            _monthlyBreakdownGenerator,
            _topProductSorter,
            _timeWindowParser);
    }
```

Also update the second construction call site, inside `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator`. Change:

```csharp
        var handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            marginCalculatorMock.Object,
            monthlyBreakdownGeneratorMock.Object,
            _timeWindowParser);
```

to:

```csharp
        var handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            marginCalculatorMock.Object,
            monthlyBreakdownGeneratorMock.Object,
            _topProductSorter,
            _timeWindowParser);
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"`

Expected: **build error** — `CS1729: 'GetProductMarginSummaryHandler' does not contain a constructor that takes 5 arguments` (the handler's constructor still only takes 4 parameters).

- [ ] **Step 3: Update the handler**

In `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`, change the field declarations and constructor from:

```csharp
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TimeWindowParser _timeWindowParser;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
        TimeWindowParser timeWindowParser)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
        _timeWindowParser = timeWindowParser;
    }
```

to:

```csharp
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly ITopProductSorter _topProductSorter;
    private readonly TimeWindowParser _timeWindowParser;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
        ITopProductSorter topProductSorter,
        TimeWindowParser timeWindowParser)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
        _topProductSorter = topProductSorter;
        _timeWindowParser = timeWindowParser;
    }
```

Change the call site inside `GenerateTopProducts` from:

```csharp
        // Apply sorting
        var sortedProducts = ApplySorting(topProductsWithData, sortBy, sortDescending);
```

to:

```csharp
        // Apply sorting
        var sortedProducts = _topProductSorter.Sort(topProductsWithData, sortBy, sortDescending);
```

Delete the entire `ApplySorting` private method (currently between `GenerateTopProducts` and `CalculateTotalMarginForLevel`):

```csharp
    /// <summary>
    /// Applies sorting to the top products list
    /// </summary>
    private List<TopProductDto> ApplySorting(List<TopProductDto> products, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by TotalMargin descending
            return sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList();
        }

        return sortBy.ToLower() switch
        {
            "groupkey" or "productcode" => sortDescending
                ? products.OrderByDescending(p => p.GroupKey).ToList()
                : products.OrderBy(p => p.GroupKey).ToList(),
            "displayname" or "productname" => sortDescending
                ? products.OrderByDescending(p => p.DisplayName).ToList()
                : products.OrderBy(p => p.DisplayName).ToList(),
            "totalmargin" => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList(),
            // M0-M2 margin levels - amounts
            "m0amount" => sortDescending
                ? products.OrderByDescending(p => p.M0Amount).ToList()
                : products.OrderBy(p => p.M0Amount).ToList(),
            "m1amount" => sortDescending
                ? products.OrderByDescending(p => p.M1Amount).ToList()
                : products.OrderBy(p => p.M1Amount).ToList(),
            "m2amount" => sortDescending
                ? products.OrderByDescending(p => p.M2Amount).ToList()
                : products.OrderBy(p => p.M2Amount).ToList(),
            // M0-M2 margin levels - percentages
            "m0percentage" => sortDescending
                ? products.OrderByDescending(p => p.M0Percentage).ToList()
                : products.OrderBy(p => p.M0Percentage).ToList(),
            "m1percentage" => sortDescending
                ? products.OrderByDescending(p => p.M1Percentage).ToList()
                : products.OrderBy(p => p.M1Percentage).ToList(),
            "m2percentage" => sortDescending
                ? products.OrderByDescending(p => p.M2Percentage).ToList()
                : products.OrderBy(p => p.M2Percentage).ToList(),
            // Pricing
            "sellingprice" => sortDescending
                ? products.OrderByDescending(p => p.SellingPrice).ToList()
                : products.OrderBy(p => p.SellingPrice).ToList(),
            "purchaseprice" => sortDescending
                ? products.OrderByDescending(p => p.PurchasePrice).ToList()
                : products.OrderBy(p => p.PurchasePrice).ToList(),
            _ => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList()
        };
    }

```

The handler class now contains only three methods: `Handle`, `GenerateTopProducts`, and `CalculateTotalMarginForLevel`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"`

Expected: PASS — all tests pass, including `Handle_ValidRequest_ReturnsCorrectResponse` (`TotalMargin` still `3000m`, `TopProducts.Count` still `2`) and `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator` (now using the real `TopProductSorter` for sorting).

- [ ] **Step 5: Run the full test suite and build**

Run: `dotnet build`

Expected: Build succeeds with no errors or warnings about unused members.

Run: `dotnet test`

Expected: PASS — full backend test suite green (no regressions introduced by the constructor signature change or the removed methods).

Verify the line-count reduction (FR-3 acceptance criterion — file should be well under 150 lines, down from the original 242):

Run: `wc -l backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`

Expected: roughly 130–140 lines (no `GroupMarginData` class, no `CalculateGroupMarginData` method, no `ApplySorting` method).

- [ ] **Step 6: Run `dotnet format` to match repo formatting conventions**

Run: `dotnet format`

Expected: No unexpected changes beyond whitespace/using-order in the files touched by this plan (per project validation rules in `CLAUDE.md`).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs
git commit -m "refactor(analytics): use ITopProductSorter in GetProductMarginSummaryHandler"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (move `CalculateGroupMarginData` into `IMarginCalculator.GetGroupAggregatedMarginData`, move `GroupMarginData` to `Services/`, make it `public`) → covered by `add-group-margin-aggregator` (creates `GroupMarginData.cs`, extends `IMarginCalculator`/`MarginCalculator`, adds direct unit tests) and `wire-group-margin-aggregator-into-handler` (removes the private method + internal class from the handler, updates the call site, fixes the one affected mock).
- FR-2 (extract `ApplySorting` into `ITopProductSorter`/`TopProductSorter`, register in `AnalyticsModule.cs`, new ctor dependency) → covered by `add-top-product-sorter` (new service + DI registration + full-coverage tests for all 13 keys × 2 directions + default + fallback + case-insensitivity) and `wire-top-product-sorter-into-handler` (ctor change, removes `ApplySorting`, updates both test call sites).
- FR-3 (handler reduced to orchestration only, no API/contract changes) → verified in `wire-top-product-sorter-into-handler` Step 5 (line-count check) and Step 4 (full test suite green, confirming `GetProductMarginSummaryRequest`/`Response` untouched — no task in this plan touches those files).
- NFR-1/NFR-2 (no performance or security impact) → naturally satisfied; no task changes algorithmic behavior, only code location — confirmed by verbatim-move task steps plus passing pre-existing assertions (`TotalMargin.Should().Be(3000m)` etc.) that pin the exact numeric output.
- Dependencies section's requirement to confirm no other call sites construct the handler directly → confirmed via `grep -rn "new GetProductMarginSummaryHandler" backend/`, documented in the File Map section; only the two known call sites in `GetProductMarginSummaryHandlerTests.cs` exist.
- Out of Scope items (`CalculateTotalMarginForLevel`, other Analytics handlers, Option A static helper) → correctly left untouched; no task modifies `GetProductMarginAnalysisHandler.cs` or `GetMarginReportHandler.cs`, and `TopProductSorter` is DI-registered (Option B), not static.

**2. Placeholder scan:** No "TBD"/"TODO"/"similar to task N" placeholders — every step shows complete, copy-pasteable code. No gaps found.

**3. Type consistency:** `GroupMarginData` (8 decimal properties) is identical across its creation in `add-group-margin-aggregator` and its use in `wire-group-margin-aggregator-into-handler`. `ITopProductSorter.Sort(List<TopProductDto>, string?, bool) : List<TopProductDto>` signature is identical across its creation in `add-top-product-sorter` and its use in `wire-top-product-sorter-into-handler`. Constructor parameter name `topProductSorter` and field `_topProductSorter` are consistent between the handler and its test fixture. No naming drift detected.
