### task: extract-sortitems

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests for `SortItems`**

Append to `StockAnalysisCalculatorTests.cs`:

```csharp
    private static StockAnalysisItemDto MakeSortableItem(string code, string name, double available, double consumption, double efficiency, DateTime? lastPurchaseDate) =>
        new()
        {
            ProductCode = code,
            ProductName = name,
            ProductNameNormalized = name.NormalizeForSearch(),
            ProductType = "Material",
            AvailableStock = available,
            ConsumptionInPeriod = consumption,
            StockEfficiencyPercentage = efficiency,
            LastPurchase = lastPurchaseDate.HasValue
                ? new LastPurchaseInfoDto { Date = lastPurchaseDate.Value, SupplierName = "Acme", Amount = 1, UnitPrice = 1, TotalPrice = 1 }
                : null,
        };

    [Fact]
    public void SortItems_ByProductCode_Ascending()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("B", "b", 0, 0, 0, null),
            MakeSortableItem("A", "a", 0, 0, 0, null),
        };

        var result = _calculator.SortItems(items, StockAnalysisSortBy.ProductCode, descending: false);

        result.Select(i => i.ProductCode).Should().ContainInOrder("A", "B");
    }

    [Fact]
    public void SortItems_ByProductCode_Descending_ReversesOrder()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("A", "a", 0, 0, 0, null),
            MakeSortableItem("B", "b", 0, 0, 0, null),
        };

        var result = _calculator.SortItems(items, StockAnalysisSortBy.ProductCode, descending: true);

        result.Select(i => i.ProductCode).Should().ContainInOrder("B", "A");
    }

    [Fact]
    public void SortItems_ByAvailableStock()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("A", "a", available: 50, 0, 0, null),
            MakeSortableItem("B", "b", available: 10, 0, 0, null),
        };

        var result = _calculator.SortItems(items, StockAnalysisSortBy.AvailableStock, descending: false);

        result.Select(i => i.ProductCode).Should().ContainInOrder("B", "A");
    }

    [Fact]
    public void SortItems_ByLastPurchaseDate_NullTreatedAsMinValue()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("A", "a", 0, 0, 0, new DateTime(2024, 6, 1)),
            MakeSortableItem("B", "b", 0, 0, 0, null),
        };

        var result = _calculator.SortItems(items, StockAnalysisSortBy.LastPurchaseDate, descending: false);

        result.Select(i => i.ProductCode).Should().ContainInOrder("B", "A");
    }

    [Fact]
    public void SortItems_DefaultFallback_SortsByStockEfficiency()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("A", "a", 0, 0, efficiency: 80, null),
            MakeSortableItem("B", "b", 0, 0, efficiency: 20, null),
        };

        var result = _calculator.SortItems(items, (StockAnalysisSortBy)999, descending: false);

        result.Select(i => i.ProductCode).Should().ContainInOrder("B", "A");
    }
```

- [ ] **Step 2: Run to verify it fails to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockAnalysisCalculatorTests"`
Expected: build error — `SortItems` does not exist on `IStockAnalysisCalculator`/`StockAnalysisCalculator`.

- [ ] **Step 3: Add `SortItems` to the interface**

Edit `IStockAnalysisCalculator.cs`, add:
```csharp
    /// <summary>
    /// Sorts analyzed stock items by the requested sort key and direction.
    /// </summary>
    /// <param name="items">Items to sort</param>
    /// <param name="sortBy">The field to sort by</param>
    /// <param name="descending">Whether to reverse the ascending order</param>
    /// <returns>The sorted item list</returns>
    List<StockAnalysisItemDto> SortItems(List<StockAnalysisItemDto> items, StockAnalysisSortBy sortBy, bool descending);
```

- [ ] **Step 4: Implement `SortItems` in `StockAnalysisCalculator`, moved verbatim from the handler**

Add to `StockAnalysisCalculator.cs`, moved from `GetPurchaseStockAnalysisHandler.cs` lines 184–198:

```csharp
    public List<StockAnalysisItemDto> SortItems(List<StockAnalysisItemDto> items, StockAnalysisSortBy sortBy, bool descending)
    {
        var sorted = sortBy switch
        {
            StockAnalysisSortBy.ProductCode => items.OrderBy(i => i.ProductCode),
            StockAnalysisSortBy.ProductName => items.OrderBy(i => i.ProductName),
            StockAnalysisSortBy.AvailableStock => items.OrderBy(i => i.AvailableStock),
            StockAnalysisSortBy.Consumption => items.OrderBy(i => i.ConsumptionInPeriod),
            StockAnalysisSortBy.StockEfficiency => items.OrderBy(i => i.StockEfficiencyPercentage),
            StockAnalysisSortBy.LastPurchaseDate => items.OrderBy(i => i.LastPurchase?.Date ?? DateTime.MinValue),
            _ => items.OrderBy(i => i.StockEfficiencyPercentage)
        };

        return descending ? sorted.Reverse().ToList() : sorted.ToList();
    }
```

- [ ] **Step 5: Remove `SortItems` from the handler and delegate**

Edit `GetPurchaseStockAnalysisHandler.cs`:
- Delete the private `SortItems` method (lines 184–198).
- Change:
```csharp
        analysisItems = SortItems(analysisItems, request.SortBy, request.SortDescending);
```
to:
```csharp
        analysisItems = _stockAnalysisCalculator.SortItems(analysisItems, request.SortBy, request.SortDescending);
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
git commit -m "refactor(purchase): move SortItems into StockAnalysisCalculator"
```

---
