# Move stock-analysis business logic out of GetPurchaseStockAnalysisHandler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `AnalyzeStockItem`, `ShouldIncludeItem`, `SortItems`, and `CalculateSummary` out of `GetPurchaseStockAnalysisHandler` and into `IStockAnalysisCalculator`/`StockAnalysisCalculator`, with zero behavior change, so the handler becomes a thin coordinator and each piece of analysis logic is independently unit-testable.

**Architecture:** `IStockAnalysisCalculator` grows four new public methods (`AnalyzeItem`, `FilterItems`, `SortItems`, `CalculateSummary`), mirroring the existing `IItemFilterService` pattern in the Manufacture module. `StockAnalysisCalculator` gains `IStockSeverityCalculator` as a constructor dependency (moved off the handler, since `AnalyzeItem` is its only caller). `GetPurchaseStockAnalysisHandler`'s constructor shrinks from 5 to 4 dependencies and its `Handle()` method delegates to `_stockAnalysisCalculator` for analysis/filter/sort/summary instead of implementing them as private methods. The free-text search-term filter stays inline in the handler (out of scope — not one of the four methods named in the issue).

**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions.

---

## Reference: exact current test constructor wiring (read before Task 1)

Both existing test fixtures build `StockAnalysisCalculator` with **no constructor arguments** today, and pass `IStockSeverityCalculator` to the **handler's** constructor as its 2nd parameter:

`backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs:30`
```csharp
_handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, _stockSeverityCalculatorMock.Object, new StockAnalysisCalculator(), _loggerMock.Object, _timeProviderMock.Object);
```

`backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs:30-34`
```csharp
_handler = new GetPurchaseStockAnalysisHandler(
    _materialCatalogMock.Object,
    _stockSeverityCalculatorMock.Object,
    new StockAnalysisCalculator(),
    _loggerMock.Object,
    _timeProviderMock.Object);
```

After this plan, both become (handler drops the severity-calculator param; `StockAnalysisCalculator` takes it instead):
```csharp
_handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, new StockAnalysisCalculator(_stockSeverityCalculatorMock.Object), _loggerMock.Object, _timeProviderMock.Object);
```
`_stockSeverityCalculatorMock.Setup(...)` calls elsewhere in both files (per-test severity stubbing) need no change — they configure the same `Mock<IStockSeverityCalculator>` object, which is still reachable through `StockAnalysisCalculator`'s injected dependency.

Test command used throughout this plan:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"
```

---

### task: add-severitycalculator-dependency-and-analyzeitem

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`

- [ ] **Step 1: Write the failing test for `AnalyzeItem`**

