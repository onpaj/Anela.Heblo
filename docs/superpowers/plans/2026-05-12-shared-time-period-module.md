# Shared Time-Period Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidate all time-period bucket logic (PreviousQuarter, FutureQuarter, Y2Y, PreviousSeason, Q9M, CustomPeriod) into one shared BE module and one shared FE utility, replacing four independent implementations.

**Architecture:** A new `Common/TimePeriods/` folder in the Application layer hosts `TimePeriod` enum, `DateRange` record, `ITimePeriodResolver` interface, and `TimePeriodResolver` implementation. The FE mirror lives at `frontend/src/utils/timePeriod/`. Three frontend pages (Purchase, BatchPlanning, GiftPackage) that had inline date-math quick-buttons will call the shared resolver; the Manufacturing Stock Analysis page already uses the enum contract and just gets imports updated.

**Tech Stack:** C# 12 / .NET 8, xUnit + FluentAssertions + Moq (BE tests), TypeScript + Jest (FE tests)

---

## Context

Four separate implementations today:
1. **BE `TimePeriodCalculator`** — lives inside `Features/Manufacture/Services/`, returns tuples `(DateTime, DateTime)`
2. **FE hook** — `useManufacturingStockAnalysis.ts` re-declares the enum and re-implements the date math
3. **Three FE pages** — PurchaseStockAnalysis, ManufactureBatchPlanning, GiftPackageManufacturingFilters each have inline `handleQuickDateRange` and `getDateRangeTooltip` functions with identical (but inconsistently keyed) math for PrevQ / Y2Y / FutureQuarter

**Note on Y2Y semantic change in migrated pages:** The three inline pages used day-granular Y2Y: `(now.year-1, now.month, now.day) → today`. The shared resolver uses month-aligned Y2Y: `start of (month - 12) → last day of previous month`. This is intentional — makes all pages consistent with Manufacturing's well-defined semantics.

## File Map

**New (BE)**
- `backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriod.cs`
- `backend/src/Anela.Heblo.Application/Common/TimePeriods/DateRange.cs`
- `backend/src/Anela.Heblo.Application/Common/TimePeriods/ITimePeriodResolver.cs`
- `backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriodResolver.cs`
- `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs`

**Modified (BE)**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IConsumptionRateCalculator.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ConsumptionRateCalculator.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/GetManufacturingStockAnalysisRequestValidator.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ConsumptionRateCalculatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufacturingStockAnalysisHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufacturingStockAnalysisRequestValidatorTests.cs`

**Deleted (BE)**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ITimePeriodCalculator.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/TimePeriodCalculator.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/TimePeriodCalculatorTests.cs`

**New (FE)**
- `frontend/src/utils/timePeriod/timePeriod.ts`
- `frontend/src/utils/timePeriod/resolve.ts`
- `frontend/src/utils/timePeriod/displayText.ts`
- `frontend/src/utils/timePeriod/index.ts`
- `frontend/src/utils/timePeriod/__tests__/resolve.test.ts`

**Modified (FE)**
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`
- `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx`
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`
- `frontend/src/components/pages/PurchaseStockAnalysis.tsx`
- `frontend/src/components/pages/ManufactureBatchPlanning.tsx`
- `frontend/src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingFilters.tsx`

---

## Task 1: Create BE shared enum and DateRange record

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriod.cs`
- Create: `backend/src/Anela.Heblo.Application/Common/TimePeriods/DateRange.cs`

- [ ] **Step 1: Create `TimePeriod.cs`**

```csharp
namespace Anela.Heblo.Application.Common.TimePeriods;

public enum TimePeriod
{
    PreviousQuarter,
    FutureQuarter,
    Y2Y,
    PreviousSeason,
    Q9M,
    CustomPeriod
}
```

- [ ] **Step 2: Create `DateRange.cs`**

```csharp
namespace Anela.Heblo.Application.Common.TimePeriods;

public sealed record DateRange(DateTime From, DateTime To);
```

- [ ] **Step 3: Verify the two files compile in isolation**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet build --no-incremental 2>&1 | tail -20
```

Expected: build fails on missing references (old `TimePeriodFilter` still referenced) — that's OK at this point. Confirm the new files themselves have no syntax errors.

---

## Task 2: Create BE ITimePeriodResolver interface and TimePeriodResolver implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Common/TimePeriods/ITimePeriodResolver.cs`
- Create: `backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriodResolver.cs`

- [ ] **Step 1: Create `ITimePeriodResolver.cs`**

```csharp
namespace Anela.Heblo.Application.Common.TimePeriods;

public interface ITimePeriodResolver
{
    IReadOnlyList<DateRange> Resolve(
        TimePeriod period,
        DateTime? customFrom = null,
        DateTime? customTo = null);
}
```

- [ ] **Step 2: Create `TimePeriodResolver.cs`**

Port the logic verbatim from `Features/Manufacture/Services/TimePeriodCalculator.cs`, replacing tuples with `DateRange`.

```csharp
namespace Anela.Heblo.Application.Common.TimePeriods;

public sealed class TimePeriodResolver : ITimePeriodResolver
{
    public IReadOnlyList<DateRange> Resolve(
        TimePeriod period,
        DateTime? customFrom = null,
        DateTime? customTo = null)
    {
        var now = DateTime.UtcNow;

        return period switch
        {
            TimePeriod.PreviousQuarter => PreviousQuarter(now),
            TimePeriod.FutureQuarter => FutureQuarter(now),
            TimePeriod.Y2Y => Y2Y(now),
            TimePeriod.PreviousSeason => PreviousSeason(now),
            TimePeriod.Q9M => Q9M(now),
            TimePeriod.CustomPeriod when customFrom.HasValue && customTo.HasValue =>
                [new DateRange(customFrom.Value, customTo.Value)],
            _ => PreviousQuarter(now)
        };
    }

    private static IReadOnlyList<DateRange> PreviousQuarter(DateTime now)
    {
        var startOfCurrentMonth = new DateTime(now.Year, now.Month, 1);
        var endOfPreviousMonth = startOfCurrentMonth.AddDays(-1);
        var startOfPreviousQuarter = startOfCurrentMonth.AddMonths(-3);
        return [new DateRange(startOfPreviousQuarter, endOfPreviousMonth)];
    }

    private static IReadOnlyList<DateRange> FutureQuarter(DateTime now)
    {
        var startOfFutureQuarterLastYear = new DateTime(now.Year - 1, now.Month, 1);
        var endOfFutureQuarterLastYear = startOfFutureQuarterLastYear.AddMonths(3).AddDays(-1);
        return [new DateRange(startOfFutureQuarterLastYear, endOfFutureQuarterLastYear)];
    }

    private static IReadOnlyList<DateRange> Y2Y(DateTime now)
    {
        var startOfY2Y = new DateTime(now.Year, now.Month, 1).AddMonths(-12);
        var endOfY2Y = new DateTime(now.Year, now.Month, 1).AddDays(-1);
        return [new DateRange(startOfY2Y, endOfY2Y)];
    }

    private static IReadOnlyList<DateRange> PreviousSeason(DateTime now)
    {
        var seasonStart = new DateTime(now.Year - 1, 10, 1);
        var seasonEnd = new DateTime(now.Year, 1, 31);
        return [new DateRange(seasonStart, seasonEnd)];
    }

    private static IReadOnlyList<DateRange> Q9M(DateTime now)
    {
        var rangeAFrom = now.AddMonths(-6);
        var rangeATo = now;
        var rangeBFrom = now.AddYears(-1);
        var rangeBTo = now.AddYears(-1).AddMonths(3);
        return
        [
            new DateRange(rangeAFrom, rangeATo),
            new DateRange(rangeBFrom, rangeBTo)
        ];
    }
}
```

