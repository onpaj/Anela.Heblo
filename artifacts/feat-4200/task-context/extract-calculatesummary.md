### task: extract-calculatesummary

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests for `CalculateSummary`**

Append to `StockAnalysisCalculatorTests.cs`:

```csharp
    private static StockAnalysisItemDto MakeSummaryItem(StockSeverity severity, double effectiveStock, double? unitPrice) =>
        new()
        {
            ProductCode = "MAT",
            ProductName = "Test",
            ProductNameNormalized = "test",
            ProductType = "Material",
            Severity = severity,
            EffectiveStock = effectiveStock,
            LastPurchase = unitPrice.HasValue
                ? new LastPurchaseInfoDto { Date = DateTime.UtcNow, SupplierName = "Acme", Amount = 1, UnitPrice = unitPrice.Value, TotalPrice = 1 }
                : null,
        };

    [Fact]
    public void CalculateSummary_CountsEachSeverityBucket()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSummaryItem(StockSeverity.Critical, 1, 1),
            MakeSummaryItem(StockSeverity.Critical, 1, 1),
            MakeSummaryItem(StockSeverity.Low, 1, 1),
            MakeSummaryItem(StockSeverity.Optimal, 1, 1),
            MakeSummaryItem(StockSeverity.Overstocked, 1, 1),
            MakeSummaryItem(StockSeverity.NotConfigured, 1, 1),
        };
        var from = new DateTime(2024, 1, 1);
        var to = new DateTime(2024, 1, 31);

        var summary = _calculator.CalculateSummary(items, from, to);

        summary.TotalProducts.Should().Be(6);
        summary.CriticalCount.Should().Be(2);
        summary.LowStockCount.Should().Be(1);
        summary.OptimalCount.Should().Be(1);
        summary.OverstockedCount.Should().Be(1);
        summary.NotConfiguredCount.Should().Be(1);
        summary.AnalysisPeriodStart.Should().Be(from);
        summary.AnalysisPeriodEnd.Should().Be(to);
    }

    [Fact]
    public void CalculateSummary_TotalInventoryValue_MissingLastPurchaseTreatedAsZeroUnitPrice()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSummaryItem(StockSeverity.Optimal, effectiveStock: 10, unitPrice: 5),   // 10 * 5 = 50
            MakeSummaryItem(StockSeverity.Optimal, effectiveStock: 10, unitPrice: null), // 10 * 0 = 0
        };

        var summary = _calculator.CalculateSummary(items, DateTime.UtcNow, DateTime.UtcNow);

        summary.TotalInventoryValue.Should().Be(50m);
    }
```

- [ ] **Step 2: Run to verify it fails to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockAnalysisCalculatorTests"`
Expected: build error — `CalculateSummary` does not exist on `IStockAnalysisCalculator`/`StockAnalysisCalculator`.

- [ ] **Step 3: Add `CalculateSummary` to the interface**

Edit `IStockAnalysisCalculator.cs`, add:
```csharp
    /// <summary>
    /// Calculates severity counts and total inventory value across the given (unfiltered) item set.
    /// </summary>
    /// <param name="items">The full analyzed item set — callers must pass the unfiltered list, not a status/search-filtered subset</param>
    /// <param name="fromDate">Start of the analysis period, echoed into the summary</param>
    /// <param name="toDate">End of the analysis period, echoed into the summary</param>
    /// <returns>The computed summary</returns>
    StockAnalysisSummaryDto CalculateSummary(List<StockAnalysisItemDto> items, DateTime fromDate, DateTime toDate);
```

- [ ] **Step 4: Implement `CalculateSummary` in `StockAnalysisCalculator`, moved verbatim from the handler**

Add to `StockAnalysisCalculator.cs`, moved from `GetPurchaseStockAnalysisHandler.cs` lines 200–214:

```csharp
    public StockAnalysisSummaryDto CalculateSummary(List<StockAnalysisItemDto> items, DateTime fromDate, DateTime toDate)
    {
        return new StockAnalysisSummaryDto
        {
            TotalProducts = items.Count,
            CriticalCount = items.Count(i => i.Severity == StockSeverity.Critical),
            LowStockCount = items.Count(i => i.Severity == StockSeverity.Low),
            OptimalCount = items.Count(i => i.Severity == StockSeverity.Optimal),
            OverstockedCount = items.Count(i => i.Severity == StockSeverity.Overstocked),
            NotConfiguredCount = items.Count(i => i.Severity == StockSeverity.NotConfigured),
            TotalInventoryValue = items.Sum(i => (decimal)i.EffectiveStock * (i.LastPurchase?.UnitPrice ?? 0)),
            AnalysisPeriodStart = fromDate,
            AnalysisPeriodEnd = toDate
        };
    }
```

- [ ] **Step 5: Remove `CalculateSummary` from the handler and delegate — verify the `allAnalysisItems` invariant is preserved**

Edit `GetPurchaseStockAnalysisHandler.cs`:
- Delete the private `CalculateSummary` method (lines 200–214).
- Change:
```csharp
        var summary = CalculateSummary(allAnalysisItems, fromDate, toDate);
```
to:
```csharp
        var summary = _stockAnalysisCalculator.CalculateSummary(allAnalysisItems, fromDate, toDate);
```
**Do not** change `allAnalysisItems` to `analysisItems` here — this is the exact regression flagged as highest-risk in the architecture review (summary must reflect the unfiltered, category-matched set, not the status/search-filtered, paginated set).

After this step, `GetPurchaseStockAnalysisHandler.cs` should contain no private methods below `Handle()` — confirm with:
```bash
grep -n "private " backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs
```
Expected: no output (no private methods remain).

- [ ] **Step 6: Run tests to verify everything passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs
git commit -m "refactor(purchase): move CalculateSummary into StockAnalysisCalculator; handler is now a thin coordinator"
```

---