Create `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Xcc;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class StockAnalysisCalculatorTests
{
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly StockAnalysisCalculator _calculator;

    public StockAnalysisCalculatorTests()
    {
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _calculator = new StockAnalysisCalculator(_stockSeverityCalculatorMock.Object);
    }

    private static MaterialStockSnapshot MakeSnapshot(
        string productCode = "MAT001",
        string productName = "Test Material",
        decimal available = 100m,
        decimal ordered = 0m,
        decimal stockMinSetup = 10m,
        int optimalStockDaysSetup = 30,
        string minimalOrderQuantity = "",
        double consumptionInPeriod = 60,
        MaterialPurchaseSnapshot? lastPurchase = null)
    {
        var effective = available + ordered;
        return new MaterialStockSnapshot
        {
            ProductCode = productCode,
            ProductName = productName,
            ProductNameNormalized = productName.NormalizeForSearch(),
            ProductType = MaterialProductType.Material,
            SupplierName = "Acme",
            MinimalOrderQuantity = minimalOrderQuantity,
            IsMinStockConfigured = stockMinSetup > 0,
            IsOptimalStockConfigured = optimalStockDaysSetup > 0,
            Stock = new MaterialStockLevels
            {
                Available = available,
                Ordered = ordered,
                EffectiveStock = effective,
            },
            StockMinSetup = stockMinSetup,
            OptimalStockDaysSetup = optimalStockDaysSetup,
            ConsumptionInPeriod = consumptionInPeriod,
            LastPurchase = lastPurchase,
        };
    }

    [Fact]
    public void AnalyzeItem_ComputesDailyConsumptionAndDaysUntilStockout()
    {
        _stockSeverityCalculatorMock
            .Setup(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var snapshot = MakeSnapshot(available: 100m, ordered: 0m, consumptionInPeriod: 60);
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 1, 31); // 30-day period

        var result = _calculator.AnalyzeItem(snapshot, fromDate, toDate);

        result.DailyConsumption.Should().Be(60d / 30d);
        result.DaysUntilStockout.Should().Be((int)(100d / (60d / 30d)));
        result.ProductCode.Should().Be("MAT001");
        result.Severity.Should().Be(StockSeverity.Optimal);
    }

    [Fact]
    public void AnalyzeItem_ZeroConsumption_DaysUntilStockoutIsNull()
    {
        _stockSeverityCalculatorMock
            .Setup(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var snapshot = MakeSnapshot(consumptionInPeriod: 0);

        var result = _calculator.AnalyzeItem(snapshot, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        result.DaysUntilStockout.Should().BeNull();
    }

    [Fact]
    public void AnalyzeItem_NoLastPurchase_MapsNullLastPurchase()
    {
        _stockSeverityCalculatorMock
            .Setup(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.NotConfigured);

        var snapshot = MakeSnapshot(lastPurchase: null);

        var result = _calculator.AnalyzeItem(snapshot, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        result.LastPurchase.Should().BeNull();
    }

    [Fact]
    public void AnalyzeItem_WithLastPurchase_MapsAllFields()
    {
        _stockSeverityCalculatorMock
            .Setup(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var lastPurchase = new MaterialPurchaseSnapshot
        {
            Date = new DateTime(2024, 1, 15),
            SupplierName = "Acme",
            Amount = 50m,
            UnitPrice = 12.5,
            TotalPrice = 625,
        };
        var snapshot = MakeSnapshot(lastPurchase: lastPurchase);

        var result = _calculator.AnalyzeItem(snapshot, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        result.LastPurchase.Should().NotBeNull();
        result.LastPurchase!.Date.Should().Be(lastPurchase.Date);
        result.LastPurchase.SupplierName.Should().Be("Acme");
        result.LastPurchase.Amount.Should().Be(50d);
        result.LastPurchase.UnitPrice.Should().Be(12.5);
        result.LastPurchase.TotalPrice.Should().Be(625);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile (methods/constructor don't exist yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockAnalysisCalculatorTests"`
Expected: build error — `StockAnalysisCalculator` has no constructor taking `IStockSeverityCalculator` and no `AnalyzeItem` method.

- [ ] **Step 3: Add `IStockSeverityCalculator` constructor dependency and `AnalyzeItem` to the interface**

Edit `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — add after `CalculateRecommendedOrderQuantity`:

```csharp
    /// <summary>
    /// Analyzes a single material stock snapshot, computing consumption, stockout, efficiency,
    /// severity, and recommended order quantity for the given period.
    /// </summary>
    /// <param name="item">The material stock snapshot to analyze</param>
    /// <param name="fromDate">Start of the analysis period</param>
    /// <param name="toDate">End of the analysis period</param>
    /// <returns>The fully-computed analysis item</returns>
    StockAnalysisItemDto AnalyzeItem(MaterialStockSnapshot item, DateTime fromDate, DateTime toDate);
```

Add the required `using` at the top of the file:
```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
```

- [ ] **Step 4: Implement `AnalyzeItem` in `StockAnalysisCalculator`, moved verbatim from the handler**

Edit `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — add the constructor and the two methods (`AnalyzeItem`, private `GetLastPurchaseInfo`), moved verbatim from `GetPurchaseStockAnalysisHandler.cs` lines 94–164 (only the receiver of `_stockSeverityCalculatorMock`/`_stockAnalysisCalculator` calls changes from cross-class calls to local calls):

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;

namespace Anela.Heblo.Application.Features.Purchase.Services;

