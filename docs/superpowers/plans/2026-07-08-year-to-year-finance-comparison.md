# Year-over-Year Financial Comparison Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "year-over-year" view mode to the Finanční přehled page that lines up the same month across 2–3 years, with the current partial month cut at `today − 5 days` and that same day-cut mirrored into prior years' same month for a fair comparison.

**Architecture:** New MediatR endpoint `GET /api/FinancialOverview/comparison` returns one series per year (each a list of `MonthlyFinancialDataDto` cells). Completed months reuse the existing per-month cache; the partial month is computed live for every year using a new period-based stock primitive. The existing timeline endpoint/UI is untouched; the page gains a view-mode toggle that pivots the per-year series onto a fixed 12-month x-axis.

**Tech Stack:** .NET 8, MediatR, MVC controllers, IMemoryCache, xUnit + Moq + FluentAssertions (BE); React 18, @tanstack/react-query, Chart.js + react-chartjs-2, Tailwind, react-scripts/Jest (FE); NSwag-generated TS client.

---

## File Structure

**Backend — new files** (`backend/src/Anela.Heblo.Application/Features/FinancialOverview/`)
- `GetFinancialComparisonRequest.cs` — MediatR request DTO.
- `GetFinancialComparisonHandler.cs` — thin handler → service.
- `GetFinancialComparisonResponse.cs` — `: BaseResponse`, holds series + metadata.
- `Model/YearComparisonSeriesDto.cs` — one year's cells + per-year YTD totals.
- `Model/FinancialComparisonMetadataDto.cs` — cutoff/anchor/years metadata.

**Backend — modified files**
- `Model/MonthlyFinancialDataDto.cs` — add `IsPartial`, `PartialDayOfMonth`.
- `FinancialAnalysisOptions.cs` — add `PartialMonthLagDays`.
- `Domain/Features/FinancialOverview/IStockValueService.cs` — add `GetStockValueChangeForPeriodAsync`.
- `Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs` — implement it.
- `Services/IFinancialAnalysisService.cs` + `Services/FinancialAnalysisService.cs` — add `GetFinancialComparisonAsync` + private helpers.
- `API/Controllers/FinancialOverviewController.cs` — add `[HttpGet("comparison")]` action.

**Frontend — new files** (`frontend/src/`)
- `api/hooks/useFinancialComparison.ts` — react-query hook.
- `components/pages/financial-overview/FinancialComparisonChart.tsx` — one line per year.
- `components/pages/financial-overview/FinancialComparisonTable.tsx` — month × year pivot with Δ.
- `components/pages/financial-overview/comparisonUtils.ts` — metric union, month labels, colors, pivot helpers.

**Frontend — modified files**
- `api/client.ts` — add `financialComparison` query key.
- `components/pages/financial-overview/utils.ts` — add `FinancialViewMode`.
- `components/pages/financial-overview/FinancialFilters.tsx` — view-mode + years + metric controls.
- `components/pages/FinancialOverview.tsx` — wire the comparison branch.

**Design note (deviation from the high-level plan, intentional — KISS/YAGNI):** partial months are **computed live, never cached**. This removes the cache-poisoning risk entirely (the full-month cache keys are never written by the comparison path) at the cost of a few extra ERP calls per request (only 2–3 partial cells). The dedicated `financial_partial_*` cache keys and `PartialCacheTtlMinutes`/`ComparisonYears` options from the high-level plan are therefore **not** added.

---

## Backend

### Task 1: Add partial-month markers to the shared cell DTO

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/MonthlyFinancialDataDto.cs`

This is a pure data holder (no behavior) so it needs no dedicated test; it is exercised by the service tests in Task 4.

- [ ] **Step 1: Add the two nullable fields**

Append these properties inside the `MonthlyFinancialDataDto` class, after the existing `TotalBalance` property (keep them nullable so the existing overview endpoint's serialization is unchanged — it leaves them unset/`null`):

```csharp
    /// <summary>
    /// True when this cell covers a partial month (cut at PartialDayOfMonth).
    /// Null/unset for the standard full-month overview endpoint.
    /// </summary>
    public bool? IsPartial { get; set; }

    /// <summary>
    /// The inclusive day-of-month the partial cell was cut at (e.g. 3 => 1st–3rd).
    /// Null unless IsPartial is true.
    /// </summary>
    public int? PartialDayOfMonth { get; set; }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/MonthlyFinancialDataDto.cs
git commit -m "feat: add partial-month markers to MonthlyFinancialDataDto"
```

---

### Task 2: Add a period-based stock-change primitive

The existing `IStockValueService` only computes whole-month changes. Partial-month stock needs `value(periodEnd) − value(periodStart)`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/FinancialOverview/IStockValueService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/StockValueServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test to `StockValueServiceTests` (it reuses the existing mock fields). It seeds Materials warehouse (ID 5) at Jan 1 and Jan 10 and asserts the change equals `(end − start) × price`, proving the period end date drives the "end" snapshot:

```csharp
    [Fact]
    public async Task GetStockValueChangeForPeriodAsync_UsesPeriodEndDate_ForEndSnapshot()
    {
        // Arrange
        var periodStart = new DateTime(2025, 7, 1);
        var periodEnd = new DateTime(2025, 7, 10); // partial month cut at day 10

        _priceClientMock.Setup(x => x.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new() { ProductCode = "MAT001", PurchasePrice = 100m }
            });

        // Every warehouse/date returns empty by default...
        _stockClientMock.Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>());
        // ...except Materials (ID 5) at the two period boundaries.
        _stockClientMock.Setup(x => x.StockToDateAsync(periodStart, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock> { new() { ProductCode = "MAT001", Stock = 10m } });
        _stockClientMock.Setup(x => x.StockToDateAsync(periodEnd, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock> { new() { ProductCode = "MAT001", Stock = 13m } });

        // Act
        var result = await _service.GetStockValueChangeForPeriodAsync(periodStart, periodEnd, CancellationToken.None);

        // Assert
        result.Year.Should().Be(2025);
        result.Month.Should().Be(7);
        result.StockChanges.Materials.Should().Be(300m); // (13 - 10) * 100
        result.TotalStockValueChange.Should().Be(300m);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockValueServiceTests.GetStockValueChangeForPeriodAsync_UsesPeriodEndDate_ForEndSnapshot" -p:UseSharedCompilation=false`
Expected: FAIL — `IStockValueService` does not contain `GetStockValueChangeForPeriodAsync` (compile error).

- [ ] **Step 3: Add the interface method**

In `IStockValueService.cs`, add inside the interface:

```csharp
    /// <summary>
    /// Gets the stock value change for an arbitrary period: value(periodEndInclusive) - value(periodStart).
    /// Year/Month on the result are taken from periodStart. Used for partial-month (fair-cut) comparison.
    /// </summary>
    Task<MonthlyStockChange> GetStockValueChangeForPeriodAsync(
        DateTime periodStart,
        DateTime periodEndInclusive,
        CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement it in the adapter**

In `FinancialOverviewStockValueAdapter.cs`, add this method (reuses the existing private `GetWarehouseStockValueAsync` and warehouse-id constants; does not touch the existing whole-month method):

```csharp
    public async Task<MonthlyStockChange> GetStockValueChangeForPeriodAsync(
        DateTime periodStart,
        DateTime periodEndInclusive,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calculating partial stock value change for period {Start:d}..{End:d}",
            periodStart, periodEndInclusive);

        var prices = await _priceClient.GetAllAsync(forceReload: false, cancellationToken);
        var priceDict = prices.ToDictionary(p => p.ProductCode, p => p.PurchasePrice);

        var startTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, periodStart, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, periodStart, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, periodStart, priceDict, cancellationToken)
        };

        var endTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, periodEndInclusive, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, periodEndInclusive, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, periodEndInclusive, priceDict, cancellationToken)
        };

        await Task.WhenAll(startTasks.Concat(endTasks));

        var startValues = await Task.WhenAll(startTasks);
        var endValues = await Task.WhenAll(endTasks);

        return new MonthlyStockChange
        {
            Year = periodStart.Year,
            Month = periodStart.Month,
            StockChanges = new StockChangeByType
            {
                Materials = endValues[0] - startValues[0],
                SemiProducts = endValues[1] - startValues[1],
                Products = endValues[2] - startValues[2]
            }
        };
    }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockValueServiceTests.GetStockValueChangeForPeriodAsync_UsesPeriodEndDate_ForEndSnapshot" -p:UseSharedCompilation=false`
