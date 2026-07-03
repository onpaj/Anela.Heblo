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
