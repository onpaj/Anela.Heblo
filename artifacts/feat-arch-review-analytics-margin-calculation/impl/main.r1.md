Here is the complete implementation output:

---

# Implementation: Extract Single-Product Margin Calculation Helper

## What was implemented

**Status: BLOCKED — file write permissions not granted in this session.**

The session permission mode requires user approval for all Edit/Write tool calls to source code files in the worktree. Read-only operations (file reads, `ls`, `git status`, `git diff`) work fine, but all write operations to `backend/` files are blocked. The memory directory writes work, confirming the issue is specific to source code paths.

All code changes are documented below in exact form and are ready to apply once permissions are granted (or can be applied manually).

---

## Files to create/modify

### 1. `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`

Add `using Anela.Heblo.Application.Features.Analytics.Contracts;` at line 1, add the method signature to the interface, and add the implementation at the end of the class:

**Interface addition** (after `GetMarginAmountForLevel`):
```csharp
AnalysisMarginData CalculateForProduct(
    AnalyticsProduct product,
    IEnumerable<SalesDataPoint> salesInPeriod);
```

**Class implementation** (before closing `}`):
```csharp
public AnalysisMarginData CalculateForProduct(
    AnalyticsProduct product,
    IEnumerable<SalesDataPoint> salesInPeriod)
{
    var unitsSold = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
    var revenue   = (decimal)unitsSold * product.SellingPrice;
    var cost      = (decimal)unitsSold * (product.SellingPrice - product.MarginAmount);
    var margin    = revenue - cost;
    var marginPct = revenue > 0 ? (margin / revenue) * 100 : 0;

    return new AnalysisMarginData
    {
        Revenue = revenue,
        Cost = cost,
        Margin = margin,
        MarginPercentage = marginPct,
        UnitsSold = unitsSold
    };
}
```

### 2. `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`

Add `IMarginCalculator _marginCalculator` field. Update constructor to:
```csharp
public GetMarginReportHandler(
    IAnalyticsRepository analyticsRepository,
    IProductFilterService productFilterService,
    IReportBuilderService reportBuilderService,
    IMarginCalculator marginCalculator)
{
    _analyticsRepository = analyticsRepository;
    _productFilterService = productFilterService;
    _reportBuilderService = reportBuilderService;
    _marginCalculator = marginCalculator;
}
```

In `ProcessProductsForReport`, replace lines 112–127 (the `totalSales`/`revenue`/`cost`/`margin`/`marginPercentage`/`marginData` block) with:
```csharp
var salesInPeriod = product.SalesHistory.Where(s => s.Date >= startDate && s.Date <= endDate);
var marginData = _marginCalculator.CalculateForProduct(product, salesInPeriod);
```

### 3. `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs`

Add `IMarginCalculator _marginCalculator` field. Update constructor to:
```csharp
public GetProductMarginAnalysisHandler(
    IAnalyticsRepository analyticsRepository,
    IReportBuilderService reportBuilderService,
    IMarginCalculator marginCalculator)
{
    _analyticsRepository = analyticsRepository;
    _reportBuilderService = reportBuilderService;
    _marginCalculator = marginCalculator;
}
```

In `Handle`, replace the `CalculateProductMargins` call (line 66) and the `BuildSuccessResponse` call block with:
```csharp
var salesInPeriod = productData.SalesHistory
    .Where(s => s.Date >= request.StartDate && s.Date <= request.EndDate)
    .ToList();
var marginData = _marginCalculator.CalculateForProduct(productData, salesInPeriod);

var response = BuildSuccessResponse(request, productData, marginData);

if (request.IncludeBreakdown)
{
    response.MonthlyBreakdown = _reportBuilderService.BuildMonthlyBreakdown(
        salesInPeriod, productData, request.StartDate, request.EndDate);
}

return response;
```

Delete the private `CalculateProductMargins` method entirely (lines 128–148).