/// <summary>
/// Service responsible for calculating stock analysis metrics such as efficiency and recommended order quantity.
/// </summary>
public class StockAnalysisCalculator : IStockAnalysisCalculator
{
    private readonly IStockSeverityCalculator _stockSeverityCalculator;

    public StockAnalysisCalculator(IStockSeverityCalculator stockSeverityCalculator)
    {
        _stockSeverityCalculator = stockSeverityCalculator;
    }

    // ... existing CalculateStockEfficiency, CalculateRecommendedOrderQuantity unchanged ...

    public StockAnalysisItemDto AnalyzeItem(MaterialStockSnapshot item, DateTime fromDate, DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        var consumption = item.ConsumptionInPeriod;
        var dailyConsumption = consumption / (double)daysDiff;

        int? daysUntilStockout = null;
        if (dailyConsumption > 0)
        {
            daysUntilStockout = (int)((double)item.Stock.EffectiveStock / dailyConsumption);
        }

        var minStock = item.StockMinSetup;
        var optimalStockDays = item.OptimalStockDaysSetup;
        var optimalStock = optimalStockDays > 0 ? dailyConsumption * (double)optimalStockDays : 0;

        var stockEfficiency = CalculateStockEfficiency((double)item.Stock.EffectiveStock, (double)minStock, optimalStock);
        var severity = _stockSeverityCalculator.DetermineStockSeverity((double)item.Stock.EffectiveStock, (double)minStock, optimalStock, item.IsMinStockConfigured, item.IsOptimalStockConfigured);

        var lastPurchase = GetLastPurchaseInfo(item);

        var recommendedQuantity = CalculateRecommendedOrderQuantity(
            (double)item.Stock.Available,
            optimalStock,
            (double)minStock,
            item.MinimalOrderQuantity);

        return new StockAnalysisItemDto
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ProductNameNormalized = item.ProductNameNormalized,
            ProductType = item.ProductType.ToString(),
            AvailableStock = (double)item.Stock.Available,
            OrderedStock = (double)item.Stock.Ordered,
            EffectiveStock = (double)item.Stock.EffectiveStock,
            MinStockLevel = (double)minStock,
            OptimalStockLevel = optimalStock,
            ConsumptionInPeriod = consumption,
            DailyConsumption = dailyConsumption,
            DaysUntilStockout = daysUntilStockout,
            StockEfficiencyPercentage = stockEfficiency,
            Severity = severity,
            MinimalOrderQuantity = item.MinimalOrderQuantity,
            LastPurchase = lastPurchase,
            Supplier = item.SupplierName,
            RecommendedOrderQuantity = recommendedQuantity,
            IsConfigured = item.IsMinStockConfigured || item.IsOptimalStockConfigured
        };
    }

    private LastPurchaseInfoDto? GetLastPurchaseInfo(MaterialStockSnapshot item)
    {
        var lastPurchase = item.LastPurchase;

        if (lastPurchase == null)
        {
            return null;
        }

        return new LastPurchaseInfoDto
        {
            Date = lastPurchase.Date,
            SupplierName = lastPurchase.SupplierName,
            Amount = (double)lastPurchase.Amount,
            UnitPrice = lastPurchase.UnitPrice,
            TotalPrice = lastPurchase.TotalPrice
        };
    }
}
```

Note: `CalculateStockEfficiency`/`CalculateRecommendedOrderQuantity` calls inside `AnalyzeItem` are now unqualified local calls (`this.` implicit) instead of `_stockAnalysisCalculator.CalculateStockEfficiency(...)` — this is the only mechanical change from the original handler code besides the receiver of `DetermineStockSeverity`.

- [ ] **Step 5: Remove `AnalyzeStockItem` and `GetLastPurchaseInfo` from the handler; update handler constructor**

Edit `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`:
- Delete the private `AnalyzeStockItem` method (lines 94–145) and the private `GetLastPurchaseInfo` method (lines 147–164).
- Remove the `IStockSeverityCalculator _stockSeverityCalculator` field and its constructor parameter.
- Change the constructor to:
```csharp
    public GetPurchaseStockAnalysisHandler(
        IMaterialCatalogService materialCatalog,
        IStockAnalysisCalculator stockAnalysisCalculator,
        ILogger<GetPurchaseStockAnalysisHandler> logger,
        TimeProvider timeProvider)
    {
        _materialCatalog = materialCatalog;
        _stockAnalysisCalculator = stockAnalysisCalculator;
        _logger = logger;
        _timeProvider = timeProvider;
    }