Expected: PASS (1 passed).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/FinancialOverview/IStockValueService.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs \
        backend/test/Anela.Heblo.Tests/Application/FinancialOverview/StockValueServiceTests.cs
git commit -m "feat: add period-based stock value change primitive"
```

---

### Task 3: Comparison request/response/metadata DTOs and options

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/GetFinancialComparisonRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/GetFinancialComparisonResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/YearComparisonSeriesDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/FinancialComparisonMetadataDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialAnalysisOptions.cs`

> These are plain DTOs (classes, never records — the OpenAPI generator mishandles record parameter order; see CLAUDE.md). No dedicated test; exercised by Task 4.

- [ ] **Step 1: Create the request**

`GetFinancialComparisonRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialComparisonRequest : IRequest<GetFinancialComparisonResponse>
{
    /// <summary>Number of years to compare. Clamped to 2..3 by the service.</summary>
    public int? Years { get; set; } = 3;

    public bool IncludeStockData { get; set; } = true;

    public List<string>? ExcludedDepartments { get; set; }

    /// <summary>When true, includes the partial cutoff month (cut at today - lag days) for every year.</summary>
    public bool IncludePartialMonth { get; set; } = true;
}
```

- [ ] **Step 2: Create the per-year series DTO**

`Model/YearComparisonSeriesDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

public class YearComparisonSeriesDto
{
    [Required]
    public int Year { get; set; }

    /// <summary>Cells for this year, ascending by month, only months that have data.</summary>
    [Required]
    public List<MonthlyFinancialDataDto> Months { get; set; } = new();

    [Required]
    public decimal YtdIncome { get; set; }

    [Required]
    public decimal YtdExpenses { get; set; }

    [Required]
    public decimal YtdFinancialBalance { get; set; }

    public decimal? YtdStockValueChange { get; set; }

    public decimal? YtdTotalBalance { get; set; }
}
```

- [ ] **Step 3: Create the metadata DTO**

`Model/FinancialComparisonMetadataDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

public class FinancialComparisonMetadataDto
{
    [Required]
    public DateTime CutoffDate { get; set; }

    [Required]
    public int AnchorYear { get; set; }

    [Required]
    public int PartialMonth { get; set; }

    [Required]
    public int PartialDayOfMonth { get; set; }

    [Required]
    public List<int> Years { get; set; } = new();

    [Required]
    public bool IncludeStockData { get; set; }

    [Required]
    public bool PartialMonthIncluded { get; set; }

    [Required]
    public int LagDays { get; set; }
}
```

- [ ] **Step 4: Create the response**

`GetFinancialComparisonResponse.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialComparisonResponse : BaseResponse
{
    /// <summary>One series per year, descending by year (anchor year first).</summary>
    [Required]
    public List<YearComparisonSeriesDto> Series { get; set; } = new();

    [Required]
    public FinancialComparisonMetadataDto Metadata { get; set; } = new();
}
```

- [ ] **Step 5: Add the lag-days option**

In `FinancialAnalysisOptions.cs`, add the property inside the class:

```csharp
    /// <summary>
    /// Days subtracted from today to pick the comparison cutoff date, absorbing ERP data lag.
    /// The partial (cutoff) month is cut at this date's day-of-month across every compared year.
    /// </summary>
    public int PartialMonthLagDays { get; set; } = 5;
```

- [ ] **Step 6: Build to verify it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/GetFinancialComparisonRequest.cs \
        backend/src/Anela.Heblo.Application/Features/FinancialOverview/GetFinancialComparisonResponse.cs \
        backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/YearComparisonSeriesDto.cs \
        backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/FinancialComparisonMetadataDto.cs \
        backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialAnalysisOptions.cs
git commit -m "feat: add financial comparison DTOs and cutoff-lag option"
```

---

### Task 4: Comparison service method

Add `GetFinancialComparisonAsync` to the service. Algorithm per year × month:
- Skip anchor-year months after the partial month (future, no data).
- The `partialMonth` cell (every year) is cut at `min(cutoffDay, daysInMonth)`; computed live; marked `IsPartial`.
- Other months: read the existing per-month cache when present and no department filter; otherwise compute that single month live with in-memory department filtering.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

First, add a default mock for the new stock primitive to the existing constructor of `FinancialAnalysisServiceTests` (place it right after the existing `GetStockValueChangesAsync` default setup so partial cells don't NRE):

```csharp
        // Default: partial stock primitive returns a zero change stamped with the period's year/month
        _stockValueServiceMock
            .Setup(x => x.GetStockValueChangeForPeriodAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime start, DateTime _, CancellationToken __) =>
                new MonthlyStockChange { Year = start.Year, Month = start.Month });