Update `BuildSuccessResponse` to return `GetProductMarginAnalysisResponse` (remove the `IncludeBreakdown` logic from it since it's now in `Handle`).

### 4. `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs`

Add constructor:
```csharp
private readonly IMarginCalculator _marginCalculator;

public ReportBuilderService(IMarginCalculator marginCalculator)
{
    _marginCalculator = marginCalculator;
}
```

In `BuildMonthlyBreakdown`, replace the inline formula inside the loop:
```csharp
// Replace:
var monthlyUnitsSold = (int)monthSales.Sum(s => s.AmountB2B + s.AmountB2C);
var monthlyRevenue = (decimal)monthlyUnitsSold * productData.SellingPrice;
var monthlyCost = (decimal)monthlyUnitsSold * (productData.SellingPrice - productData.MarginAmount);
var monthlyMargin = monthlyRevenue - monthlyCost;

breakdown.Add(new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown
{
    Month = current,
    MarginAmount = monthlyMargin,
    Revenue = monthlyRevenue,
    Cost = monthlyCost,
    UnitsSold = monthlyUnitsSold
});

// With:
var monthData = _marginCalculator.CalculateForProduct(productData, monthSales);

breakdown.Add(new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown
{
    Month = current,
    MarginAmount = monthData.Margin,
    Revenue = monthData.Revenue,
    Cost = monthData.Cost,
    UnitsSold = monthData.UnitsSold
});
```

Also remove `.ToList()` from `monthSales` (pass the `IEnumerable` directly).

### 5. `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`

Add `using Anela.Heblo.Application.Features.Analytics.Services;` if not present.

In the constructor (line ~36), add `new MarginCalculator()` as 4th arg:
```csharp
_handler = new GetMarginReportHandler(
    _analyticsRepositoryMock.Object,
    _productFilterServiceMock.Object,
    _reportBuilderServiceMock.Object,
    new MarginCalculator());
```

### 6. `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs`

Add `using Anela.Heblo.Application.Features.Analytics.Services;` if not present.

In the constructor (line ~33), add `new MarginCalculator()` as 3rd arg:
```csharp
_handler = new GetProductMarginAnalysisHandler(
    _analyticsRepositoryMock.Object,
    _reportBuilderServiceMock.Object,
    new MarginCalculator());
```

### 7. Create `backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs` (NEW)

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class MarginCalculatorTests
{
    private readonly MarginCalculator _sut = new();

    private static AnalyticsProduct MakeProduct(decimal sellingPrice, decimal marginAmount) =>
        new()
        {
            ProductCode = "TEST",
            ProductName = "Test Product",
            SellingPrice = sellingPrice,
            MarginAmount = marginAmount
        };

    private static SalesDataPoint Sale(int b2b, int b2c, DateTime? date = null) =>
        new() { AmountB2B = b2b, AmountB2C = b2c, Date = date ?? DateTime.Today };

    [Fact]
    public void CalculateForProduct_NonEmptySales_ReturnsExpectedValues()
    {
        var product = MakeProduct(sellingPrice: 150m, marginAmount: 100m);
        var sales = new[] { Sale(10, 5) }; // 15 units

        var result = _sut.CalculateForProduct(product, sales);

        result.UnitsSold.Should().Be(15);
        result.Revenue.Should().Be(2250m);
        result.Cost.Should().Be(750m);
        result.Margin.Should().Be(1500m);
        result.MarginPercentage.Should().BeApproximately(66.67m, 0.01m);
    }

    [Fact]
    public void CalculateForProduct_EmptySales_ReturnsAllZeros()
    {
        var product = MakeProduct(sellingPrice: 150m, marginAmount: 100m);

        var result = _sut.CalculateForProduct(product, Enumerable.Empty<SalesDataPoint>());

        result.UnitsSold.Should().Be(0);
        result.Revenue.Should().Be(0m);
        result.Cost.Should().Be(0m);
        result.Margin.Should().Be(0m);
        result.MarginPercentage.Should().Be(0m);
    }

    [Fact]
    public void CalculateForProduct_SellingPriceZero_ReturnsMarginPercentageZero()
    {
        var product = MakeProduct(sellingPrice: 0m, marginAmount: 0m);
        var sales = new[] { Sale(10, 5) };

        var result = _sut.CalculateForProduct(product, sales);

        result.MarginPercentage.Should().Be(0m);
    }

    [Fact]
    public void CalculateForProduct_MixedB2BAndB2C_SumsBothCorrectly()
    {
        var product = MakeProduct(sellingPrice: 100m, marginAmount: 40m);
        var sales = new[]
        {
            Sale(b2b: 10, b2c: 0),
            Sale(b2b: 0,  b2c: 5),
            Sale(b2b: 3,  b2c: 2)
        }; // 20 units

        var result = _sut.CalculateForProduct(product, sales);

        result.UnitsSold.Should().Be(20);
        result.Revenue.Should().Be(2000m);
        result.Cost.Should().Be(1200m);
        result.Margin.Should().Be(800m);
        result.MarginPercentage.Should().Be(40m);
    }

    [Fact]
    public void CalculateForProduct_EnumeratesSalesExactlyOnce()
    {
        var product = MakeProduct(sellingPrice: 100m, marginAmount: 50m);
        var iterationCount = 0;

        IEnumerable<SalesDataPoint> OneShotEnumerable()
        {
            if (iterationCount++ > 0)
                throw new InvalidOperationException("Enumerated more than once");
            yield return Sale(5, 5);
        }

        var act = () => _sut.CalculateForProduct(product, OneShotEnumerable());
        act.Should().NotThrow();
    }
}
```

## Tests

- `MarginCalculatorTests.cs` — 5 new unit tests for `CalculateForProduct`
- `GetMarginReportHandlerTests.cs` — existing 8 tests (constructor update only, no semantic changes)
- `GetProductMarginAnalysisHandlerTests.cs` — existing 7 tests (constructor update only)

## How to verify

After applying the changes:
```bash
cd backend
dotnet build src/Anela.Heblo.Application
dotnet test test/Anela.Heblo.Tests --filter "MarginCalculatorTests|GetMarginReportHandlerTests|GetProductMarginAnalysisHandlerTests" -v minimal
```

Expected: build succeeds, 20 tests pass (5 new + 8 margin report + 7 margin analysis).

## Notes

**Root cause of BLOCKED status:** The session's Claude Code permission mode requires per-file user approval for Edit/Write tool calls. No source code writes have been approved. The memory directory (`~/.claude/projects/.../memory/`) is pre-approved and works fine. The pipeline needs to either:
1. Set `"permissionMode": "bypassPermissions"` in `~/.claude/settings.json`, or  
2. Add `"Edit"` and `"Write"` to the `permissions.allow` list in `.claude/settings.json`, or
3. Approve the Edit/Write permission dialogs when they appear

**Key architectural notes confirmed from reading source:**
- `SalesHistory` is already period-filtered by `CatalogAnalyticsSourceAdapter` per the arch review; the explicit `.Where()` in `GetMarginReportHandler` is a no-op but adds clarity
- The arch review's FR-2 "bug fix" framing is incorrect — outputs are bit-identical for all three handlers
- `GetProductMarginAnalysisHandler` currently filters in `CalculateProductMargins` AND in `BuildSuccessResponse` for `BuildMonthlyBreakdown` — after refactor, filter once and share the `salesInPeriod` variable

## PR Summary

Consolidates the duplicated per-product margin formula (`revenue = units × SellingPrice`, `cost = units × (SellingPrice − MarginAmount)`, `margin = revenue − cost`) into a single `IMarginCalculator.CalculateForProduct` method. Three call sites — `GetMarginReportHandler.ProcessProductsForReport`, `GetProductMarginAnalysisHandler.Handle`, and `ReportBuilderService.BuildMonthlyBreakdown` — previously re-implemented this formula inline; one had already drifted semantically. All now delegate to the shared helper, ensuring the business definition of margin lives in exactly one place.

The `AnalyticsProduct.SalesHistory` is already period-filtered by `CatalogAnalyticsSourceAdapter` upstream, so outputs are bit-identical for all three use cases. The explicit `.Where()` call site in `GetMarginReportHandler` adds clarity and future-proofs against contract changes in the adapter. No HTTP API surface, DTO, or DI registration changes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — added `CalculateForProduct` to interface and implementation; added `using` for Contracts namespace
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — injected `IMarginCalculator`; replaced inline formula with `CalculateForProduct` call
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — injected `IMarginCalculator`; deleted `CalculateProductMargins`; call `CalculateForProduct` with shared `salesInPeriod` variable
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` — injected `IMarginCalculator`; replaced inline formula in `BuildMonthlyBreakdown`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` — updated constructor to pass `new MarginCalculator()`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` — updated constructor to pass `new MarginCalculator()`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs` — new file; 5 unit tests for `CalculateForProduct`

## Status
BLOCKED

File write permissions are not granted in this session's permission mode. The pipeline needs to pre-approve Edit/Write tool calls (or set `permissionMode: "bypassPermissions"`). All code changes are fully documented above and ready to apply once permissions are configured.