```
- In `Handle()`, change:
```csharp
            .Select(s => AnalyzeStockItem(s, fromDate, toDate))
```
to:
```csharp
            .Select(s => _stockAnalysisCalculator.AnalyzeItem(s, fromDate, toDate))
```
(leave every other line of `Handle()` as-is for now — `ShouldIncludeItem`, `SortItems`, `CalculateSummary` are still private handler methods at this point and are handled in Tasks 2–4).

- [ ] **Step 6: Fix the two existing handler test fixtures to match the new constructors**

Edit `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs:30`, change:
```csharp
        _handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, _stockSeverityCalculatorMock.Object, new StockAnalysisCalculator(), _loggerMock.Object, _timeProviderMock.Object);
```
to:
```csharp
        _handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, new StockAnalysisCalculator(_stockSeverityCalculatorMock.Object), _loggerMock.Object, _timeProviderMock.Object);
```

Edit `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs:30-34`, change:
```csharp
        _handler = new GetPurchaseStockAnalysisHandler(
            _materialCatalogMock.Object,
            _stockSeverityCalculatorMock.Object,
            new StockAnalysisCalculator(),
            _loggerMock.Object,
            _timeProviderMock.Object);
```
to:
```csharp
        _handler = new GetPurchaseStockAnalysisHandler(
            _materialCatalogMock.Object,
            new StockAnalysisCalculator(_stockSeverityCalculatorMock.Object),
            _loggerMock.Object,
            _timeProviderMock.Object);
```

- [ ] **Step 7: Run tests to verify everything passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"`
Expected: PASS — all `StockAnalysisCalculatorTests`, `GetPurchaseStockAnalysisHandlerTests`, `GetPurchaseStockAnalysisHandlerDiacriticsTests` green.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs
git commit -m "refactor(purchase): move AnalyzeStockItem into StockAnalysisCalculator"
```

---

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

### task: full-suite-validation

**Files:** none created/modified — validation only.

- [ ] **Step 1: Full backend build**

Run: `dotnet build Anela.Heblo.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or the same pre-existing warning count as `main` — this refactor introduces no new warnings).

- [ ] **Step 2: Format check**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: no formatting violations. If violations are reported, run `dotnet format Anela.Heblo.sln`, review the diff is whitespace-only, then re-run Step 1.

- [ ] **Step 3: Full Purchase-module test run**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"`
Expected: PASS — includes `StockAnalysisCalculatorTests` (new), `GetPurchaseStockAnalysisHandlerTests` (585 lines, unmodified assertions), `GetPurchaseStockAnalysisHandlerDiacriticsTests` (104 lines, unmodified assertions).

- [ ] **Step 4: Full test project run (regression check for anything referencing the changed types elsewhere)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS, same pass count as on `main` plus the new `StockAnalysisCalculatorTests` cases.

- [ ] **Step 5: Confirm no unrelated files changed**

Run: `git diff --stat main...HEAD`
Expected: only the five files touched across Tasks 1–4 (`IStockAnalysisCalculator.cs`, `StockAnalysisCalculator.cs`, `GetPurchaseStockAnalysisHandler.cs`, `GetPurchaseStockAnalysisHandlerTests.cs`, `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`) plus the new `StockAnalysisCalculatorTests.cs`, plus this plan's own `artifacts/feat-4200/` files. No `PurchaseModule.cs` change (DI registration for `IStockAnalysisCalculator` already existed), no OpenAPI/frontend client regeneration (no public contract changed).

- [ ] **Step 6: Final commit (if Steps 2's format run produced changes not already committed)**

```bash
git add -A
git commit -m "chore(purchase): dotnet format after stock-analysis refactor" --allow-empty
```
