### task: extract-filteritems

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests for `FilterItems`**

Append to `StockAnalysisCalculatorTests.cs`:

```csharp
    private static StockAnalysisItemDto MakeItem(StockSeverity severity, bool isConfigured = true) =>
        new()
        {
            ProductCode = "MAT001",
            ProductName = "Test",
            ProductNameNormalized = "test",
            ProductType = "Material",
            Severity = severity,
            IsConfigured = isConfigured,
        };

    [Theory]
    [InlineData(StockStatusFilter.Critical, StockSeverity.Critical, true)]
    [InlineData(StockStatusFilter.Critical, StockSeverity.Low, false)]
    [InlineData(StockStatusFilter.Low, StockSeverity.Low, true)]
    [InlineData(StockStatusFilter.Optimal, StockSeverity.Optimal, true)]
    [InlineData(StockStatusFilter.Overstocked, StockSeverity.Overstocked, true)]
    [InlineData(StockStatusFilter.NotConfigured, StockSeverity.NotConfigured, true)]
    [InlineData(StockStatusFilter.All, StockSeverity.Critical, true)]
    public void FilterItems_StatusFilter_IncludesOnlyMatchingSeverity(StockStatusFilter filter, StockSeverity severity, bool expectedIncluded)
    {
        var items = new List<StockAnalysisItemDto> { MakeItem(severity) };
        var request = new GetPurchaseStockAnalysisRequest { StockStatus = filter, PageNumber = 1, PageSize = 10 };

        var result = _calculator.FilterItems(items, request);

        result.Should().HaveCount(expectedIncluded ? 1 : 0);
    }

    [Fact]
    public void FilterItems_OnlyConfiguredTrue_ExcludesUnconfiguredItems()
    {
        var items = new List<StockAnalysisItemDto> { MakeItem(StockSeverity.Optimal, isConfigured: false) };
        var request = new GetPurchaseStockAnalysisRequest { OnlyConfigured = true, StockStatus = StockStatusFilter.All, PageNumber = 1, PageSize = 10 };

        var result = _calculator.FilterItems(items, request);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FilterItems_OnlyConfiguredTrue_KeepsConfiguredItemsMatchingStatus()
    {
        var items = new List<StockAnalysisItemDto> { MakeItem(StockSeverity.Critical, isConfigured: true) };
        var request = new GetPurchaseStockAnalysisRequest { OnlyConfigured = true, StockStatus = StockStatusFilter.Critical, PageNumber = 1, PageSize = 10 };

        var result = _calculator.FilterItems(items, request);

        result.Should().HaveCount(1);
    }
```

Add `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;` to the top of `StockAnalysisCalculatorTests.cs` for `GetPurchaseStockAnalysisRequest`/`StockStatusFilter` if not already present via another using (check `GetPurchaseStockAnalysisHandlerTests.cs` imports for the exact namespace).

- [ ] **Step 2: Run to verify it fails to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockAnalysisCalculatorTests"`
Expected: build error — `FilterItems` does not exist on `IStockAnalysisCalculator`/`StockAnalysisCalculator`.

- [ ] **Step 3: Add `FilterItems` to the interface**

Edit `IStockAnalysisCalculator.cs`, add:
```csharp
    /// <summary>
    /// Filters analyzed stock items by configured-status and stock-status request filters.
    /// </summary>
    /// <param name="items">Analyzed items to filter</param>
    /// <param name="request">The request carrying the filter criteria</param>
    /// <returns>The filtered item list</returns>
    List<StockAnalysisItemDto> FilterItems(List<StockAnalysisItemDto> items, GetPurchaseStockAnalysisRequest request);
```

Add `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;` to `IStockAnalysisCalculator.cs` for `GetPurchaseStockAnalysisRequest`.

- [ ] **Step 4: Implement `FilterItems` in `StockAnalysisCalculator`, moved verbatim from the handler's `ShouldIncludeItem`**

Add to `StockAnalysisCalculator.cs`, moved from `GetPurchaseStockAnalysisHandler.cs` lines 166–182 (wrapping the existing per-item predicate in a `.Where()`, matching `ItemFilterService.FilterItems`'s shape):

```csharp
    public List<StockAnalysisItemDto> FilterItems(List<StockAnalysisItemDto> items, GetPurchaseStockAnalysisRequest request)
    {
        return items.Where(item => ShouldIncludeItem(item, request)).ToList();
    }

    private bool ShouldIncludeItem(StockAnalysisItemDto item, GetPurchaseStockAnalysisRequest request)
    {
        if (request.OnlyConfigured && !item.IsConfigured)
        {
            return false;
        }

        return request.StockStatus switch
        {
            StockStatusFilter.Critical => item.Severity == StockSeverity.Critical,
            StockStatusFilter.Low => item.Severity == StockSeverity.Low,
            StockStatusFilter.Optimal => item.Severity == StockSeverity.Optimal,
            StockStatusFilter.Overstocked => item.Severity == StockSeverity.Overstocked,
            StockStatusFilter.NotConfigured => item.Severity == StockSeverity.NotConfigured,
            _ => true
        };
    }
```

Add `using System.Linq;` if not already implicitly available (check top-level implicit usings in the `.csproj` — if `ImplicitUsings` is enabled, no import is needed).

- [ ] **Step 5: Remove `ShouldIncludeItem` from the handler and delegate**

Edit `GetPurchaseStockAnalysisHandler.cs`:
- Delete the private `ShouldIncludeItem` method (lines 166–182).
- Change:
```csharp
        var analysisItems = allAnalysisItems
            .Where(item => ShouldIncludeItem(item, request))
            .ToList();
```
to:
```csharp
        var analysisItems = _stockAnalysisCalculator.FilterItems(allAnalysisItems, request);
```

- [ ] **Step 6: Run tests to verify everything passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs
git commit -m "refactor(purchase): move ShouldIncludeItem into StockAnalysisCalculator.FilterItems"
```

---