```

Then add these tests to the class:

```csharp
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(9, 3)]
    public async Task GetFinancialComparisonAsync_ClampsYearsToBetween2And3(int requested, int expectedYears)
    {
        var response = await _service.GetFinancialComparisonAsync(
            years: requested, includeStockData: false, excludedDepartments: null, includePartialMonth: true);

        response.Series.Should().HaveCount(expectedYears);
        response.Metadata.Years.Should().HaveCount(expectedYears);
    }

    [Fact]
    public async Task GetFinancialComparisonAsync_PartialMonth_IsCutAtSameDayForEveryYear()
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-5);
        var anchorYear = cutoff.Year;
        var partialMonth = cutoff.Month;
        var cutoffDay = cutoff.Day;

        await _service.GetFinancialComparisonAsync(
            years: 2, includeStockData: false, excludedDepartments: null, includePartialMonth: true);

        // Anchor year: partial month queried through the cutoff date itself.
        var anchorStart = new DateTime(anchorYear, partialMonth, 1);
        _ledgerServiceMock.Verify(x => x.GetLedgerItems(
            anchorStart, cutoff,
            It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Prior year: same month cut at the same day-of-month (clamped to that month's length).
        var priorYear = anchorYear - 1;
        var priorStart = new DateTime(priorYear, partialMonth, 1);
        var priorEnd = new DateTime(priorYear, partialMonth,
            Math.Min(cutoffDay, DateTime.DaysInMonth(priorYear, partialMonth)));
        _ledgerServiceMock.Verify(x => x.GetLedgerItems(
            priorStart, priorEnd,
            It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetFinancialComparisonAsync_MarksPartialCells_AndOmitsAnchorFutureMonths()
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-5);
        var anchorYear = cutoff.Year;

        var response = await _service.GetFinancialComparisonAsync(
            years: 2, includeStockData: true, excludedDepartments: null, includePartialMonth: true);

        var anchorSeries = response.Series.Single(s => s.Year == anchorYear);
        anchorSeries.Months.Should().OnlyContain(m => m.Month <= cutoff.Month);
        anchorSeries.Months.Max(m => m.Month).Should().Be(cutoff.Month);

        var partialCells = response.Series.SelectMany(s => s.Months).Where(m => m.IsPartial == true).ToList();
        partialCells.Should().NotBeEmpty();
        partialCells.Should().OnlyContain(m => m.Month == cutoff.Month);
        partialCells.Should().OnlyContain(m =>
            m.PartialDayOfMonth == Math.Min(cutoff.Day, DateTime.DaysInMonth(m.Year, m.Month)));
    }

    [Fact]
    public async Task GetFinancialComparisonAsync_WhenPartialExcluded_DropsPartialMonthFromEveryYear()
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-5);
        var partialMonth = cutoff.Month;

        var response = await _service.GetFinancialComparisonAsync(
            years: 2, includeStockData: false, excludedDepartments: null, includePartialMonth: false);

        response.Series.Should().HaveCount(2);
        response.Series.SelectMany(s => s.Months).Should().NotContain(m => m.Month == partialMonth);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FinancialAnalysisServiceTests.GetFinancialComparisonAsync" -p:UseSharedCompilation=false`
Expected: FAIL — `IFinancialAnalysisService` has no `GetFinancialComparisonAsync` (compile error).

- [ ] **Step 3: Add the interface method**

In `IFinancialAnalysisService.cs`, add:

```csharp
    Task<GetFinancialComparisonResponse> GetFinancialComparisonAsync(
        int years,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments,
        bool includePartialMonth,
        CancellationToken cancellationToken = default);
```

Ensure the file has `using Anela.Heblo.Application.Features.FinancialOverview;` available (the response type lives in that namespace — the interface is in `...FinancialOverview.Services`, so add `using Anela.Heblo.Application.Features.FinancialOverview;` at the top if not already present).

- [ ] **Step 4: Implement the method + helpers in the service**

In `FinancialAnalysisService.cs`, add the public method and four private helpers (place them after `GetFinancialOverviewAsync`). They reuse the existing `CalculatePeriodTotals`, `MapToDto`, and the existing cache-key prefixes `MONTHLY_DATA_CACHE_KEY_PREFIX` / `STOCK_DATA_CACHE_KEY_PREFIX`:

```csharp
    public async Task<GetFinancialComparisonResponse> GetFinancialComparisonAsync(
        int years,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments,
        bool includePartialMonth,
        CancellationToken cancellationToken = default)
    {
        var n = Math.Clamp(years, 2, 3);
        var cutoffDate = DateTime.UtcNow.Date.AddDays(-_options.PartialMonthLagDays);
        var anchorYear = cutoffDate.Year;
        var partialMonth = cutoffDate.Month;
        var cutoffDay = cutoffDate.Day;

        var excludedSet = excludedDepartments is { Count: > 0 }
            ? excludedDepartments.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        _logger.LogInformation(
            "Financial comparison: {Years} years, anchor {AnchorYear}, cutoff {Cutoff:d}, includeStock={Stock}, includePartial={Partial}, deptFilter={Filter}",
            n, anchorYear, cutoffDate, includeStockData, includePartialMonth, excludedSet != null);

        try
        {
            var yearsList = Enumerable.Range(0, n).Select(i => anchorYear - i).ToList();

            var series = new List<YearComparisonSeriesDto>();
            foreach (var year in yearsList)
            {
                var months = await BuildComparisonYearAsync(
                    year, anchorYear, partialMonth, cutoffDay, cutoffDate,
                    includeStockData, includePartialMonth, excludedSet, cancellationToken);
                series.Add(BuildYearSeries(year, months, partialMonth, includeStockData));
            }

            return new GetFinancialComparisonResponse
            {
                Series = series,
                Metadata = new FinancialComparisonMetadataDto
                {
                    CutoffDate = cutoffDate,
                    AnchorYear = anchorYear,
                    PartialMonth = partialMonth,
                    PartialDayOfMonth = cutoffDay,
                    Years = yearsList,
                    IncludeStockData = includeStockData,
                    PartialMonthIncluded = includePartialMonth,
                    LagDays = _options.PartialMonthLagDays
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build financial comparison for anchor year {AnchorYear}", anchorYear);
            throw;
        }
    }

    private async Task<List<MonthlyFinancialDataDto>> BuildComparisonYearAsync(
        int year, int anchorYear, int partialMonth, int cutoffDay, DateTime cutoffDate,
        bool includeStockData, bool includePartialMonth, HashSet<string>? excludedSet,
        CancellationToken cancellationToken)
    {
        var cells = new List<MonthlyFinancialDataDto>();

        for (int month = 1; month <= 12; month++)
        {
            // Anchor-year months after the partial month are still in the future - no data.
            if (year == anchorYear && month > partialMonth)
                continue;

            var isPartial = month == partialMonth;
            if (isPartial && !includePartialMonth)
                continue;

            var cell = await BuildComparisonCellAsync(
                year, month, isPartial, cutoffDay, cutoffDate,
                includeStockData, excludedSet, cancellationToken);
            cells.Add(cell);
        }

        return cells; // ascending by month
    }

    private async Task<MonthlyFinancialDataDto> BuildComparisonCellAsync(
        int year, int month, bool isPartial, int cutoffDay, DateTime cutoffDate,
        bool includeStockData, HashSet<string>? excludedSet,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var effectiveDay = isPartial ? Math.Min(cutoffDay, daysInMonth) : daysInMonth;

        // Anchor-year partial cell ends exactly on the cutoff date (matches its time component of 00:00).
        var periodEnd = isPartial ? new DateTime(year, month, effectiveDay) : new DateTime(year, month, daysInMonth);

        // Completed full months without a department filter can be served from the existing cache.
        if (!isPartial && excludedSet == null &&
            TryGetCachedComparisonMonth(year, month, includeStockData, out var cachedCell))
        {
            return cachedCell!;
        }

        // Live single-month computation (also the path for partial cells and department-filtered requests).
        var debitTask = _ledgerService.GetLedgerItems(
            monthStart, periodEnd, debitAccountPrefix: new[] { "5", "6" }, cancellationToken: cancellationToken);
        var creditTask = _ledgerService.GetLedgerItems(
            monthStart, periodEnd, creditAccountPrefix: new[] { "5", "6" }, cancellationToken: cancellationToken);

        await Task.WhenAll(debitTask, creditTask);

        var debitItems = FilterExcludedDepartments(await debitTask, excludedSet);
        var creditItems = FilterExcludedDepartments(await creditTask, excludedSet);

        var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);

        MonthlyStockChange? stockChange = null;
        if (includeStockData)
        {
            stockChange = isPartial
                ? await _stockValueService.GetStockValueChangeForPeriodAsync(monthStart, periodEnd, cancellationToken)
                : (await _stockValueService.GetStockValueChangesAsync(monthStart, periodEnd, cancellationToken))
                    .FirstOrDefault();
        }

        var dto = MapToDto(year, month, income, expenses, stockChange, includeStockData);
        if (isPartial)
        {
            dto.IsPartial = true;
            dto.PartialDayOfMonth = effectiveDay;
        }
        return dto;
    }

    private bool TryGetCachedComparisonMonth(
        int year, int month, bool includeStockData, out MonthlyFinancialDataDto? dto)
    {
        dto = null;

        var monthlyKey = $"{MONTHLY_DATA_CACHE_KEY_PREFIX}{year}_{month}";
        if (!_memoryCache.TryGetValue(monthlyKey, out var value) || value is not MonthlyFinancialData data)
            return false;

        MonthlyStockChange? stock = null;
        if (includeStockData)
        {
            var stockKey = $"{STOCK_DATA_CACHE_KEY_PREFIX}{year}_{month}";
            if (_memoryCache.TryGetValue(stockKey, out var s) && s is MonthlyStockChange sc)
                stock = sc;
        }

        dto = MapToDto(data.Year, data.Month, data.Income, data.Expenses, stock, includeStockData);
        return true;
    }

    private static IEnumerable<LedgerItem> FilterExcludedDepartments(
        IReadOnlyList<LedgerItem> items, HashSet<string>? excludedSet)
        => excludedSet == null
            ? items
            : items.Where(item => item.Department == null || !excludedSet.Contains(item.Department));

    private static YearComparisonSeriesDto BuildYearSeries(
        int year, List<MonthlyFinancialDataDto> months, int partialMonth, bool includeStockData)
    {
        var ytdCells = months.Where(m => m.Month <= partialMonth).ToList();

        return new YearComparisonSeriesDto
        {
            Year = year,
            Months = months,
            YtdIncome = ytdCells.Sum(m => m.Income),
            YtdExpenses = ytdCells.Sum(m => m.Expenses),
            YtdFinancialBalance = ytdCells.Sum(m => m.FinancialBalance),
            YtdStockValueChange = includeStockData ? ytdCells.Sum(m => m.TotalStockValueChange ?? 0) : (decimal?)null,
            YtdTotalBalance = includeStockData ? ytdCells.Sum(m => m.TotalBalance ?? m.FinancialBalance) : (decimal?)null
        };
    }
```

> Note: `GetLedgerItems` return type is `IReadOnlyList<LedgerItem>`; `FilterExcludedDepartments` takes that and returns `IEnumerable<LedgerItem>`, which `CalculatePeriodTotals` accepts. `MonthlyFinancialData` and `LedgerItem` are already imported at the top of the service file.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FinancialAnalysisServiceTests.GetFinancialComparisonAsync" -p:UseSharedCompilation=false`
Expected: PASS (7 passed — 4 from the Theory + 3 Facts).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs \
        backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs \
        backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs
git commit -m "feat: add GetFinancialComparisonAsync with fair partial-month cut"
```

---

### Task 5: Comparison handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/GetFinancialComparisonHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/GetFinancialComparisonHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `GetFinancialComparisonHandlerTests.cs` (mirrors `GetFinancialOverviewHandlerTests` style — mocks the service, asserts pass-through and the `Years ?? 3` default):

```csharp
using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.FinancialOverview.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.FinancialOverview;

public class GetFinancialComparisonHandlerTests
{
    private readonly Mock<IFinancialAnalysisService> _serviceMock = new();

    [Fact]
    public async Task Handle_PassesRequestValuesToService_AndDefaultsYearsTo3()
    {
        // Arrange
        var expected = new GetFinancialComparisonResponse();
        _serviceMock
            .Setup(x => x.GetFinancialComparisonAsync(
                3, true, It.IsAny<IReadOnlyList<string>?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new GetFinancialComparisonHandler(
            _serviceMock.Object, NullLogger<GetFinancialComparisonHandler>.Instance);

        // Act
        var result = await handler.Handle(
            new GetFinancialComparisonRequest { Years = null }, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        _serviceMock.Verify(x => x.GetFinancialComparisonAsync(
            3, true, It.IsAny<IReadOnlyList<string>?>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetFinancialComparisonHandlerTests" -p:UseSharedCompilation=false`
Expected: FAIL — `GetFinancialComparisonHandler` does not exist (compile error).

- [ ] **Step 3: Create the handler**

`GetFinancialComparisonHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.FinancialOverview.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialComparisonHandler
    : IRequestHandler<GetFinancialComparisonRequest, GetFinancialComparisonResponse>
{
    private readonly IFinancialAnalysisService _financialAnalysisService;
    private readonly ILogger<GetFinancialComparisonHandler> _logger;

    public GetFinancialComparisonHandler(
        IFinancialAnalysisService financialAnalysisService,
        ILogger<GetFinancialComparisonHandler> logger)
    {
        _financialAnalysisService = financialAnalysisService;
        _logger = logger;
    }

    public async Task<GetFinancialComparisonResponse> Handle(
        GetFinancialComparisonRequest request, CancellationToken cancellationToken)
    {
        var years = request.Years ?? 3;

        _logger.LogInformation(
            "Handling financial comparison request for {Years} years, IncludeStock={Stock}, IncludePartial={Partial}",
            years, request.IncludeStockData, request.IncludePartialMonth);

        return await _financialAnalysisService.GetFinancialComparisonAsync(
            years,
            request.IncludeStockData,
            request.ExcludedDepartments,
            request.IncludePartialMonth,
            cancellationToken);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetFinancialComparisonHandlerTests" -p:UseSharedCompilation=false`
Expected: PASS (1 passed).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/GetFinancialComparisonHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/FinancialOverview/GetFinancialComparisonHandlerTests.cs
git commit -m "feat: add financial comparison MediatR handler"
```

---

### Task 6: Controller action

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/FinancialOverviewController.cs`

> Endpoint behavior (auth via `[FeatureAuthorize]` at class level) is best proven by the FE/E2E verification in Task 14; no new controller unit test (matches the existing controller, which has none).

- [ ] **Step 1: Add the action**

In `FinancialOverviewController.cs`, add this method after `GetFinancialOverview`:

```csharp
    [HttpGet("comparison")]
    [ProducesResponseType(typeof(GetFinancialComparisonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFinancialComparison(
        [FromQuery] int years = 3,
        [FromQuery] bool includeStockData = true,
        [FromQuery] List<string>? excludedDepartments = null,
        [FromQuery] bool includePartialMonth = true)
    {
        var request = new GetFinancialComparisonRequest
        {
            Years = years,
            IncludeStockData = includeStockData,
            ExcludedDepartments = excludedDepartments,
            IncludePartialMonth = includePartialMonth
        };
        var response = await _mediator.Send(request);

        return Ok(response);
    }
```

- [ ] **Step 2: Build the API project**

Run: `cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/FinancialOverviewController.cs
git commit -m "feat: expose GET /api/FinancialOverview/comparison"
```

---

### Task 7: Backend gate + regenerate the TypeScript client

**Files:**
- Modify (generated): `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Full backend build + format**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes`
Expected: Build succeeded; format reports no changes (if it does, run `dotnet format` and re-run the verify).

- [ ] **Step 2: Run the full FinancialOverview test suite**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FinancialOverview" -p:UseSharedCompilation=false`
Expected: PASS (all FinancialOverview tests green).

- [ ] **Step 3: Regenerate the frontend client**

Run: `cd backend && dotnet msbuild -t:GenerateFrontendClientManual`
Expected: `frontend/src/api/generated/api-client.ts` now contains `financialOverview_GetFinancialComparison(...)` and the types `GetFinancialComparisonResponse`, `YearComparisonSeriesDto`, `FinancialComparisonMetadataDto`, and the new `isPartial` / `partialDayOfMonth` on `MonthlyFinancialDataDto`.

Verify: `grep -n "financialOverview_GetFinancialComparison\|YearComparisonSeriesDto\|partialDayOfMonth" frontend/src/api/generated/api-client.ts`
Expected: matches printed.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate TS client for financial comparison endpoint"
```

---

## Frontend

### Task 8: Comparison utilities (metric, labels, colors, pivot)

**Files:**
- Create: `frontend/src/components/pages/financial-overview/comparisonUtils.ts`
- Modify: `frontend/src/components/pages/financial-overview/utils.ts`
- Test: `frontend/src/components/pages/financial-overview/__tests__/comparisonUtils.test.ts`

- [ ] **Step 1: Write the failing test**

Create `__tests__/comparisonUtils.test.ts`:

```ts
import {
  pivotSeriesToMonthly,
  getMetricValue,
  MONTH_LABELS_SHORT,
  type ComparisonMetric,
} from '../comparisonUtils'
import type { MonthlyFinancialDataDto } from '../../../../api/hooks/useFinancialOverview'

const cell = (month: number, over: Partial<MonthlyFinancialDataDto> = {}): MonthlyFinancialDataDto =>
  ({
    year: 2026,
    month,
    monthYearDisplay: `${String(month).padStart(2, '0')}/2026`,
    income: 0,
    expenses: 0,
    financialBalance: 0,
    ...over,
  }) as MonthlyFinancialDataDto

describe('comparisonUtils', () => {
  it('has 12 Czech short month labels', () => {
    expect(MONTH_LABELS_SHORT).toHaveLength(12)
    expect(MONTH_LABELS_SHORT[0]).toBe('Led')
    expect(MONTH_LABELS_SHORT[11]).toBe('Pro')
  })

  it('reads the requested metric from a cell', () => {
    const c = cell(7, { income: 100, expenses: 40, financialBalance: 60, totalBalance: 75 })
    expect(getMetricValue(c, 'income')).toBe(100)
    expect(getMetricValue(c, 'expenses')).toBe(40)
    expect(getMetricValue(c, 'balance')).toBe(60)
    expect(getMetricValue(c, 'totalBalance')).toBe(75)
  })

  it('falls back to financialBalance when totalBalance is absent', () => {
    const c = cell(7, { financialBalance: 60 })
    expect(getMetricValue(c, 'totalBalance')).toBe(60)
  })

  it('pivots months onto a fixed 12-slot array with null gaps', () => {
    const metric: ComparisonMetric = 'balance'
    const values = pivotSeriesToMonthly([cell(1, { financialBalance: 10 }), cell(3, { financialBalance: 30 })], metric)
    expect(values).toHaveLength(12)
    expect(values[0]).toBe(10)
    expect(values[1]).toBeNull()
    expect(values[2]).toBe(30)
    expect(values[11]).toBeNull()
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern "comparisonUtils"`
Expected: FAIL — cannot find module `../comparisonUtils`.

- [ ] **Step 3: Create `comparisonUtils.ts`**

```ts
import type { MonthlyFinancialDataDto } from '../../../api/hooks/useFinancialOverview'

export type ComparisonMetric = 'income' | 'expenses' | 'balance' | 'totalBalance'

export const MONTH_LABELS_SHORT = [
  'Led', 'Úno', 'Bře', 'Dub', 'Kvě', 'Čvn',
  'Čvc', 'Srp', 'Zář', 'Říj', 'Lis', 'Pro',
] as const

export const COMPARISON_METRIC_LABELS: Record<ComparisonMetric, string> = {
  income: 'Příjmy',
  expenses: 'Náklady',
  balance: 'Účetní bilance',
  totalBalance: 'Celková bilance (vč. skladu)',
}

// Distinct line colors per year slot (anchor year first). Extend if N grows beyond 3.
export const YEAR_SERIES_COLORS = [
  'rgb(59, 130, 246)', // blue-500  - anchor year
  'rgb(168, 85, 247)', // purple-500 - previous year
  'rgb(245, 158, 11)', // amber-500 - two years ago
] as const

export const getMetricValue = (cell: MonthlyFinancialDataDto, metric: ComparisonMetric): number => {
  switch (metric) {
    case 'income':
      return cell.income
    case 'expenses':
      return cell.expenses
    case 'balance':
      return cell.financialBalance
    case 'totalBalance':
      return cell.totalBalance ?? cell.financialBalance
    default: {
      const _exhaustive: never = metric
      throw new Error(`Unhandled metric: ${_exhaustive}`)
    }
  }
}

/**
 * Projects a year's month cells onto a fixed 12-element array (index 0 = January).
 * Missing months become null so a chart line stops rather than dropping to zero.
 */
export const pivotSeriesToMonthly = (
  months: MonthlyFinancialDataDto[],
  metric: ComparisonMetric,
): (number | null)[] => {
  const values: (number | null)[] = Array(12).fill(null)
  for (const cell of months) {
    if (cell.month >= 1 && cell.month <= 12) {
      values[cell.month - 1] = getMetricValue(cell, metric)
    }
  }
  return values
}
```

- [ ] **Step 4: Add the view-mode type to `utils.ts`**

Append to `frontend/src/components/pages/financial-overview/utils.ts`:

```ts
export type FinancialViewMode = 'timeline' | 'comparison'
```

- [ ] **Step 5: Run to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern "comparisonUtils"`
Expected: PASS (4 passed).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/financial-overview/comparisonUtils.ts \
        frontend/src/components/pages/financial-overview/utils.ts \
        frontend/src/components/pages/financial-overview/__tests__/comparisonUtils.test.ts
git commit -m "feat: add comparison utils (metric, month labels, pivot)"
```

---

### Task 9: Comparison data hook

**Files:**
- Modify: `frontend/src/api/client.ts`
- Create: `frontend/src/api/hooks/useFinancialComparison.ts`

- [ ] **Step 1: Add the query key**

In `frontend/src/api/client.ts`, inside the `QUERY_KEYS` object, add after the `financialOverview` line:

```ts
  financialComparison: ["financialComparison"] as const,
```

- [ ] **Step 2: Create the hook**

`frontend/src/api/hooks/useFinancialComparison.ts` (mirrors `useFinancialOverview.ts`):

```ts
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetFinancialComparisonResponse,
  YearComparisonSeriesDto,
  FinancialComparisonMetadataDto,
} from "../generated/api-client";

// Re-export the generated types for convenience
export {
  GetFinancialComparisonResponse,
  YearComparisonSeriesDto,
  FinancialComparisonMetadataDto,
};

export const useFinancialComparisonQuery = (
  years: number = 3,
  includeStockData: boolean = true,
  excludedDepartments: string[] = [],
  includePartialMonth: boolean = true,
) => {
  return useQuery<GetFinancialComparisonResponse, Error>({
    queryKey: [
      ...QUERY_KEYS.financialComparison,
      years,
      includeStockData,
      excludedDepartments,
      includePartialMonth,
    ],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return await apiClient.financialOverview_GetFinancialComparison(
        years,
        includeStockData,
        excludedDepartments,
        includePartialMonth,
      );
    },
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};
```

- [ ] **Step 3: Type-check via build (no isolated test — it's a thin wrapper over the generated client)**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json`
Expected: no errors referencing `useFinancialComparison.ts` or `client.ts`.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/api/hooks/useFinancialComparison.ts
git commit -m "feat: add useFinancialComparison query hook"
```

---

### Task 10: Comparison chart (one line per year)

**Files:**
- Create: `frontend/src/components/pages/financial-overview/FinancialComparisonChart.tsx`
- Test: `frontend/src/components/pages/financial-overview/__tests__/FinancialComparisonChart.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `__tests__/FinancialComparisonChart.test.tsx`. It mocks the `FinancialChart` wrapper to capture the datasets it receives, then asserts one dataset per year with the pivoted values:

```tsx
import React from 'react'
import { render } from '@testing-library/react'
import { FinancialComparisonChart } from '../FinancialComparisonChart'
import type { YearComparisonSeriesDto } from '../../../../api/hooks/useFinancialComparison'

let captured: { labels: unknown; datasets: any[] } | null = null

jest.mock('../FinancialChart', () => ({
  FinancialChart: ({ chartData }: { chartData: { labels: unknown; datasets: any[] } }) => {
    captured = chartData as { labels: unknown; datasets: any[] }
    return <div data-testid="mock-chart" />
  },
}))

const series = (year: number, months: number[], balance: number): YearComparisonSeriesDto =>
  ({
    year,
    months: months.map((m) => ({
      year,
      month: m,
      monthYearDisplay: `${String(m).padStart(2, '0')}/${year}`,
      income: 0,
      expenses: 0,
      financialBalance: balance,
    })),
    ytdIncome: 0,
    ytdExpenses: 0,
    ytdFinancialBalance: 0,
  }) as YearComparisonSeriesDto

describe('FinancialComparisonChart', () => {
  beforeEach(() => {
    captured = null
  })

  it('renders one dataset per year over a 12-month axis with null gaps', () => {
    render(
      <FinancialComparisonChart
        series={[series(2026, [1, 2], 50), series(2025, [1, 2, 3], 30)]}
        metric="balance"
        title="test"
      />,
    )

    expect(captured).not.toBeNull()
    expect(captured!.labels).toHaveLength(12)
    expect(captured!.datasets).toHaveLength(2)

    const anchor = captured!.datasets[0]
    expect(anchor.label).toBe('2026')
    expect(anchor.data[0]).toBe(50)
    expect(anchor.data[2]).toBeNull() // month 3 missing for 2026
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern "FinancialComparisonChart"`
Expected: FAIL — cannot find module `../FinancialComparisonChart`.

- [ ] **Step 3: Create the chart component**

`FinancialComparisonChart.tsx`:

```tsx
import React from 'react'
import type { ChartData, ChartOptions } from 'chart.js'
import { FinancialChart } from './FinancialChart'
import { formatCurrency } from './utils'
import type { YearComparisonSeriesDto } from '../../../api/hooks/useFinancialComparison'
import {
  MONTH_LABELS_SHORT,
  YEAR_SERIES_COLORS,
  pivotSeriesToMonthly,
  type ComparisonMetric,
} from './comparisonUtils'

interface FinancialComparisonChartProps {
  series: YearComparisonSeriesDto[]
  metric: ComparisonMetric
  title: string
}

export const FinancialComparisonChart: React.FC<FinancialComparisonChartProps> = ({
  series,
  metric,
  title,
}) => {
  const chartData = React.useMemo<ChartData<'bar'>>(() => {
    const datasets = series.map((s, index) => {
      const color = YEAR_SERIES_COLORS[index % YEAR_SERIES_COLORS.length]
      return {
        label: String(s.year),
        type: 'line' as const,
        data: pivotSeriesToMonthly(s.months, metric),
        borderColor: color,
        backgroundColor: color,
        spanGaps: false,
        tension: 0.1,
        borderWidth: 3,
        pointRadius: 3,
      }
    })
    return { labels: [...MONTH_LABELS_SHORT], datasets } as ChartData<'bar'>
  }, [series, metric])

  const chartOptions = React.useMemo<ChartOptions<'bar'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'top' as const, align: 'center' as const },
        title: { display: false },
        tooltip: {
          callbacks: {
            label: (context) => `${context.dataset.label}: ${formatCurrency(context.parsed.y ?? 0)}`,
          },
        },
      },
      scales: {
        y: {
          beginAtZero: false,
          ticks: { callback: (value) => formatCurrency(Number(value)) },
          grid: {
            color: (context) => (context.tick.value === 0 ? '#374151' : '#e5e7eb'),
            lineWidth: (context) => (context.tick.value === 0 ? 3 : 1),
          },
        },
      },
      interaction: { intersect: false, mode: 'index' },
    }),
    [],
  )

  return <FinancialChart chartData={chartData} chartOptions={chartOptions} title={title} />
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern "FinancialComparisonChart"`
Expected: PASS (1 passed).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/financial-overview/FinancialComparisonChart.tsx \
        frontend/src/components/pages/financial-overview/__tests__/FinancialComparisonChart.test.tsx
git commit -m "feat: add year-over-year comparison chart"
```

---

### Task 11: Comparison table (month × year pivot with Δ)

**Files:**
- Create: `frontend/src/components/pages/financial-overview/FinancialComparisonTable.tsx`
- Test: `frontend/src/components/pages/financial-overview/__tests__/FinancialComparisonTable.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `__tests__/FinancialComparisonTable.test.tsx`:

```tsx
import React from 'react'
import { render, screen } from '@testing-library/react'
import { FinancialComparisonTable } from '../FinancialComparisonTable'
import type { YearComparisonSeriesDto } from '../../../../api/hooks/useFinancialComparison'

const series = (year: number, month: number, balance: number, isPartial = false): YearComparisonSeriesDto =>
  ({
    year,
    months: [
      {
        year,
        month,
        monthYearDisplay: `${String(month).padStart(2, '0')}/${year}`,
        income: 0,
        expenses: 0,
        financialBalance: balance,
        isPartial: isPartial ? true : undefined,
        partialDayOfMonth: isPartial ? 3 : undefined,
      },
    ],
    ytdIncome: 0,
    ytdExpenses: 0,
    ytdFinancialBalance: balance,
  }) as YearComparisonSeriesDto

describe('FinancialComparisonTable', () => {
  it('renders a column header per year', () => {
    render(
      <FinancialComparisonTable
        series={[series(2026, 7, 100), series(2025, 7, 80)]}
        metric="balance"
      />,
    )
    expect(screen.getByText('2026')).toBeInTheDocument()
    expect(screen.getByText('2025')).toBeInTheDocument()
    // Delta = anchor - previous = 100 - 80 = 20 => rendered as currency somewhere in the July row
    expect(screen.getByText(/červenec/i)).toBeInTheDocument()
  })

  it('marks the partial month with an asterisk footnote', () => {
    render(
      <FinancialComparisonTable
        series={[series(2026, 7, 100, true), series(2025, 7, 80)]}
        metric="balance"
      />,
    )
    expect(screen.getByText(/částečný měsíc/i)).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern "FinancialComparisonTable"`
Expected: FAIL — cannot find module `../FinancialComparisonTable`.

- [ ] **Step 3: Create the table component**

`FinancialComparisonTable.tsx`:

```tsx
import React from 'react'
import type { YearComparisonSeriesDto } from '../../../api/hooks/useFinancialComparison'
import { formatCurrency } from './utils'
import { getMetricValue, type ComparisonMetric } from './comparisonUtils'

interface FinancialComparisonTableProps {
  series: YearComparisonSeriesDto[]
  metric: ComparisonMetric
}

const MONTH_NAMES_FULL = [
  'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
  'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
]

const valueColor = (value: number): string =>
  value >= 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400'

export const FinancialComparisonTable: React.FC<FinancialComparisonTableProps> = ({ series, metric }) => {
  // series arrives descending by year (anchor first). Anchor = series[0], previous = series[1].
  const anchor = series[0]
  const previous = series[1]

  const cellFor = (s: YearComparisonSeriesDto | undefined, month: number) =>
    s?.months.find((m) => m.month === month)

  const anchorHasPartial = anchor?.months.some((m) => m.isPartial) ?? false
  const partialDay = anchor?.months.find((m) => m.isPartial)?.partialDayOfMonth

  return (
    <div className="overflow-auto" style={{ maxHeight: '400px' }}>
      <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
        <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
          <tr>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
              Měsíc
            </th>
            {series.map((s) => (
              <th
                key={s.year}
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                {s.year}
              </th>
            ))}
            <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
              Δ ({anchor?.year} vs {previous?.year})
            </th>
          </tr>
        </thead>
        <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
          {Array.from({ length: 12 }, (_, i) => i + 1).map((month) => {
            const anchorCell = cellFor(anchor, month)
            const previousCell = cellFor(previous, month)
            const anchorValue = anchorCell ? getMetricValue(anchorCell, metric) : null
            const previousValue = previousCell ? getMetricValue(previousCell, metric) : null
            const delta =
              anchorValue !== null && previousValue !== null ? anchorValue - previousValue : null
            const isPartialRow = anchorCell?.isPartial === true

            return (
              <tr key={month} className="hover:bg-gray-50 dark:hover:bg-white/5">
                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                  {MONTH_NAMES_FULL[month - 1]}
                  {isPartialRow && <span className="text-amber-500"> *</span>}
                </td>
                {series.map((s) => {
                  const c = cellFor(s, month)
                  const v = c ? getMetricValue(c, metric) : null
                  return (
                    <td
                      key={s.year}
                      className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                        v === null ? 'text-gray-400 dark:text-graphite-faint' : valueColor(v)
                      }`}
                    >
                      {v === null ? '—' : formatCurrency(v)}
                    </td>
                  )
                })}
                <td
                  className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                    delta === null ? 'text-gray-400 dark:text-graphite-faint' : valueColor(delta)
                  }`}
                >
                  {delta === null ? '—' : formatCurrency(delta)}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
      {anchorHasPartial && partialDay !== undefined && (
        <p className="px-6 py-3 text-xs text-gray-500 dark:text-graphite-muted">
          * částečný měsíc – data k {partialDay}. dni měsíce (stejné oříznutí pro všechny roky).
        </p>
      )}
    </div>
  )
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern "FinancialComparisonTable"`
Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/financial-overview/FinancialComparisonTable.tsx \
        frontend/src/components/pages/financial-overview/__tests__/FinancialComparisonTable.test.tsx
git commit -m "feat: add year-over-year comparison table with delta column"
```

---

### Task 12: Extend filters with view-mode / years / metric controls

**Files:**
- Modify: `frontend/src/components/pages/financial-overview/FinancialFilters.tsx`

- [ ] **Step 1: Extend the props interface**

Replace the `FinancialFiltersProps` interface with (adds four fields; keeps all existing ones):

```tsx
interface FinancialFiltersProps {
  viewMode: FinancialViewMode
  comparisonYears: number
  comparisonMetric: ComparisonMetric
  selectedPeriod: PeriodType
  includeStockData: boolean
  includeCurrentMonth: boolean
  excludedDepartments: string[]
  departments: Department[] | undefined
  isRefetching: boolean
  onViewModeChange: (mode: FinancialViewMode) => void
  onComparisonYearsChange: (years: number) => void
  onComparisonMetricChange: (metric: ComparisonMetric) => void
  onPeriodChange: (period: PeriodType) => void
  onIncludeStockDataChange: (value: boolean) => void
  onIncludeCurrentMonthChange: (value: boolean) => void
  onExcludedDepartmentsChange: (departments: string[]) => void
}
```

- [ ] **Step 2: Update imports and the destructured params**

At the top of the file, extend the `utils` import and add the metric import:

```tsx
import { getPeriodLabel, type PeriodType, type FinancialViewMode } from './utils'
import { COMPARISON_METRIC_LABELS, type ComparisonMetric } from './comparisonUtils'
```

And add the new props to the destructuring at the top of the component:

```tsx
  viewMode,
  comparisonYears,
  comparisonMetric,
  onViewModeChange,
  onComparisonYearsChange,
  onComparisonMetricChange,
```

- [ ] **Step 3: Add the view-mode selector and swap the period block**

Replace the first period `<div>` block inside `controlsBlock` (the one whose label is `Časové období:`) with a view-mode selector followed by a mode-dependent block. That is, replace this existing block:

```tsx
      <div>
        <label
          htmlFor="period-select"
          className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2"
        >
          Časové období:
        </label>
        <select
          id="period-select"
          value={selectedPeriod}
          onChange={(e) => onPeriodChange(e.target.value as PeriodType)}
          className="block w-60 pl-3 pr-10 py-2 text-base border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
        >
          <option value="current-year">Aktuální rok</option>
          <option value="current-and-previous-year">Aktuální + předchozí rok</option>
          <option value="last-6-months">Posledních 6 měsíců</option>
          <option value="last-13-months">Posledních 13 měsíců</option>
          <option value="last-26-months">Posledních 26 měsíců</option>
        </select>
      </div>
```

with:

```tsx
      <div>
        <label
          htmlFor="view-mode-select"
          className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2"
        >
          Zobrazení:
        </label>
        <select
          id="view-mode-select"
          value={viewMode}
          onChange={(e) => onViewModeChange(e.target.value as FinancialViewMode)}
          className="block w-60 pl-3 pr-10 py-2 text-base border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
        >
          <option value="timeline">Časová osa</option>
          <option value="comparison">Meziroční srovnání</option>
        </select>
      </div>

      {viewMode === 'timeline' ? (
        <div>
          <label
            htmlFor="period-select"
            className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2"
          >
            Časové období:
          </label>
          <select
            id="period-select"
            value={selectedPeriod}
            onChange={(e) => onPeriodChange(e.target.value as PeriodType)}
            className="block w-60 pl-3 pr-10 py-2 text-base border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
          >
            <option value="current-year">Aktuální rok</option>
            <option value="current-and-previous-year">Aktuální + předchozí rok</option>
            <option value="last-6-months">Posledních 6 měsíců</option>
            <option value="last-13-months">Posledních 13 měsíců</option>
            <option value="last-26-months">Posledních 26 měsíců</option>
          </select>
        </div>
      ) : (
        <>
          <div>
            <label
              htmlFor="comparison-years-select"
              className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2"
            >
              Počet roků:
            </label>
            <select
              id="comparison-years-select"
              value={comparisonYears}
              onChange={(e) => onComparisonYearsChange(Number(e.target.value))}
              className="block w-40 pl-3 pr-10 py-2 text-base border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
            >
              <option value={2}>2 roky</option>
              <option value={3}>3 roky</option>
            </select>
          </div>
          <div>
            <label
              htmlFor="comparison-metric-select"
              className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2"
            >
              Metrika:
            </label>
            <select
              id="comparison-metric-select"
              value={comparisonMetric}
              onChange={(e) => onComparisonMetricChange(e.target.value as ComparisonMetric)}
              className="block w-60 pl-3 pr-10 py-2 text-base border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
            >
              <option value="income">{COMPARISON_METRIC_LABELS.income}</option>
              <option value="expenses">{COMPARISON_METRIC_LABELS.expenses}</option>
              <option value="balance">{COMPARISON_METRIC_LABELS.balance}</option>
              <option value="totalBalance">{COMPARISON_METRIC_LABELS.totalBalance}</option>
            </select>
          </div>
        </>
      )}
```

> The stock / current-month toggles and department checkboxes stay exactly as they are. In comparison mode the "Zobrazit aktuální měsíc" checkbox drives `includePartialMonth` (wired in Task 13).

- [ ] **Step 3b: Type-check**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json`
Expected: errors only in `FinancialOverview.tsx` (it doesn't yet pass the new props — fixed in Task 13). No errors inside `FinancialFilters.tsx` itself.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/pages/financial-overview/FinancialFilters.tsx
git commit -m "feat: add view-mode, years and metric controls to financial filters"
```

---

### Task 13: Wire the comparison branch into the page

**Files:**
- Modify: `frontend/src/components/pages/FinancialOverview.tsx`

- [ ] **Step 1: Add imports**

Add near the existing imports:

```tsx
import { useFinancialComparisonQuery } from "../../api/hooks/useFinancialComparison";
import { FinancialComparisonChart } from "./financial-overview/FinancialComparisonChart";
import { FinancialComparisonTable } from "./financial-overview/FinancialComparisonTable";
import { COMPARISON_METRIC_LABELS, type ComparisonMetric } from "./financial-overview/comparisonUtils";
import { type FinancialViewMode } from "./financial-overview/utils";
```

- [ ] **Step 2: Add view-mode state**

After the existing `const [excludedDepartments, setExcludedDepartments] = useState<string[]>([]);` line, add:

```tsx
  const [viewMode, setViewMode] = useState<FinancialViewMode>("timeline");
  const [comparisonYears, setComparisonYears] = useState<number>(2);
  const [comparisonMetric, setComparisonMetric] = useState<ComparisonMetric>("balance");
```

- [ ] **Step 3: Call the comparison hook (enabled only in comparison mode)**

After the existing `useFinancialOverviewQuery(...)` call, add the comparison query. Reuse `includeCurrentMonth` as the partial-month toggle:

```tsx
  const {
    data: comparisonData,
    isLoading: isComparisonLoading,
    error: comparisonError,
  } = useFinancialComparisonQuery(
    comparisonYears,
    includeStockData,
    excludedDepartments,
    includeCurrentMonth,
  );
```

- [ ] **Step 4: Gate the loading / error guards by view mode**

Replace the existing `if (isLoading) {` guard condition with a mode-aware one, and likewise for the error guard. Change:

```tsx
  if (isLoading) {
```
to:
```tsx
  const activeLoading = viewMode === "comparison" ? isComparisonLoading : isLoading;
  const activeError = viewMode === "comparison" ? comparisonError : error;

  if (activeLoading) {
```

and change the `if (error) {` line to:

```tsx
  if (activeError) {
```

Inside that error block, replace the two `{error.message ...}` / `error` references so it reads `{activeError.message || "Neznámá chyba"}`.

- [ ] **Step 5: Pass the new props to `FinancialFilters`**

Update the `<FinancialFilters ... />` usage to include the new props:

```tsx
        <FinancialFilters
          viewMode={viewMode}
          comparisonYears={comparisonYears}
          comparisonMetric={comparisonMetric}
          selectedPeriod={selectedPeriod}
          includeStockData={includeStockData}
          includeCurrentMonth={includeCurrentMonth}
          excludedDepartments={excludedDepartments}
          departments={departments}
          isRefetching={isRefetching}
          onViewModeChange={setViewMode}
          onComparisonYearsChange={setComparisonYears}
          onComparisonMetricChange={setComparisonMetric}
          onPeriodChange={setSelectedPeriod}
          onIncludeStockDataChange={setIncludeStockData}
          onIncludeCurrentMonthChange={setIncludeCurrentMonth}
          onExcludedDepartmentsChange={setExcludedDepartments}
        />
```

- [ ] **Step 6: Render the comparison view when active**

Immediately after the closing `/>` of `<FinancialFilters ... />`, wrap the existing summary-cards + chart + monthly-data + empty-state blocks so they only show in timeline mode, and add the comparison block. Insert this **before** the existing `{/* Summary Cards */}` comment:

```tsx
        {viewMode === "comparison" && comparisonData && (
          <>
            {/* Per-year YTD summary cards */}
            <div className="grid grid-cols-2 md:grid-cols-3 gap-4 mb-6">
              {comparisonData.series.map((s) => (
                <div
                  key={s.year}
                  className="bg-white dark:bg-graphite-surface overflow-hidden shadow dark:shadow-soft-dark rounded-lg"
                >
                  <div className="p-3">
                    <dt className="text-xs font-medium text-gray-500 dark:text-graphite-muted truncate">
                      {s.year} — {COMPARISON_METRIC_LABELS.balance} (YTD)
                    </dt>
                    <dd
                      className={`text-sm font-medium ${
                        s.ytdFinancialBalance >= 0
                          ? "text-emerald-600 dark:text-emerald-400"
                          : "text-red-600 dark:text-red-400"
                      }`}
                    >
                      {formatCurrency(s.ytdFinancialBalance)}
                    </dd>
                  </div>
                </div>
              ))}
            </div>

            <FinancialComparisonChart
              series={comparisonData.series}
              metric={comparisonMetric}
              title={`Meziroční srovnání — ${COMPARISON_METRIC_LABELS[comparisonMetric]}`}
            />

            <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark sm:rounded-md mb-8">
              <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-graphite-border">
                <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text">
                  Měsíční srovnání
                </h3>
              </div>
              <FinancialComparisonTable series={comparisonData.series} metric={comparisonMetric} />
            </div>
          </>
        )}
```

Then guard the existing timeline content: change the existing `{/* Summary Cards */}` conditional opener `{data?.summary && (` to `{viewMode === "timeline" && data?.summary && (`, the chart opener `{chartData && (` to `{viewMode === "timeline" && chartData && (`, the monthly-data opener `{data?.data && (` to `{viewMode === "timeline" && data?.data && (`, and the empty-state opener `{data?.data && data.data.length === 0 && (` to `{viewMode === "timeline" && data?.data && data.data.length === 0 && (`.

- [ ] **Step 7: Build (stricter than tsc) + lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds, lint passes (no errors).

- [ ] **Step 8: Run the financial-overview FE tests**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern "financial-overview|useFinancial"`
Expected: PASS (existing + new tests green). If a shell-component test for `FinancialOverview` fails because the new hook isn't mocked, add `jest.mock("../../api/hooks/useFinancialComparison")` to that test's mock block (see memory gotcha on FE tests mocking hooks/contexts).

- [ ] **Step 9: Commit**

```bash
git add frontend/src/components/pages/FinancialOverview.tsx
git commit -m "feat: wire year-over-year comparison view into financial overview page"
```

---

### Task 14: End-to-end verification on staging

**Files:**
- None (manual / Playwright validation).

- [ ] **Step 1: Full local gates**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes`
Run: `cd frontend && npm run build && npm run lint`
Expected: all green.

- [ ] **Step 2: Drive the flow on staging**

Per `docs/testing/playwright-e2e-testing.md`, authenticate via `navigateToApp()` (not `createE2EAuthSession()` alone), then:
1. Open `/finance/overview`.
2. Switch "Zobrazení" to **Meziroční srovnání**. Confirm the chart x-axis shows the 12 Czech month labels (Led…Pro) and one line per year.
3. Toggle "Počet roků" 2 ↔ 3 and confirm a third line appears/disappears.
4. Change "Metrika" and confirm every line + the table recompute.
5. Confirm the current month row is marked `*` and the footnote reads `data k <den>. dni měsíce`; confirm the same month for prior years is present and comparable (both cut at the same day).
6. Toggle "Zobrazit aktuální měsíc" off → the partial month disappears from all years.

- [ ] **Step 3: Cross-check the fair cut against the timeline**

With "Zobrazit aktuální měsíc" on in **timeline** mode, note the anchor year's current-month balance. Switch to **comparison** mode: the anchor year's partial cell for that month should match (both are `today − 5 days` cut). Any mismatch means the cutoff/stock period wiring diverged — investigate `BuildComparisonCellAsync` `periodEnd` vs the timeline's `now.Date`.

> Note the intentional difference: the timeline's `includeCurrentMonth` cuts the current month at **today**, while comparison cuts at **today − PartialMonthLagDays**. So they will differ by the lag window; the cross-check is about the *shape* (partial vs full), and exact equality only holds if you temporarily set `PartialMonthLagDays = 0`. Document the lag in the PR description.

- [ ] **Step 4: Optional automated E2E**

If adding a Playwright spec, place it under `frontend/test/e2e/finance/` (module-folder rule), use fixtures from `frontend/test/e2e/fixtures/test-data.ts` (throw, don't skip, on missing data), and run `./scripts/run-playwright-tests.sh`.

---

## Self-Review

**Spec coverage:**
- Multiple years by month, same-month comparison → Tasks 4 (series per year), 10 (one line per year over 12-month axis), 11 (month × year table). ✓
- Fair partial-month cut (current + prior years cut at same day) → Task 4 `BuildComparisonCellAsync` `effectiveDay`/`periodEnd`; Task 2 partial stock. Verified by service tests + Task 14 step 5. ✓
- Cutoff = today − 5 days (named option) → Task 3 `PartialMonthLagDays`, Task 4 `cutoffDate`. ✓
- Configurable N = 2–3 → Task 4 `Math.Clamp(years, 2, 3)`; Task 12 years selector. ✓
- View-mode toggle on the existing page (reuse filters/chart/formatCurrency) → Tasks 10 (reuses `FinancialChart`), 11 (reuses `formatCurrency`), 12, 13. ✓
- Partial-month toggle → reuses existing `includeCurrentMonth` → `includePartialMonth` (Tasks 12–13). ✓

**Placeholder scan:** No TBD/TODO; every code step shows full content; every test step shows the assertion and the exact run command with expected result. ✓

**Type/name consistency:** `GetFinancialComparisonAsync(int, bool, IReadOnlyList<string>?, bool, CancellationToken)` identical across interface (Task 4), service (Task 4), handler (Task 5), and handler test (Task 5). Response `Series`/`Metadata`, series `Months`/`Ytd*`, metadata field names identical across Tasks 3, 4, 13. FE: `useFinancialComparisonQuery(years, includeStockData, excludedDepartments, includePartialMonth)` used identically in Tasks 9 and 13; `ComparisonMetric`/`pivotSeriesToMonthly`/`getMetricValue`/`MONTH_LABELS_SHORT`/`YEAR_SERIES_COLORS`/`COMPARISON_METRIC_LABELS` defined in Task 8 and consumed unchanged in Tasks 10–13. Generated client method `financialOverview_GetFinancialComparison` used in Task 9 matches the regen in Task 7. ✓

---

## Execution notes
- `dotnet test` in Conductor worktrees: build first, then `--no-build` with `-p:UseSharedCompilation=false` if a parallel worktree contends (memory gotcha). Solution is at repo root.
- FE tests: use `react-scripts test`, not bare `npx jest` (memory gotcha).
- After any code-review fixes, re-run `npm run build` (not just `tsc`) — FE build is stricter (memory gotcha).