- [ ] **Step 3: Verify no syntax errors**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet build --no-incremental 2>&1 | grep -E "error|warning" | head -20
```

Expected: same compilation errors as before (old references untouched). No new errors.

---

## Task 3: Update IConsumptionRateCalculator and ConsumptionRateCalculator

The multi-range overload currently takes `IReadOnlyList<(DateTime fromDate, DateTime toDate)>` — update to `IReadOnlyList<DateRange>`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IConsumptionRateCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ConsumptionRateCalculator.cs`

- [ ] **Step 1: Update `IConsumptionRateCalculator.cs`**

Replace:
```csharp
double CalculateDailySalesRate(IEnumerable<CatalogSaleRecord> salesHistory, IReadOnlyList<(DateTime fromDate, DateTime toDate)> ranges);
```
With:
```csharp
double CalculateDailySalesRate(IEnumerable<CatalogSaleRecord> salesHistory, IReadOnlyList<Anela.Heblo.Application.Common.TimePeriods.DateRange> ranges);
```

Add using at the top of the file:
```csharp
using Anela.Heblo.Application.Common.TimePeriods;
```

- [ ] **Step 2: Update `ConsumptionRateCalculator.cs`**

Add using:
```csharp
using Anela.Heblo.Application.Common.TimePeriods;
```

Replace the method signature:
```csharp
public double CalculateDailySalesRate(IEnumerable<CatalogSaleRecord> salesHistory, IReadOnlyList<(DateTime fromDate, DateTime toDate)> ranges)
```
With:
```csharp
public double CalculateDailySalesRate(IEnumerable<CatalogSaleRecord> salesHistory, IReadOnlyList<DateRange> ranges)
```

Replace the loop body (the `foreach` and range accesses):
```csharp
// OLD
foreach (var (from, to) in ranges)
{
    var lo = from <= to ? from : to;
    var hi = from <= to ? to : from;
```
Replace with:
```csharp
// NEW
foreach (var range in ranges)
{
    var lo = range.From <= range.To ? range.From : range.To;
    var hi = range.From <= range.To ? range.To : range.From;
```

- [ ] **Step 3: Attempt build — expect remaining errors**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet build --no-incremental 2>&1 | grep "error CS" | head -20
```

Expected: errors still about `TimePeriodFilter`, `ITimePeriodCalculator`, handler code. The `ConsumptionRateCalculator` should now compile cleanly.

---

## Task 4: Update GetManufacturingStockAnalysisRequest — drop inline enum

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisRequest.cs`

- [ ] **Step 1: Add using and update the request DTO**

Replace the entire file content with:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Common.TimePeriods;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;

public class GetManufacturingStockAnalysisRequest : IRequest<GetManufacturingStockAnalysisResponse>
{
    public TimePeriod TimePeriod { get; set; } = TimePeriod.Q9M;

    public DateTime? CustomFromDate { get; set; }

    public DateTime? CustomToDate { get; set; }

    public string? ProductFamily { get; set; }

    public bool CriticalItemsOnly { get; set; } = false;

    public bool MajorItemsOnly { get; set; } = false;

    public bool AdequateItemsOnly { get; set; } = false;

    public bool UnconfiguredOnly { get; set; } = false;

    public string? SearchTerm { get; set; }

    /// <summary>
    /// Page number for pagination. Must be at least 1.
    /// </summary>
    [Range(Anela.Heblo.Application.Features.Manufacture.ManufactureConstants.MIN_PAGE_NUMBER, int.MaxValue,
           ErrorMessage = "PageNumber must be at least 1")]
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// Number of items per page. Must be between 1 and 100.
    /// </summary>
    [Range(Anela.Heblo.Application.Features.Manufacture.ManufactureConstants.MIN_PAGE_SIZE,
           Anela.Heblo.Application.Features.Manufacture.ManufactureConstants.MAX_PAGE_SIZE,
           ErrorMessage = "PageSize must be between 1 and 100")]
    public int PageSize { get; set; } = 20;

    public ManufacturingStockSortBy SortBy { get; set; } = ManufacturingStockSortBy.StockDaysAvailable;

    public bool SortDescending { get; set; } = false;

    /// <summary>
    /// Multiplier applied to daily sales rate for demand forecasting.
    /// Range: 0.1 to 3.0, default 1.0.
    /// </summary>
    public double SalesMultiplier { get; set; } = 1.0;

    public bool IsExport { get; set; } = false;
}

public enum ManufacturingStockSortBy
{
    ProductCode,
    ProductName,
    CurrentStock,
    Reserve,
    Quarantine,
    Planned,
    SalesInPeriod,
    DailySales,
    OptimalDaysSetup,
    StockDaysAvailable,
    MinimumStock,
    OverstockPercentage,
    BatchSize
}
```

Note: `TimePeriodFilter` enum (lines 54-62) is removed entirely. The `TimePeriod` property now uses `Anela.Heblo.Application.Common.TimePeriods.TimePeriod`.

---

## Task 5: Update GetManufacturingStockAnalysisHandler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisHandler.cs`

- [ ] **Step 1: Update usings and inject ITimePeriodResolver**

Replace the using for `ITimePeriodCalculator` with `ITimePeriodResolver`:

At the top, change:
```csharp
using Anela.Heblo.Application.Features.Manufacture.Services;
```
to keep this (for other services) AND add:
```csharp
using Anela.Heblo.Application.Common.TimePeriods;
```

Change the field declaration from:
```csharp
private readonly ITimePeriodCalculator _timePeriodCalculator;
```
to:
```csharp
private readonly ITimePeriodResolver _timePeriodResolver;
```

Change the constructor parameter from:
```csharp
ITimePeriodCalculator timePeriodCalculator,
```
to:
```csharp
ITimePeriodResolver timePeriodResolver,
```

Change the constructor body assignment from:
```csharp
_timePeriodCalculator = timePeriodCalculator;
```
to:
```csharp
_timePeriodResolver = timePeriodResolver;
```

- [ ] **Step 2: Update the Handle method**

In `Handle(...)`, change:
```csharp
var ranges = _timePeriodCalculator.CalculateTimePeriodRanges(
    request.TimePeriod, request.CustomFromDate, request.CustomToDate);

var outerFrom = ranges.Min(r => r.fromDate);
var outerTo = ranges.Max(r => r.toDate);
```
to:
```csharp
var ranges = _timePeriodResolver.Resolve(
    request.TimePeriod, request.CustomFromDate, request.CustomToDate);

var outerFrom = ranges.Min(r => r.From);
var outerTo = ranges.Max(r => r.To);
```

- [ ] **Step 3: Update AnalyzeManufacturingStockItem**

