# GetPurchaseStockAnalysis Coverage Tests Implementation Plan

> **For agentic workers:** Implement this plan task-by-task using the steps below.

**Goal:** Add two unit tests to `GetPurchaseStockAnalysisHandlerTests.cs` that verify the dual-bucket invariant (Summary reflects all items regardless of display filter) and pin all StockAnalysisSummaryDto field values.

**Architecture:** Pure test addition — no production code changes. Two new `[Fact]` methods appended to the existing test class. Uses `SetupSequence` on the severity calculator mock to return per-item severities in snapshot order.

**Tech Stack:** C# / xUnit / Moq / FluentAssertions / .NET 8

---

### task: add-coverage-tests

**What this task does:** Appends two new `[Fact]` test methods to the existing `GetPurchaseStockAnalysisHandlerTests` class. No new files, classes, or mocks are introduced. All infrastructure (`_materialCatalogMock`, `_stockSeverityCalculatorMock`, `_loggerMock`, `_handler`, `MakeSnapshot`) already exists in the class.

**File to modify:**
`backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`

**Insertion point:** Append both methods immediately before the final `}` that closes the class body — after the `CreateManyTestSnapshots` method (currently ending at line 381).

#### Step 1 — Read the file to locate the exact insertion point

Open `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` and confirm the last two lines are:

```
    }  // closes CreateManyTestSnapshots
}      // closes GetPurchaseStockAnalysisHandlerTests
```

The two new methods go between these two closing braces.

#### Step 2 — Insert the FR-1 test: dual-bucket invariant

Using an exact-string edit tool, replace the closing brace of `CreateManyTestSnapshots` followed by the class-closing brace with those same braces plus the new FR-1 method inserted between them. The new method to insert is:

```csharp
    [Fact]
    public async Task Handle_FilterByCriticalStatus_SummaryReflectsAllItems()
    {
        // 2 Critical + 2 Optimal = 4 total
        var snapshots = new List<MaterialStockSnapshot>
        {
            MakeSnapshot("C001", "Critical 1", MaterialProductType.Material, available: 10m),
            MakeSnapshot("C002", "Critical 2", MaterialProductType.Material, available: 10m),
            MakeSnapshot("O001", "Optimal 1", MaterialProductType.Material, available: 10m),
            MakeSnapshot("O002", "Optimal 2", MaterialProductType.Material, available: 10m),
        };

        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        // Sequence must match snapshot declaration order — handler calls DetermineStockSeverity once per snapshot via Select
        _stockSeverityCalculatorMock
            .SetupSequence(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Critical)
            .Returns(StockSeverity.Critical)
            .Returns(StockSeverity.Optimal)
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            StockStatus = StockStatusFilter.Critical,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        // Items are filtered — only Critical items visible
        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.Severity == StockSeverity.Critical);

        // Summary reflects ALL 4 items (dual-bucket invariant)
        response.Summary.TotalProducts.Should().Be(4);
        response.Summary.OptimalCount.Should().Be(2, "non-critical items must appear in summary even when filtered from Items");
        response.Summary.CriticalCount.Should().Be(2);
    }
```

#### Step 3 — Insert the FR-2 test: all summary fields pinned

Immediately after the FR-1 method (still before the class-closing `}`), insert:

```csharp
    [Fact]
    public async Task Handle_CalculateSummary_AllFieldsAreCorrect()
    {
        var fromDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        // 5 items: one per severity. ordered=0 so EffectiveStock=available.
        // NotConfigured item has no lastPurchase (contributes 0 to TotalInventoryValue).
        var snapshots = new List<MaterialStockSnapshot>
        {
            MakeSnapshot("P001", "Critical Item",     MaterialProductType.Material, available: 10m, lastPurchase: new MaterialPurchaseSnapshot { Date = fromDate, SupplierName = "S1", Amount = 0m, UnitPrice = 5.00m, TotalPrice = 0m }),
            MakeSnapshot("P002", "Low Item",          MaterialProductType.Material, available: 20m, lastPurchase: new MaterialPurchaseSnapshot { Date = fromDate, SupplierName = "S2", Amount = 0m, UnitPrice = 3.00m, TotalPrice = 0m }),
            MakeSnapshot("P003", "Optimal Item",      MaterialProductType.Material, available: 30m, lastPurchase: new MaterialPurchaseSnapshot { Date = fromDate, SupplierName = "S3", Amount = 0m, UnitPrice = 2.00m, TotalPrice = 0m }),
            MakeSnapshot("P004", "Overstocked Item",  MaterialProductType.Material, available: 15m, lastPurchase: new MaterialPurchaseSnapshot { Date = fromDate, SupplierName = "S4", Amount = 0m, UnitPrice = 4.00m, TotalPrice = 0m }),
            MakeSnapshot("P005", "NotConfigured Item",MaterialProductType.Material, available:  0m), // no lastPurchase → 0 contribution
        };

        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        // Sequence matches snapshot declaration order
        _stockSeverityCalculatorMock
            .SetupSequence(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Critical)
            .Returns(StockSeverity.Low)
            .Returns(StockSeverity.Optimal)
            .Returns(StockSeverity.Overstocked)
            .Returns(StockSeverity.NotConfigured);

        var request = new GetPurchaseStockAnalysisRequest
        {
            FromDate = fromDate,
            ToDate = toDate,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Summary.TotalProducts.Should().Be(5);
        response.Summary.CriticalCount.Should().Be(1);
        response.Summary.LowStockCount.Should().Be(1);
        response.Summary.OptimalCount.Should().Be(1);
        response.Summary.OverstockedCount.Should().Be(1);
        response.Summary.NotConfiguredCount.Should().Be(1);
        // (10 × 5.00) + (20 × 3.00) + (30 × 2.00) + (15 × 4.00) + (0 × 0) = 50 + 60 + 60 + 60 + 0 = 230.00
        response.Summary.TotalInventoryValue.Should().Be(230.00m);
        response.Summary.AnalysisPeriodStart.Should().Be(fromDate);
        response.Summary.AnalysisPeriodEnd.Should().Be(toDate);
    }
```

#### Step 4 — Verify the edit produces a valid file structure

After both edits, the tail of the file must read (schematically):

```
    private List<MaterialStockSnapshot> CreateManyTestSnapshots(int count)
    {
        ...
    }

    [Fact]
    public async Task Handle_FilterByCriticalStatus_SummaryReflectsAllItems()
    {
        ...
    }

    [Fact]
    public async Task Handle_CalculateSummary_AllFieldsAreCorrect()
    {
        ...
    }
}   // ← single closing brace for the class
```

There must be exactly one closing `}` after `Handle_CalculateSummary_AllFieldsAreCorrect`. Do not add a namespace closing brace — the file uses file-scoped namespaces (`namespace Anela.Heblo.Tests.Features.Purchase;`).

#### Step 5 — Run the tests

```bash
cd /home/user/worktrees/feature-3415-Coverage-Gap-Purchase-Getpurchasestockanalysishand
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetPurchaseStockAnalysisHandler" -v normal
```

Expected outcome: all tests in `GetPurchaseStockAnalysisHandlerTests` pass, including the two new ones:
- `Handle_FilterByCriticalStatus_SummaryReflectsAllItems`
- `Handle_CalculateSummary_AllFieldsAreCorrect`

If a test fails with a `MockException` stating "Sequence contains no more elements", the `SetupSequence` call count does not match the number of snapshots — re-check that the snapshot list length matches the number of `.Returns(...)` calls chained in the sequence.

If `TotalInventoryValue` is wrong, verify that `EffectiveStock` is being used in the handler formula (not `Available`). With `ordered = 0`, `EffectiveStock = available`, so the result must still be `230.00m`.

#### Step 6 — Commit

Stage only the test file, then commit:

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs
git commit -m "test(purchase): add dual-bucket invariant and summary field snapshot tests for GetPurchaseStockAnalysisHandler"
```