Change the method signature from:
```csharp
private ManufacturingStockItemDto AnalyzeManufacturingStockItem(
    CatalogAggregate item,
    IReadOnlyList<(DateTime fromDate, DateTime toDate)> ranges,
    double salesMultiplier = 1.0)
```
to:
```csharp
private ManufacturingStockItemDto AnalyzeManufacturingStockItem(
    CatalogAggregate item,
    IReadOnlyList<DateRange> ranges,
    double salesMultiplier = 1.0)
```

Change inside the method:
```csharp
var salesInPeriod = ranges.Sum(r => item.GetTotalSold(r.fromDate, r.toDate)) * salesMultiplier;
```
to:
```csharp
var salesInPeriod = ranges.Sum(r => item.GetTotalSold(r.From, r.To)) * salesMultiplier;
```

---

## Task 6: Update validator

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/GetManufacturingStockAnalysisRequestValidator.cs`

- [ ] **Step 1: Update using and enum reference**

The validator currently has:
```csharp
When(x => x.TimePeriod == TimePeriodFilter.CustomPeriod, () =>
```

Since `TimePeriodFilter` is removed and replaced by `TimePeriod` from the `Common` namespace, update:

Add using at the top:
```csharp
using Anela.Heblo.Application.Common.TimePeriods;
```

Change the condition:
```csharp
When(x => x.TimePeriod == TimePeriodFilter.CustomPeriod, () =>
```
to:
```csharp
When(x => x.TimePeriod == TimePeriod.CustomPeriod, () =>
```

- [ ] **Step 2: Build to confirm errors reduced**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet build --no-incremental 2>&1 | grep "error CS" | head -20
```

Expected: errors should now only remain in `ManufactureModule.cs` (still references `ITimePeriodCalculator`).

---

## Task 7: Update DI registration — ManufactureModule and ApplicationModule

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

- [ ] **Step 1: Remove ITimePeriodCalculator from ManufactureModule**

In `ManufactureModule.cs`, delete the line:
```csharp
services.AddScoped<ITimePeriodCalculator, TimePeriodCalculator>();
```

Also remove the `using` for `Anela.Heblo.Application.Features.Manufacture.Services` if it was imported solely for this (check other references first — it's likely still needed for other services).

- [ ] **Step 2: Register ITimePeriodResolver in ApplicationModule**

In `ApplicationModule.cs`, add after the `using` statements at the top:
```csharp
using Anela.Heblo.Application.Common.TimePeriods;
```

In the `AddApplicationServices` method body, before the feature module registrations, add:
```csharp
// Register shared time-period resolver
services.AddScoped<ITimePeriodResolver, TimePeriodResolver>();
```

- [ ] **Step 3: Build — expect clean**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet build --no-incremental 2>&1 | grep "error CS"
```

Expected: **no errors**. If there are lingering `TimePeriodFilter` or `ITimePeriodCalculator` references in other files, find and fix them now:

```bash
grep -rn "TimePeriodFilter\|ITimePeriodCalculator\|TimePeriodCalculator" /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/src --include="*.cs"
```

Expected: no results (test files are under `test/`, not `src/`).

---

## Task 8: Delete obsolete BE source files

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ITimePeriodCalculator.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/TimePeriodCalculator.cs`

- [ ] **Step 1: Delete the files**

```bash
rm /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ITimePeriodCalculator.cs
rm /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/src/Anela.Heblo.Application/Features/Manufacture/Services/TimePeriodCalculator.cs
```

- [ ] **Step 2: Build to confirm still clean**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet build 2>&1 | grep "error CS"
```

Expected: **no errors**.

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Application/Common/TimePeriods/
git add backend/src/Anela.Heblo.Application/Features/Manufacture/
git add backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "refactor: move time-period resolver to shared Common module"
```

---

## Task 9: Create BE TimePeriodResolverTests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/TimePeriodCalculatorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs`:

```csharp
using Anela.Heblo.Application.Common.TimePeriods;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Common.TimePeriods;

public class TimePeriodResolverTests
{
    private readonly TimePeriodResolver _sut = new();

    [Fact]
    public void Resolve_PreviousQuarter_ReturnsOneRange()
    {
        var now = DateTime.UtcNow;
        var ranges = _sut.Resolve(TimePeriod.PreviousQuarter);

        ranges.Should().HaveCount(1);
        ranges[0].From.Should().Be(new DateTime(now.Year, now.Month, 1).AddMonths(-3));
        ranges[0].To.Should().Be(new DateTime(now.Year, now.Month, 1).AddDays(-1));
    }

    [Fact]
    public void Resolve_FutureQuarter_ReturnsOneRange()
    {
        var now = DateTime.UtcNow;
        var ranges = _sut.Resolve(TimePeriod.FutureQuarter);

        ranges.Should().HaveCount(1);
        var expectedFrom = new DateTime(now.Year - 1, now.Month, 1);
        ranges[0].From.Should().Be(expectedFrom);
        ranges[0].To.Should().Be(expectedFrom.AddMonths(3).AddDays(-1));
    }

    [Fact]
    public void Resolve_Y2Y_ReturnsOneRange()
    {
        var now = DateTime.UtcNow;
        var ranges = _sut.Resolve(TimePeriod.Y2Y);

        ranges.Should().HaveCount(1);
        ranges[0].From.Should().Be(new DateTime(now.Year, now.Month, 1).AddMonths(-12));
        ranges[0].To.Should().Be(new DateTime(now.Year, now.Month, 1).AddDays(-1));
    }

    [Fact]
    public void Resolve_PreviousSeason_ReturnsOctToJan()
    {
        var now = DateTime.UtcNow;
        var ranges = _sut.Resolve(TimePeriod.PreviousSeason);

        ranges.Should().HaveCount(1);
        ranges[0].From.Should().Be(new DateTime(now.Year - 1, 10, 1));
        ranges[0].To.Should().Be(new DateTime(now.Year, 1, 31));
    }

    [Fact]
    public void Resolve_Q9M_ReturnsTwoRanges()
    {
        var now = DateTime.UtcNow;
        var ranges = _sut.Resolve(TimePeriod.Q9M);

        ranges.Should().HaveCount(2);
        ranges[0].From.Date.Should().Be(now.AddMonths(-6).Date);
        ranges[0].To.Date.Should().Be(now.Date);
        ranges[1].From.Date.Should().Be(now.AddYears(-1).Date);
        ranges[1].To.Date.Should().Be(now.AddYears(-1).AddMonths(3).Date);
    }

    [Fact]
    public void Resolve_CustomPeriod_WithDates_ReturnsProvidedRange()
    {
        var from = new DateTime(2023, 1, 1);
        var to = new DateTime(2023, 3, 31);
        var ranges = _sut.Resolve(TimePeriod.CustomPeriod, from, to);

        ranges.Should().HaveCount(1);
        ranges[0].From.Should().Be(from);
        ranges[0].To.Should().Be(to);
    }

    [Fact]
    public void Resolve_CustomPeriod_WithoutDates_FallsBackToPreviousQuarter()
    {
        var now = DateTime.UtcNow;
        var ranges = _sut.Resolve(TimePeriod.CustomPeriod);

        ranges.Should().HaveCount(1);
        ranges[0].From.Should().Be(new DateTime(now.Year, now.Month, 1).AddMonths(-3));
        ranges[0].To.Should().Be(new DateTime(now.Year, now.Month, 1).AddDays(-1));
    }

    [Theory]
    [InlineData(TimePeriod.FutureQuarter)]
    [InlineData(TimePeriod.Y2Y)]
    [InlineData(TimePeriod.PreviousSeason)]
    [InlineData(TimePeriod.PreviousQuarter)]
    public void Resolve_SingleRangePeriods_ReturnOneElement(TimePeriod period)
    {
        _sut.Resolve(period).Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_InvalidEnumValue_FallsBackToPreviousQuarter()
    {
        var now = DateTime.UtcNow;
        var ranges = _sut.Resolve((TimePeriod)999);

        ranges.Should().HaveCount(1);
        ranges[0].From.Should().Be(new DateTime(now.Year, now.Month, 1).AddMonths(-3));
    }
}
```

- [ ] **Step 2: Run the new tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet test --filter "FullyQualifiedName~TimePeriodResolverTests" --no-build 2>&1 | tail -20
```

Expected: all tests PASS.

- [ ] **Step 3: Delete the old test file**

```bash
rm /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/TimePeriodCalculatorTests.cs
```

- [ ] **Step 4: Confirm tests still pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet test --filter "FullyQualifiedName~TimePeriod" 2>&1 | tail -10
```

Expected: 10+ tests, all PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/test/Anela.Heblo.Tests/Common/
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/TimePeriodCalculatorTests.cs
git commit -m "test: add TimePeriodResolverTests, remove obsolete TimePeriodCalculatorTests"
```

---

## Task 10: Update ConsumptionRateCalculatorTests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ConsumptionRateCalculatorTests.cs`

- [ ] **Step 1: Add using for DateRange**

Add at the top of the test file:
```csharp
using Anela.Heblo.Application.Common.TimePeriods;
```

- [ ] **Step 2: Update the multi-range test variables**

Find the three tests that use `IReadOnlyList<(DateTime, DateTime)>`. Replace each declaration like:
```csharp
IReadOnlyList<(DateTime, DateTime)> ranges = [rangeA, rangeB];
```
with:
```csharp
IReadOnlyList<DateRange> ranges = [new DateRange(rangeA.Item1, rangeA.Item2), new DateRange(rangeB.Item1, rangeB.Item2)];
```

But actually these tests construct the tuples inline. The cleanest rewrite for each of the 3 test methods:

**Test at line ~185** — multi-range test. Replace the range setup:
```csharp
// OLD
var rangeA = (new DateTime(2025, 1, 1), new DateTime(2025, 1, 11));
var rangeB = (new DateTime(2025, 2, 1), new DateTime(2025, 2, 11));
IReadOnlyList<(DateTime, DateTime)> ranges = [rangeA, rangeB];

// NEW
IReadOnlyList<DateRange> ranges =
[
    new DateRange(new DateTime(2025, 1, 1), new DateTime(2025, 1, 11)),
    new DateRange(new DateTime(2025, 2, 1), new DateTime(2025, 2, 11))
];
```

**Test at line ~206** — empty ranges test. Replace:
```csharp
IReadOnlyList<(DateTime, DateTime)> ranges = [];
// NEW
IReadOnlyList<DateRange> ranges = [];
```

**Test at line ~222** — single range comparison. Replace:
```csharp
IReadOnlyList<(DateTime, DateTime)> ranges = [(fromDate, toDate)];
// NEW
IReadOnlyList<DateRange> ranges = [new DateRange(fromDate, toDate)];
```

- [ ] **Step 3: Run the tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet test --filter "FullyQualifiedName~ConsumptionRateCalculatorTests" 2>&1 | tail -10
```

Expected: all PASS.

---

## Task 11: Update GetManufacturingStockAnalysisHandlerTests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufacturingStockAnalysisHandlerTests.cs`

- [ ] **Step 1: Add using for Common.TimePeriods**

Add at the top:
```csharp
using Anela.Heblo.Application.Common.TimePeriods;
```

- [ ] **Step 2: Replace ITimePeriodCalculator mock with ITimePeriodResolver**

In the test class field declarations, change:
```csharp
private readonly Mock<ITimePeriodCalculator> _timePeriodCalculatorMock;
```
to:
```csharp
private readonly Mock<ITimePeriodResolver> _timePeriodResolverMock;
```

In the constructor, change:
```csharp
_timePeriodCalculatorMock = new Mock<ITimePeriodCalculator>();
```
to:
```csharp
_timePeriodResolverMock = new Mock<ITimePeriodResolver>();
```

And the handler construction:
```csharp
_handler = new GetManufacturingStockAnalysisHandler(
    _catalogRepositoryMock.Object,
    _timePeriodCalculatorMock.Object,
    ...
```
to:
```csharp
_handler = new GetManufacturingStockAnalysisHandler(
    _catalogRepositoryMock.Object,
    _timePeriodResolverMock.Object,
    ...
```

- [ ] **Step 3: Update all mock setups**

Find every occurrence of (there are 5 in the file):
```csharp
_timePeriodCalculatorMock.Setup(x => x.CalculateTimePeriodRanges(It.IsAny<TimePeriodFilter>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
    .Returns(new List<(DateTime, DateTime)> { (DateTime.Today.AddDays(-30), DateTime.Today) });
```
Replace with:
```csharp
_timePeriodResolverMock.Setup(x => x.Resolve(It.IsAny<TimePeriod>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
    .Returns(new List<DateRange> { new DateRange(DateTime.Today.AddDays(-30), DateTime.Today) });
```

(Adjust the specific date offsets if they differ between tests — keep each test's original date values, just change the type.)

Also update the mock setup for `_consumptionCalculatorMock` that uses `IReadOnlyList<(DateTime, DateTime)>`:
```csharp
// OLD
_consumptionCalculatorMock.Setup(x => x.CalculateDailySalesRate(It.IsAny<IEnumerable<CatalogSaleRecord>>(), It.IsAny<IReadOnlyList<(DateTime, DateTime)>>()))
    .Returns(5.0);

// NEW
_consumptionCalculatorMock.Setup(x => x.CalculateDailySalesRate(It.IsAny<IEnumerable<CatalogSaleRecord>>(), It.IsAny<IReadOnlyList<DateRange>>()))
    .Returns(5.0);
```

Also update any `TimePeriodFilter.PreviousQuarter` references in test request construction to `TimePeriod.PreviousQuarter`.

- [ ] **Step 4: Run the handler tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet test --filter "FullyQualifiedName~GetManufacturingStockAnalysisHandlerTests" 2>&1 | tail -10
```

Expected: all PASS.

---

## Task 12: Update GetManufacturingStockAnalysisRequestValidatorTests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufacturingStockAnalysisRequestValidatorTests.cs`

- [ ] **Step 1: Add using and update references**

Add at the top:
```csharp
using Anela.Heblo.Application.Common.TimePeriods;
```

Replace every `TimePeriodFilter.` with `TimePeriod.` in the file (there are 5 occurrences: `TimePeriodFilter.PreviousQuarter` × 2, `TimePeriodFilter.CustomPeriod` × 3).

```bash
# Verify count first
grep -c "TimePeriodFilter\." /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufacturingStockAnalysisRequestValidatorTests.cs
```

- [ ] **Step 2: Run the validator tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet test --filter "FullyQualifiedName~GetManufacturingStockAnalysisRequestValidatorTests" 2>&1 | tail -10
```

Expected: all PASS.

- [ ] **Step 3: Run full BE test suite**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet test 2>&1 | tail -15
```

Expected: all tests PASS, 0 failed.

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/test/
git commit -m "test: update BE tests for ITimePeriodResolver and DateRange"
```

---

## Task 13: Create FE shared time-period utility

**Files:**
- Create: `frontend/src/utils/timePeriod/timePeriod.ts`
- Create: `frontend/src/utils/timePeriod/resolve.ts`
- Create: `frontend/src/utils/timePeriod/displayText.ts`
- Create: `frontend/src/utils/timePeriod/index.ts`

- [ ] **Step 1: Write the failing test first**

Create `frontend/src/utils/timePeriod/__tests__/resolve.test.ts`:

```typescript
import { resolveTimePeriod } from "../resolve";
import { TimePeriod } from "../timePeriod";

describe("resolveTimePeriod", () => {
  const FIXED_NOW = new Date("2023-04-15T12:00:00.000Z");

  beforeAll(() => {
    jest.useFakeTimers();
    jest.setSystemTime(FIXED_NOW);
  });

  afterAll(() => {
    jest.useRealTimers();
  });

  describe("PreviousQuarter", () => {
    it("returns one range covering last 3 completed months", () => {
      const { ranges, primary } = resolveTimePeriod(TimePeriod.PreviousQuarter);
      expect(ranges).toHaveLength(1);
      expect(primary).not.toBeNull();
      // April 15 2023: prevQ = Jan 1 - Mar 31 2023
      expect(primary!.from.getFullYear()).toBe(2023);
      expect(primary!.from.getMonth()).toBe(0); // January
      expect(primary!.from.getDate()).toBe(1);
      // End of March
      expect(primary!.to.getFullYear()).toBe(2023);
      expect(primary!.to.getMonth()).toBe(2); // March
    });
  });

  describe("FutureQuarter", () => {
    it("returns one range for next 3 months from previous year", () => {
      const { ranges, primary } = resolveTimePeriod(TimePeriod.FutureQuarter);
      expect(ranges).toHaveLength(1);
      // April 15 2023 → Apr 2022 to Jun 2022
      expect(primary!.from.getFullYear()).toBe(2022);
      expect(primary!.from.getMonth()).toBe(3); // April
      expect(primary!.to.getFullYear()).toBe(2022);
      expect(primary!.to.getMonth()).toBe(5); // June
    });
  });

  describe("Y2Y", () => {
    it("returns one range for last 12 months (month-aligned)", () => {
      const { ranges, primary } = resolveTimePeriod(TimePeriod.Y2Y);
      expect(ranges).toHaveLength(1);
      // April 15 2023: Y2Y = Apr 1 2022 - Mar 31 2023
      expect(primary!.from.getFullYear()).toBe(2022);
      expect(primary!.from.getMonth()).toBe(3); // April
      expect(primary!.from.getDate()).toBe(1);
      expect(primary!.to.getFullYear()).toBe(2023);
      expect(primary!.to.getMonth()).toBe(2); // March
    });
  });

  describe("PreviousSeason", () => {
    it("returns one range Oct-Jan", () => {
      const { ranges, primary } = resolveTimePeriod(TimePeriod.PreviousSeason);
      expect(ranges).toHaveLength(1);
      expect(primary!.from.getMonth()).toBe(9); // October
      expect(primary!.from.getFullYear()).toBe(2022);
      expect(primary!.to.getMonth()).toBe(0); // January
      expect(primary!.to.getFullYear()).toBe(2023);
    });
  });

  describe("Q9M", () => {
    it("returns two ranges", () => {
      const { ranges, primary } = resolveTimePeriod(TimePeriod.Q9M);
      expect(ranges).toHaveLength(2);
      expect(primary).toEqual(ranges[0]);
      // Range A: last 6 months → now
      expect(ranges[0].from.getMonth()).toBe(9); // October 2022
      expect(ranges[0].from.getFullYear()).toBe(2022);
      // Range B: 1 year ago → 1 year ago + 3 months
      expect(ranges[1].from.getMonth()).toBe(3); // April 2022
      expect(ranges[1].from.getFullYear()).toBe(2022);
      expect(ranges[1].to.getMonth()).toBe(6); // July 2022
    });
  });

  describe("CustomPeriod", () => {
    it("returns provided dates when given", () => {
      const from = new Date("2023-01-01");
      const to = new Date("2023-03-31");
      const { ranges, primary } = resolveTimePeriod(TimePeriod.CustomPeriod, from, to);
      expect(ranges).toHaveLength(1);
      expect(primary!.from).toEqual(from);
      expect(primary!.to).toEqual(to);
    });

    it("returns empty ranges when no custom dates provided", () => {
      const { ranges, primary } = resolveTimePeriod(TimePeriod.CustomPeriod);
      expect(ranges).toHaveLength(0);
      expect(primary).toBeNull();
    });
  });
});
```

- [ ] **Step 2: Run test to confirm it FAILS (files don't exist yet)**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npm test -- --testPathPattern="timePeriod/__tests__/resolve" --watchAll=false 2>&1 | tail -20
```

Expected: FAIL with "Cannot find module".

- [ ] **Step 3: Create `timePeriod.ts`**

```typescript
export enum TimePeriod {
  PreviousQuarter = "PreviousQuarter",
  FutureQuarter = "FutureQuarter",
  Y2Y = "Y2Y",
  PreviousSeason = "PreviousSeason",
  Q9M = "Q9M",
  CustomPeriod = "CustomPeriod",
}

export interface DateRange {
  from: Date;
  to: Date;
}
```

- [ ] **Step 4: Create `resolve.ts`**

```typescript
import { TimePeriod, DateRange } from "./timePeriod";

export interface TimePeriodResult {
  ranges: DateRange[];
  primary: DateRange | null;
}

export function resolveTimePeriod(
  period: TimePeriod,
  customFrom?: Date | null,
  customTo?: Date | null,
): TimePeriodResult {
  const now = new Date();

  switch (period) {
    case TimePeriod.PreviousQuarter: {
      const startOfCurrentMonth = new Date(now.getFullYear(), now.getMonth(), 1);
      const endOfPreviousMonth = new Date(startOfCurrentMonth.getTime() - 1);
      const startOfPreviousQuarter = new Date(
        startOfCurrentMonth.getFullYear(),
        startOfCurrentMonth.getMonth() - 3,
        1,
      );
      return toResult([{ from: startOfPreviousQuarter, to: endOfPreviousMonth }]);
    }

    case TimePeriod.FutureQuarter: {
      const startOfFutureQuarterLastYear = new Date(now.getFullYear() - 1, now.getMonth(), 1);
      const endOfFutureQuarterLastYear = new Date(
        now.getFullYear() - 1,
        now.getMonth() + 3,
        0,
      );
      return toResult([{ from: startOfFutureQuarterLastYear, to: endOfFutureQuarterLastYear }]);
    }

    case TimePeriod.Y2Y: {
      const startOfY2Y = new Date(now.getFullYear(), now.getMonth() - 12, 1);
      const endOfY2Y = new Date(now.getFullYear(), now.getMonth(), 0);
      return toResult([{ from: startOfY2Y, to: endOfY2Y }]);
    }

    case TimePeriod.PreviousSeason: {
      const seasonStart = new Date(now.getFullYear() - 1, 9, 1);
      const seasonEnd = new Date(now.getFullYear(), 0, 31);
      return toResult([{ from: seasonStart, to: seasonEnd }]);
    }

    case TimePeriod.Q9M: {
      const sixMonthsAgo = new Date(now.getFullYear(), now.getMonth() - 6, now.getDate());
      const oneYearAgo = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
      const oneYearAgoPlus3 = new Date(now.getFullYear() - 1, now.getMonth() + 3, now.getDate());
      return toResult([
        { from: sixMonthsAgo, to: now },
        { from: oneYearAgo, to: oneYearAgoPlus3 },
      ]);
    }

    case TimePeriod.CustomPeriod: {
      if (customFrom && customTo) {
        return toResult([{ from: customFrom, to: customTo }]);
      }
      return { ranges: [], primary: null };
    }

    default: {
      const startOfCurrentMonth = new Date(now.getFullYear(), now.getMonth(), 1);
      const endOfPreviousMonth = new Date(startOfCurrentMonth.getTime() - 1);
      const startOfPreviousQuarter = new Date(
        startOfCurrentMonth.getFullYear(),
        startOfCurrentMonth.getMonth() - 3,
        1,
      );
      return toResult([{ from: startOfPreviousQuarter, to: endOfPreviousMonth }]);
    }
  }
}

function toResult(ranges: DateRange[]): TimePeriodResult {
  return { ranges, primary: ranges[0] ?? null };
}
```

- [ ] **Step 5: Create `displayText.ts`**

```typescript
import { TimePeriod } from "./timePeriod";

export function getTimePeriodDisplayText(period: TimePeriod): string {
  switch (period) {
    case TimePeriod.PreviousQuarter:
      return "Minulý kvartal";
    case TimePeriod.FutureQuarter:
      return "Budoucí kvartal";
    case TimePeriod.Y2Y:
      return "Y2Y (12 měsíců)";
    case TimePeriod.PreviousSeason:
      return "Předchozí sezona";
    case TimePeriod.Q9M:
      return "9M (6 měsíců + prognóza 3 měsíce)";
    case TimePeriod.CustomPeriod:
      return "Vlastní období";
    default:
      return "9M (6 měsíců + prognóza 3 měsíce)";
  }
}
```

- [ ] **Step 6: Create `index.ts`**

```typescript
export { TimePeriod } from "./timePeriod";
export type { DateRange } from "./timePeriod";
export { resolveTimePeriod } from "./resolve";
export type { TimePeriodResult } from "./resolve";
export { getTimePeriodDisplayText } from "./displayText";
```

- [ ] **Step 7: Run tests — expect PASS**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npm test -- --testPathPattern="timePeriod/__tests__/resolve" --watchAll=false 2>&1 | tail -20
```

Expected: all tests PASS.

- [ ] **Step 8: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add frontend/src/utils/timePeriod/
git commit -m "feat: add shared FE time-period utility module"
```

---

## Task 14: Update useManufacturingStockAnalysis.ts and its tests

**Files:**
- Modify: `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`
- Modify: `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx`

- [ ] **Step 1: Update the hook file**

In `useManufacturingStockAnalysis.ts`:

1. Add import at the top:
```typescript
import {
  TimePeriod,
  resolveTimePeriod,
  getTimePeriodDisplayText as getTimePeriodDisplayTextFromModule,
} from "../../utils/timePeriod";
```

2. Delete the duplicated enum (lines 22-29):
```typescript
// DELETE THIS ENTIRE BLOCK:
export enum TimePeriodFilter {
  PreviousQuarter = "PreviousQuarter",
  FutureQuarter = "FutureQuarter",
  Y2Y = "Y2Y",
  PreviousSeason = "PreviousSeason",
  Q9M = "Q9M",
  CustomPeriod = "CustomPeriod",
}
```

3. After the import statement, add a re-export for backward compatibility (the test and page files still import `TimePeriodFilter` from this module):
```typescript
export { TimePeriod } from "../../utils/timePeriod";
// Backward-compat alias — consumers can migrate to TimePeriod directly
export { TimePeriod as TimePeriodFilter } from "../../utils/timePeriod";
```

4. Update the request interface to use `TimePeriod`:
```typescript
export interface GetManufacturingStockAnalysisRequest {
  timePeriod?: TimePeriod;
  // ... rest unchanged
```

5. In the hook function body, the check `request.timePeriod !== TimePeriodFilter.Q9M` remains valid because `TimePeriodFilter` is now an alias for `TimePeriod`.

6. Delete the duplicated `getTimePeriodDisplayText` function (lines 244-264). Replace its export with:
```typescript
export { getTimePeriodDisplayText } from "../../utils/timePeriod";
```

7. Delete the duplicated `calculateTimePeriodRange` function (lines 266-356). Export the shared resolver instead:
```typescript
export { resolveTimePeriod as calculateTimePeriodRange } from "../../utils/timePeriod";
```

Note: The return shape changes slightly — `calculateTimePeriodRange` used to return `{ fromDate, toDate, ranges? }` and the new `resolveTimePeriod` returns `{ ranges, primary }`. The test at `useManufacturingStockAnalysis.test.tsx` imports and tests `calculateTimePeriodRange` directly — we'll update it next. The ManufacturingStockAnalysis.tsx page uses `calculateTimePeriodRange` from this hook — check if it uses `.fromDate`/`.toDate` or `.ranges` and update accordingly in Task 15.

Actually — because the return shape changes, using a simple re-export alias won't work. Instead, provide a compatibility wrapper:

```typescript
import { resolveTimePeriod, TimePeriod, DateRange } from "../../utils/timePeriod";

export function calculateTimePeriodRange(period: TimePeriod): {
  fromDate: Date | null;
  toDate: Date | null;
  ranges?: Array<{ fromDate: Date; toDate: Date }>;
} {
  const result = resolveTimePeriod(period);
  return {
    fromDate: result.primary?.from ?? null,
    toDate: result.primary?.to ?? null,
    ranges: result.ranges.length > 1
      ? result.ranges.map(r => ({ fromDate: r.from, toDate: r.to }))
      : undefined,
  };
}
```

This wrapper preserves the existing return shape so `ManufacturingStockAnalysis.tsx` and the existing tests don't need updating for the range shape. Only Task 15 (if it accesses internal Q9M range data) needs to be verified.

- [ ] **Step 2: Run hook tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npm test -- --testPathPattern="useManufacturingStockAnalysis" --watchAll=false 2>&1 | tail -20
```

Expected: all PASS. If `calculateTimePeriodRange` tests fail due to shape changes, update assertions to match the new wrapper's output (fromDate → `primary.from`, ranges shape → `{ fromDate, toDate }`).

- [ ] **Step 3: Fix test file if needed**

In `useManufacturingStockAnalysis.test.tsx`, the `calculateTimePeriodRange` tests use:
- `result.fromDate` — maps to `result.primary?.from` — still present in wrapper ✓
- `result.toDate` — maps to `result.primary?.to` — still present ✓
- `result.ranges![0].fromDate` — the wrapper returns `{ fromDate, toDate }` so this still works ✓

The test should pass as-is. If it doesn't, adjust the assertions to match the wrapper output.

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add frontend/src/api/hooks/useManufacturingStockAnalysis.ts
git add frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx
git commit -m "refactor: replace duplicate enum and date-range logic in useManufacturingStockAnalysis"
```

---

## Task 15: Update ManufacturingStockAnalysis.tsx imports

**Files:**
- Modify: `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`

- [ ] **Step 1: Update the import line**

The page currently imports from `useManufacturingStockAnalysis`:
```typescript
import {
  useManufacturingStockAnalysisQuery,
  GetManufacturingStockAnalysisRequest,
  TimePeriodFilter,
  ManufacturingStockSortBy,
  ManufacturingStockSeverity,
  formatNumber,
  formatPercentage,
  formatWarehouseStock,
  calculateTimePeriodRange,
  getTimePeriodDisplayText,
} from "../../api/hooks/useManufacturingStockAnalysis";
```

This import stays **unchanged** — the hook re-exports everything under the same names. No behavior change needed in the page body.

- [ ] **Step 2: Verify the page compiles**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npx tsc --noEmit 2>&1 | grep "ManufacturingStockAnalysis" | head -20
```

Expected: no errors related to this file.

---

## Task 16: Update PurchaseStockAnalysis.tsx — replace inline date math

**Files:**
- Modify: `frontend/src/components/pages/PurchaseStockAnalysis.tsx`

- [ ] **Step 1: Add import**

Find the import section at the top of `PurchaseStockAnalysis.tsx` and add:
```typescript
import { resolveTimePeriod, TimePeriod } from "../../utils/timePeriod";
```

- [ ] **Step 2: Replace `handleQuickDateRange` (lines 179-214)**

Replace the entire function:
```typescript
// OLD
const handleQuickDateRange = (
  type: "last12months" | "previousQuarter" | "nextQuarter",
) => {
  const now = new Date();
  let fromDate: Date;
  let toDate: Date;

  switch (type) {
    case "last12months":
      fromDate = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
      toDate = new Date();
      break;
    case "previousQuarter":
      fromDate = new Date(now.getFullYear(), now.getMonth() - 3, 1);
      toDate = new Date(now.getFullYear(), now.getMonth(), 0);
      break;
    case "nextQuarter":
      const lastYear = now.getFullYear() - 1;
      fromDate = new Date(lastYear, now.getMonth(), 1);
      toDate = new Date(lastYear, now.getMonth() + 3, 0);
      break;
    default:
      return;
  }

  handleFilterChange({ fromDate, toDate });
};
```

With:
```typescript
// NEW
const QUICK_RANGE_PERIODS: Record<string, TimePeriod> = {
  last12months: TimePeriod.Y2Y,
  previousQuarter: TimePeriod.PreviousQuarter,
  nextQuarter: TimePeriod.FutureQuarter,
};

const handleQuickDateRange = (
  type: "last12months" | "previousQuarter" | "nextQuarter",
) => {
  const period = QUICK_RANGE_PERIODS[type];
  if (!period) return;
  const { primary } = resolveTimePeriod(period);
  if (!primary) return;
  handleFilterChange({ fromDate: primary.from, toDate: primary.to });
};
```

- [ ] **Step 3: Replace `getDateRangeTooltip` (lines 216-250)**

Replace:
```typescript
// OLD
const getDateRangeTooltip = (
  type: "last12months" | "previousQuarter" | "nextQuarter",
) => {
  const now = new Date();
  let fromDate: Date;
  let toDate: Date;

  switch (type) {
    case "last12months":
      fromDate = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
      toDate = new Date();
      break;
    case "previousQuarter":
      fromDate = new Date(now.getFullYear(), now.getMonth() - 3, 1);
      toDate = new Date(now.getFullYear(), now.getMonth(), 0);
      break;
    case "nextQuarter":
      const lastYear = now.getFullYear() - 1;
      fromDate = new Date(lastYear, now.getMonth(), 1);
      toDate = new Date(lastYear, now.getMonth() + 3, 0);
      break;
    default:
      return "";
  }

  return `${fromDate.toLocaleDateString("cs-CZ")} - ${toDate.toLocaleDateString("cs-CZ")}`;
};
```

With:
```typescript
// NEW
const getDateRangeTooltip = (
  type: "last12months" | "previousQuarter" | "nextQuarter",
): string => {
  const period = QUICK_RANGE_PERIODS[type];
  if (!period) return "";
  const { primary } = resolveTimePeriod(period);
  if (!primary) return "";
  return `${primary.from.toLocaleDateString("cs-CZ")} - ${primary.to.toLocaleDateString("cs-CZ")}`;
};
```

- [ ] **Step 4: TypeScript check**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npx tsc --noEmit 2>&1 | grep "PurchaseStockAnalysis" | head -10
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add frontend/src/components/pages/PurchaseStockAnalysis.tsx
git commit -m "refactor: replace inline date-math in PurchaseStockAnalysis with shared resolver"
```

---

## Task 17: Update ManufactureBatchPlanning.tsx — replace inline date math and default

**Files:**
- Modify: `frontend/src/components/pages/ManufactureBatchPlanning.tsx`

- [ ] **Step 1: Add import**

```typescript
import { resolveTimePeriod, TimePeriod } from "../../utils/timePeriod";
```

- [ ] **Step 2: Replace default fromDate state (line ~50)**

Change:
```typescript
const [fromDate, setFromDate] = useState<Date>(new Date(new Date().getFullYear() - 1, new Date().getMonth(), new Date().getDate()));
```
To:
```typescript
const [fromDate, setFromDate] = useState<Date>(
  () => resolveTimePeriod(TimePeriod.Y2Y).primary?.from ?? new Date(new Date().getFullYear() - 1, new Date().getMonth(), 1)
);
```

- [ ] **Step 3: Replace `handleQuickDateRange` (lines ~236-265)**

The old function:
```typescript
const handleQuickDateRange = (type: "lastq" | "y2y" | "nextq") => {
  const now = new Date();
  let fromDateNew: Date;
  let toDateNew: Date;

  switch (type) {
    case "lastq":
      fromDateNew = new Date(now.getFullYear(), now.getMonth() - 3, 1);
      toDateNew = new Date(now.getFullYear(), now.getMonth(), 0);
      break;
    case "y2y":
      fromDateNew = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
      toDateNew = new Date();
      break;
    case "nextq":
      const lastYear = now.getFullYear() - 1;
      fromDateNew = new Date(lastYear, now.getMonth(), 1);
      toDateNew = new Date(lastYear, now.getMonth() + 3, 0);
      break;
  }

  setFromDate(fromDateNew);
  setToDate(toDateNew);
  setNeedsRecalculation(true);
};
```

Replace with:
```typescript
const BATCH_PLANNING_PERIODS: Record<string, TimePeriod> = {
  lastq: TimePeriod.PreviousQuarter,
  y2y: TimePeriod.Y2Y,
  nextq: TimePeriod.FutureQuarter,
};

const handleQuickDateRange = (type: "lastq" | "y2y" | "nextq") => {
  const period = BATCH_PLANNING_PERIODS[type];
  if (!period) return;
  const { primary } = resolveTimePeriod(period);
  if (!primary) return;
  setFromDate(primary.from);
  setToDate(primary.to);
  setNeedsRecalculation(true);
};
```

- [ ] **Step 4: Replace `getDateRangeTooltip` (lines ~267-290)**

Old:
```typescript
const getDateRangeTooltip = (type: "lastq" | "y2y" | "nextq") => {
  const now = new Date();
  let fromDateNew: Date;
  let toDateNew: Date;

  switch (type) {
    case "lastq":
      fromDateNew = new Date(now.getFullYear(), now.getMonth() - 3, 1);
      toDateNew = new Date(now.getFullYear(), now.getMonth(), 0);
      break;
    case "y2y":
      fromDateNew = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
      toDateNew = new Date();
      break;
    case "nextq":
      const lastYear = now.getFullYear() - 1;
      fromDateNew = new Date(lastYear, now.getMonth(), 1);
      toDateNew = new Date(lastYear, now.getMonth() + 3, 0);
      break;
  }

  return `${fromDateNew.toLocaleDateString("cs-CZ")} - ${toDateNew.toLocaleDateString("cs-CZ")}`;
};
```

Replace with:
```typescript
const getDateRangeTooltip = (type: "lastq" | "y2y" | "nextq"): string => {
  const period = BATCH_PLANNING_PERIODS[type];
  if (!period) return "";
  const { primary } = resolveTimePeriod(period);
  if (!primary) return "";
  return `${primary.from.toLocaleDateString("cs-CZ")} - ${primary.to.toLocaleDateString("cs-CZ")}`;
};
```

- [ ] **Step 5: TypeScript check**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npx tsc --noEmit 2>&1 | grep "ManufactureBatchPlanning" | head -10
```

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add frontend/src/components/pages/ManufactureBatchPlanning.tsx
git commit -m "refactor: replace inline date-math in ManufactureBatchPlanning with shared resolver"
```

---

## Task 18: Update GiftPackageManufacturingFilters.tsx — replace inline date math

**Files:**
- Modify: `frontend/src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingFilters.tsx`

- [ ] **Step 1: Add import**

```typescript
import { resolveTimePeriod, TimePeriod } from "../../../utils/timePeriod";
```

- [ ] **Step 2: Replace `handleQuickDateRange` (lines 15-48)**

Old:
```typescript
const handleQuickDateRange = (
  type: "last12months" | "previousQuarter" | "nextQuarter",
) => {
  const now = new Date();
  let fromDate: Date;
  let toDate: Date;

  switch (type) {
    case "last12months":
      fromDate = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
      toDate = new Date();
      break;
    case "previousQuarter":
      fromDate = new Date(now.getFullYear(), now.getMonth() - 3, 1);
      toDate = new Date(now.getFullYear(), now.getMonth(), 0);
      break;
    case "nextQuarter":
      const lastYear = now.getFullYear() - 1;
      fromDate = new Date(lastYear, now.getMonth(), 1);
      toDate = new Date(lastYear, now.getMonth() + 3, 0);
      break;
    default:
      return;
  }

  onFilterChange({ fromDate, toDate });
};
```

Replace with:
```typescript
const GIFT_PACKAGE_PERIODS: Record<string, TimePeriod> = {
  last12months: TimePeriod.Y2Y,
  previousQuarter: TimePeriod.PreviousQuarter,
  nextQuarter: TimePeriod.FutureQuarter,
};

const handleQuickDateRange = (
  type: "last12months" | "previousQuarter" | "nextQuarter",
) => {
  const period = GIFT_PACKAGE_PERIODS[type];
  if (!period) return;
  const { primary } = resolveTimePeriod(period);
  if (!primary) return;
  onFilterChange({ fromDate: primary.from, toDate: primary.to });
};
```

- [ ] **Step 3: Replace `getDateRangeTooltip` (lines 50-84)**

Old:
```typescript
const getDateRangeTooltip = (
  type: "last12months" | "previousQuarter" | "nextQuarter",
) => {
  const now = new Date();
  let fromDate: Date;
  let toDate: Date;

  switch (type) {
    case "last12months":
      fromDate = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
      toDate = new Date();
      break;
    case "previousQuarter":
      fromDate = new Date(now.getFullYear(), now.getMonth() - 3, 1);
      toDate = new Date(now.getFullYear(), now.getMonth(), 0);
      break;
    case "nextQuarter":
      const lastYear = now.getFullYear() - 1;
      fromDate = new Date(lastYear, now.getMonth(), 1);
      toDate = new Date(lastYear, now.getMonth() + 3, 0);
      break;
    default:
      return "";
  }

  return `${fromDate.toLocaleDateString("cs-CZ")} - ${toDate.toLocaleDateString("cs-CZ")}`;
};
```

Replace with:
```typescript
const getDateRangeTooltip = (
  type: "last12months" | "previousQuarter" | "nextQuarter",
): string => {
  const period = GIFT_PACKAGE_PERIODS[type];
  if (!period) return "";
  const { primary } = resolveTimePeriod(period);
  if (!primary) return "";
  return `${primary.from.toLocaleDateString("cs-CZ")} - ${primary.to.toLocaleDateString("cs-CZ")}`;
};
```

- [ ] **Step 4: TypeScript check**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npx tsc --noEmit 2>&1 | grep "GiftPackage" | head -10
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add frontend/src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingFilters.tsx
git commit -m "refactor: replace inline date-math in GiftPackageManufacturingFilters with shared resolver"
```

---

## Task 19: Final verification

- [ ] **Step 1: Full BE build and format**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet build && dotnet format
```

Expected: 0 errors, 0 warnings (or only pre-existing warnings).

- [ ] **Step 2: Full BE test suite**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend && dotnet test 2>&1 | tail -20
```

Expected: **0 failed**. Check total count — should be similar to before (no tests removed unexpectedly).

- [ ] **Step 3: Full FE build and lint**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npm run build && npm run lint
```

Expected: 0 errors, 0 lint warnings on modified files.

- [ ] **Step 4: Full FE test suite**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npm test -- --watchAll=false 2>&1 | tail -20
```

Expected: **0 failed**.

- [ ] **Step 5: Grep guard — no orphan references**

```bash
grep -rn "TimePeriodFilter\|ITimePeriodCalculator\|TimePeriodCalculator\b" \
  /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/src \
  /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend/src \
  --include="*.cs" --include="*.ts" --include="*.tsx"
```

Expected: **no results** (or only the re-export alias line in `useManufacturingStockAnalysis.ts`).

```bash
grep -rn "handleQuickDateRange\b" \
  /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend/src \
  --include="*.ts" --include="*.tsx"
```

Expected: the remaining `handleQuickDateRange` calls in the three modified pages — but no inline `switch` blocks with `"last12months"/"previousQuarter"/"nextQuarter"/"lastq"/"y2y"/"nextq"` string literals that contain raw date math.

Verify inline date math is gone:
```bash
grep -rn "getFullYear.*- 1.*getMonth\|getMonth.*- 3\|getMonth.*- 12" \
  /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend/src/components/pages/PurchaseStockAnalysis.tsx \
  /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend/src/components/pages/ManufactureBatchPlanning.tsx \
  /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend/src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingFilters.tsx
```

Expected: **no results**.
