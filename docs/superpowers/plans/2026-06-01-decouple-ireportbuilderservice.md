# Decouple IReportBuilderService from UseCase Response Types — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the three return types of `IReportBuilderService` out of `UseCases/*Response` classes and into dedicated DTOs under `Features/Analytics/Contracts/`, while keeping the public OpenAPI surface and HTTP responses byte-identical.

**Architecture:** Introduce three contract DTOs (`MonthlyMarginBreakdownDto`, `CategoryMarginSummaryDto`, `ProductMarginSummaryDto`) mirroring the existing nested response classes field-for-field. Switch the service interface + implementation to return those DTOs. Have the two consuming handlers (`GetProductMarginAnalysisHandler`, `GetMarginReportHandler`) project DTOs back to their nested response shapes inline (LINQ `Select`). The nested response types stay untouched so the OpenAPI schema and generated TypeScript client are unchanged.

**Tech Stack:** .NET 8, C#, MediatR, xUnit + Moq.

---

## File Structure

**Create (3 files):**
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/MonthlyMarginBreakdownDto.cs` — shared DTO returned by `IReportBuilderService.BuildMonthlyBreakdown`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/CategoryMarginSummaryDto.cs` — shared DTO returned by `IReportBuilderService.BuildCategorySummaries`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSummaryDto.cs` — shared DTO returned by `IReportBuilderService.BuildProductSummary`.

**Modify (5 files):**
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` — drop `UseCases.*` usings; change interface and implementation to return new DTOs.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — project `MonthlyMarginBreakdownDto` → `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` at the call site in `BuildSuccessResponse`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — switch the internal `ReportData` helper to hold DTO lists; project to nested response types once inside `BuildSuccessResponse`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` — update mock `.Returns(...)` lambdas to return new DTOs.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` — update mock breakdown setup to return new DTO list.

**Untouched (verification gates):**
- `backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs` — empty `git diff` required.
- `frontend/src/api/generated/api-client.ts` — empty `git diff` required.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisResponse.cs` — nested `MonthlyMarginBreakdown` class preserved.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportResponse.cs` — nested `ProductMarginSummary` and `CategoryMarginSummary` classes preserved.
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs:30` — DI registration unchanged.

---

## Task 1: Add `MonthlyMarginBreakdownDto`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/MonthlyMarginBreakdownDto.cs`

Field set is transcribed verbatim from `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` (lines 18-25 of `GetProductMarginAnalysisResponse.cs`).

- [ ] **Step 1: Create the DTO file**

Create `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/MonthlyMarginBreakdownDto.cs` with this exact content:

```csharp
namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class MonthlyMarginBreakdownDto
{
    public DateTime Month { get; set; }
    public decimal MarginAmount { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public int UnitsSold { get; set; }
}
```

- [ ] **Step 2: Verify it compiles in isolation**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS (the existing service still compiles because the new DTO is not referenced anywhere yet).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/MonthlyMarginBreakdownDto.cs
git commit -m "feat(analytics): add MonthlyMarginBreakdownDto contract"
```

---

## Task 2: Add `CategoryMarginSummaryDto`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/CategoryMarginSummaryDto.cs`

Field set is transcribed verbatim from `GetMarginReportResponse.CategoryMarginSummary` (lines 45-53 of `GetMarginReportResponse.cs`).

- [ ] **Step 1: Create the DTO file**

Create `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/CategoryMarginSummaryDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class CategoryMarginSummaryDto
{
    public string Category { get; set; } = string.Empty;
    public decimal TotalMargin { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AverageMarginPercentage { get; set; }
    public int ProductCount { get; set; }
    public int TotalUnitsSold { get; set; }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/CategoryMarginSummaryDto.cs
git commit -m "feat(analytics): add CategoryMarginSummaryDto contract"
```

---

## Task 3: Add `ProductMarginSummaryDto`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSummaryDto.cs`

Field set is transcribed verbatim from `GetMarginReportResponse.ProductMarginSummary` (lines 18-43 of `GetMarginReportResponse.cs`). 16 fields, same names, types, and initializers.

- [ ] **Step 1: Create the DTO file**

Create `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSummaryDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class ProductMarginSummaryDto
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal MarginAmount { get; set; }

    // M0-M2 margin levels - amounts (for sorting)
    public decimal M0Amount { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M2Amount { get; set; }

    // M0-M2 margin levels - percentages (for sorting)
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }

    // Pricing (for sorting)
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }

    public decimal MarginPercentage { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public int UnitsSold { get; set; }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS.

- [ ] **Step 3: Cross-check field correspondence**

Run: `diff <(grep -E '^\s*public ' backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSummaryDto.cs) <(sed -n '18,43p' backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportResponse.cs | grep -E '^\s*public ')`
Expected: empty diff (the property declaration lines must match exactly).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSummaryDto.cs
git commit -m "feat(analytics): add ProductMarginSummaryDto contract"
```

---

## Task 4: Switch `IReportBuilderService` and `ReportBuilderService` to the new DTOs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` (entire file rewritten — only `using` directives and three return-type / `new` expressions actually change; calculation bodies are byte-for-byte preserved).

The two call sites (`GetMarginReportHandler`, `GetProductMarginAnalysisHandler`) and their tests still expect the **old** return types at this point, so the project will not build between Step 1 and Step 5 of this task. Tasks 5–7 below restore the build. Do **not** commit until the full sequence (Task 4 → Task 7) is green.

- [ ] **Step 1: Rewrite `ReportBuilderService.cs`**

Replace the entire content of `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` with:

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Models;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IReportBuilderService
{
    List<MonthlyMarginBreakdownDto> BuildMonthlyBreakdown(
        List<SalesDataPoint> salesData,
        AnalyticsProduct productData,
        DateTime startDate,
        DateTime endDate);

    List<CategoryMarginSummaryDto> BuildCategorySummaries(
        Dictionary<string, CategoryData> categoryTotals);

    ProductMarginSummaryDto BuildProductSummary(
        AnalyticsProduct product,
        AnalysisMarginData marginData);
}

public class ReportBuilderService : IReportBuilderService
{
    public List<MonthlyMarginBreakdownDto> BuildMonthlyBreakdown(
        List<SalesDataPoint> salesData,
        AnalyticsProduct productData,
        DateTime startDate,
        DateTime endDate)
    {
        var breakdown = new List<MonthlyMarginBreakdownDto>();
        var current = new DateTime(startDate.Year, startDate.Month, 1);
        var end = new DateTime(endDate.Year, endDate.Month, 1);

        while (current <= end)
        {
            var monthSales = salesData
                .Where(s => s.Date.Year == current.Year && s.Date.Month == current.Month)
                .ToList();

            var monthlyUnitsSold = (int)monthSales.Sum(s => s.AmountB2B + s.AmountB2C);
            var monthlyRevenue = (decimal)monthlyUnitsSold * productData.SellingPrice;
            var monthlyCost = (decimal)monthlyUnitsSold * (productData.SellingPrice - productData.MarginAmount);
            var monthlyMargin = monthlyRevenue - monthlyCost;

            breakdown.Add(new MonthlyMarginBreakdownDto
            {
                Month = current,
                MarginAmount = monthlyMargin,
                Revenue = monthlyRevenue,
                Cost = monthlyCost,
                UnitsSold = monthlyUnitsSold
            });

            current = current.AddMonths(1);
        }

        return breakdown;
    }

    public List<CategoryMarginSummaryDto> BuildCategorySummaries(
        Dictionary<string, CategoryData> categoryTotals)
    {
        return categoryTotals.Select(kvp => new CategoryMarginSummaryDto
        {
            Category = kvp.Key,
            TotalMargin = kvp.Value.TotalMargin,
            TotalRevenue = kvp.Value.TotalRevenue,
            AverageMarginPercentage = kvp.Value.TotalRevenue > 0 ?
                (kvp.Value.TotalMargin / kvp.Value.TotalRevenue) * 100 : 0,
            ProductCount = kvp.Value.ProductCount,
            TotalUnitsSold = kvp.Value.TotalUnitsSold
        }).ToList();
    }

    public ProductMarginSummaryDto BuildProductSummary(
        AnalyticsProduct product,
        AnalysisMarginData marginData)
    {
        return new ProductMarginSummaryDto
        {
            ProductId = product.ProductCode,
            ProductName = product.ProductName,
            Category = product.ProductCategory ?? AnalyticsConstants.DEFAULT_CATEGORY,
            MarginAmount = marginData.Margin,

            // M0-M2 margin levels - amounts
            M0Amount = product.M0Amount,
            M1Amount = product.M1Amount,
            M2Amount = product.M2Amount,

            // M0-M2 margin levels - percentages
            M0Percentage = product.M0Percentage,
            M1Percentage = product.M1Percentage,
            M2Percentage = product.M2Percentage,

            // Pricing
            SellingPrice = product.SellingPrice,
            PurchasePrice = product.PurchasePrice,

            MarginPercentage = marginData.MarginPercentage,
            Revenue = marginData.Revenue,
            Cost = marginData.Cost,
            UnitsSold = marginData.UnitsSold
        };
    }
}
```

- [ ] **Step 2: Verify no residual `UseCases` references remain in the service**

Run: `grep -n 'UseCases' backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs`
Expected: no output (empty result).

- [ ] **Step 3: Confirm the project does NOT yet build (expected red state)**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: FAIL. Errors should point at `GetProductMarginAnalysisHandler.cs` (line ~108), `GetMarginReportHandler.cs` (lines 130, 144, and the internal `ReportData` declaration around 215-216), and the two affected handler test files. This proves the interface change is reaching the consumers; the next tasks fix them.

**Do not commit yet — proceed straight to Task 5.**

---

## Task 5: Project DTO → nested type in `GetProductMarginAnalysisHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` (lines 100-110 region).

The handler calls `_reportBuilderService.BuildMonthlyBreakdown(...)` and assigns its result directly to `response.MonthlyBreakdown` (a `List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown>`). After Task 4, the call returns `List<MonthlyMarginBreakdownDto>`. We insert an inline `Select` projection at the assembly seam — one site only.

- [ ] **Step 1: Update the breakdown assignment**

In `GetProductMarginAnalysisHandler.cs`, find the block (currently lines 102-110):

```csharp
        // Add monthly breakdown if requested
        if (request.IncludeBreakdown)
        {
            var salesInPeriod = productData.SalesHistory
                .Where(s => s.Date >= request.StartDate && s.Date <= request.EndDate)
                .ToList();

            response.MonthlyBreakdown = _reportBuilderService.BuildMonthlyBreakdown(
                salesInPeriod, productData, request.StartDate, request.EndDate);
        }
```

Replace **only the final assignment** with:

```csharp
        // Add monthly breakdown if requested
        if (request.IncludeBreakdown)
        {
            var salesInPeriod = productData.SalesHistory
                .Where(s => s.Date >= request.StartDate && s.Date <= request.EndDate)
                .ToList();

            response.MonthlyBreakdown = _reportBuilderService
                .BuildMonthlyBreakdown(salesInPeriod, productData, request.StartDate, request.EndDate)
                .Select(dto => new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown
                {
                    Month = dto.Month,
                    MarginAmount = dto.MarginAmount,
                    Revenue = dto.Revenue,
                    Cost = dto.Cost,
                    UnitsSold = dto.UnitsSold
                })
                .ToList();
        }
```

- [ ] **Step 2: Verify no `using` change is required**

The handler already imports `Anela.Heblo.Application.Features.Analytics.Contracts;` (line 1 of the file), so the new DTO type resolves without any new `using`. Run: `grep -n '^using' backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs`
Expected: includes `using Anela.Heblo.Application.Features.Analytics.Contracts;`. No other change needed.

- [ ] **Step 3: Confirm this handler now compiles in isolation against the new interface**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: still FAILS, but the failures must no longer include `GetProductMarginAnalysisHandler.cs`. The remaining errors are in `GetMarginReportHandler.cs` and the test files.

**Do not commit yet — proceed to Task 6.**

---

## Task 6: Project DTO → nested type in `GetMarginReportHandler` (single projection seam)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`.

The internal `ReportData` helper (lines 213-218) currently holds `List<GetMarginReportResponse.ProductMarginSummary>` and `List<GetMarginReportResponse.CategoryMarginSummary>`. After this task, it holds `List<ProductMarginSummaryDto>` and `List<CategoryMarginSummaryDto>`. The single projection happens in `BuildSuccessResponse`. The `OrderByDescending(p => p.M2Percentage)` sort at line 141 keeps working because `M2Percentage` is preserved field-for-field on `ProductMarginSummaryDto`.

- [ ] **Step 1: Change the local accumulator in `ProcessProductsForReport`**

In `GetMarginReportHandler.cs` at line 102, change:

```csharp
        var productSummaries = new List<GetMarginReportResponse.ProductMarginSummary>();
```

to:

```csharp
        var productSummaries = new List<ProductMarginSummaryDto>();
```

- [ ] **Step 2: Change `ReportData` to hold DTO lists**

In `GetMarginReportHandler.cs` find the helper class at lines 213-218:

```csharp
internal class ReportData
{
    public List<GetMarginReportResponse.ProductMarginSummary> ProductSummaries { get; set; } = new();
    public List<GetMarginReportResponse.CategoryMarginSummary> CategorySummaries { get; set; } = new();
    public OverallTotals OverallTotals { get; set; } = new();
}
```

Replace with:

```csharp
internal class ReportData
{
    public List<ProductMarginSummaryDto> ProductSummaries { get; set; } = new();
    public List<CategoryMarginSummaryDto> CategorySummaries { get; set; } = new();
    public OverallTotals OverallTotals { get; set; } = new();
}
```

- [ ] **Step 3: Project DTOs → nested types in `BuildSuccessResponse`**

In `GetMarginReportHandler.cs` find `BuildSuccessResponse` (lines 176-196):

```csharp
    private GetMarginReportResponse BuildSuccessResponse(GetMarginReportRequest request, ReportData reportData)
    {
        var averageMarginPercentage = reportData.OverallTotals.TotalRevenue > 0
            ? (reportData.OverallTotals.TotalMargin / reportData.OverallTotals.TotalRevenue) * 100
            : 0;

        return new GetMarginReportResponse
        {
            Success = true,
            ReportPeriodStart = request.StartDate,
            ReportPeriodEnd = request.EndDate,
            TotalMargin = reportData.OverallTotals.TotalMargin,
            TotalRevenue = reportData.OverallTotals.TotalRevenue,
            TotalCost = reportData.OverallTotals.TotalCost,
            AverageMarginPercentage = averageMarginPercentage,
            TotalProductsAnalyzed = reportData.ProductSummaries.Count,
            TotalUnitsSold = reportData.OverallTotals.TotalUnitsSold,
            ProductSummaries = reportData.ProductSummaries,
            CategorySummaries = reportData.CategorySummaries
        };
    }
```

Replace the final two assignments (`ProductSummaries`, `CategorySummaries`) so the method becomes:

```csharp
    private GetMarginReportResponse BuildSuccessResponse(GetMarginReportRequest request, ReportData reportData)
    {
        var averageMarginPercentage = reportData.OverallTotals.TotalRevenue > 0
            ? (reportData.OverallTotals.TotalMargin / reportData.OverallTotals.TotalRevenue) * 100
            : 0;

        return new GetMarginReportResponse
        {
            Success = true,
            ReportPeriodStart = request.StartDate,
            ReportPeriodEnd = request.EndDate,
            TotalMargin = reportData.OverallTotals.TotalMargin,
            TotalRevenue = reportData.OverallTotals.TotalRevenue,
            TotalCost = reportData.OverallTotals.TotalCost,
            AverageMarginPercentage = averageMarginPercentage,
            TotalProductsAnalyzed = reportData.ProductSummaries.Count,
            TotalUnitsSold = reportData.OverallTotals.TotalUnitsSold,
            ProductSummaries = reportData.ProductSummaries
                .Select(dto => new GetMarginReportResponse.ProductMarginSummary
                {
                    ProductId = dto.ProductId,
                    ProductName = dto.ProductName,
                    Category = dto.Category,
                    MarginAmount = dto.MarginAmount,
                    M0Amount = dto.M0Amount,
                    M1Amount = dto.M1Amount,
                    M2Amount = dto.M2Amount,
                    M0Percentage = dto.M0Percentage,
                    M1Percentage = dto.M1Percentage,
                    M2Percentage = dto.M2Percentage,
                    SellingPrice = dto.SellingPrice,
                    PurchasePrice = dto.PurchasePrice,
                    MarginPercentage = dto.MarginPercentage,
                    Revenue = dto.Revenue,
                    Cost = dto.Cost,
                    UnitsSold = dto.UnitsSold
                })
                .ToList(),
            CategorySummaries = reportData.CategorySummaries
                .Select(dto => new GetMarginReportResponse.CategoryMarginSummary
                {
                    Category = dto.Category,
                    TotalMargin = dto.TotalMargin,
                    TotalRevenue = dto.TotalRevenue,
                    AverageMarginPercentage = dto.AverageMarginPercentage,
                    ProductCount = dto.ProductCount,
                    TotalUnitsSold = dto.TotalUnitsSold
                })
                .ToList()
        };
    }
```

- [ ] **Step 4: Confirm no other reference to the nested type names remains in non-projection positions**

Run: `grep -n 'GetMarginReportResponse\.\(Product\|Category\)MarginSummary' backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`
Expected: only two matches, both inside the new `Select(dto => new ...)` projections in `BuildSuccessResponse`. No other line should reference the nested types.

- [ ] **Step 5: Confirm `using Anela.Heblo.Application.Features.Analytics.Contracts;` is present**

Run: `grep -n '^using Anela.Heblo.Application.Features.Analytics.Contracts;' backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`
Expected: matches line 1 (already present in the original file). No new `using` directives required.

- [ ] **Step 6: Build the production code**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS with zero new warnings. Production code is now consistent; only the test project still fails.

**Do not commit yet — proceed to Task 7.**

---

## Task 7: Update handler test mocks to return the new DTOs

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs`

The tests mock `IReportBuilderService` and stub its three methods. The stubs currently return the old nested response types; they must now return the new DTO types. This is a type-name-only change — no behavioral test logic is altered.

- [ ] **Step 1: Add the Contracts using to `GetMarginReportHandlerTests.cs`**

If `using Anela.Heblo.Application.Features.Analytics.Contracts;` is not already present at the top of `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`, add it alphabetically alongside the existing `Anela.Heblo.Application.Features.Analytics.*` usings.

Run: `grep -n '^using Anela.Heblo.Application.Features.Analytics.Contracts;' backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`
Expected: one match. If empty, add the line.

- [ ] **Step 2: Update the `BuildProductSummary` mock setup**

In `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` find lines 82-94:

```csharp
        _reportBuilderServiceMock
            .Setup(x => x.BuildProductSummary(It.IsAny<AnalyticsProduct>(), It.IsAny<AnalysisMarginData>()))
            .Returns((AnalyticsProduct product, AnalysisMarginData data) => new GetMarginReportResponse.ProductMarginSummary
            {
                ProductId = product.ProductCode,
                ProductName = product.ProductName,
                Category = product.ProductCategory ?? "Uncategorized",
                MarginAmount = data.Margin,
                MarginPercentage = data.MarginPercentage,
                Revenue = data.Revenue,
                Cost = data.Cost,
                UnitsSold = data.UnitsSold
            });
```

Replace `GetMarginReportResponse.ProductMarginSummary` with `ProductMarginSummaryDto`:

```csharp
        _reportBuilderServiceMock
            .Setup(x => x.BuildProductSummary(It.IsAny<AnalyticsProduct>(), It.IsAny<AnalysisMarginData>()))
            .Returns((AnalyticsProduct product, AnalysisMarginData data) => new ProductMarginSummaryDto
            {
                ProductId = product.ProductCode,
                ProductName = product.ProductName,
                Category = product.ProductCategory ?? "Uncategorized",
                MarginAmount = data.Margin,
                MarginPercentage = data.MarginPercentage,
                Revenue = data.Revenue,
                Cost = data.Cost,
                UnitsSold = data.UnitsSold
            });
```

- [ ] **Step 3: Update the `BuildCategorySummaries` mock setup**

In the same file find lines 96-107:

```csharp
        _reportBuilderServiceMock
            .Setup(x => x.BuildCategorySummaries(It.IsAny<Dictionary<string, CategoryData>>()))
            .Returns((Dictionary<string, CategoryData> categoryTotals) =>
                categoryTotals.Select(kvp => new GetMarginReportResponse.CategoryMarginSummary
                {
                    Category = kvp.Key,
                    TotalMargin = kvp.Value.TotalMargin,
                    TotalRevenue = kvp.Value.TotalRevenue,
                    ProductCount = kvp.Value.ProductCount,
                    TotalUnitsSold = kvp.Value.TotalUnitsSold,
                    AverageMarginPercentage = kvp.Value.TotalRevenue > 0 ? (kvp.Value.TotalMargin / kvp.Value.TotalRevenue) * 100 : 0
                }).ToList());
```

Replace `GetMarginReportResponse.CategoryMarginSummary` with `CategoryMarginSummaryDto`:

```csharp
        _reportBuilderServiceMock
            .Setup(x => x.BuildCategorySummaries(It.IsAny<Dictionary<string, CategoryData>>()))
            .Returns((Dictionary<string, CategoryData> categoryTotals) =>
                categoryTotals.Select(kvp => new CategoryMarginSummaryDto
                {
                    Category = kvp.Key,
                    TotalMargin = kvp.Value.TotalMargin,
                    TotalRevenue = kvp.Value.TotalRevenue,
                    ProductCount = kvp.Value.ProductCount,
                    TotalUnitsSold = kvp.Value.TotalUnitsSold,
                    AverageMarginPercentage = kvp.Value.TotalRevenue > 0 ? (kvp.Value.TotalMargin / kvp.Value.TotalRevenue) * 100 : 0
                }).ToList());
```

- [ ] **Step 4: Verify no other ProductMarginSummary/CategoryMarginSummary references remain in this test file**

Run: `grep -n 'GetMarginReportResponse\.\(Product\|Category\)MarginSummary' backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`
Expected: no output (empty). If any matches appear (e.g., explicit list typing such as `List<GetMarginReportResponse.ProductMarginSummary>`), replace those with the matching `*Dto` type — they are stubbing the interface return values and must align with the interface.

- [ ] **Step 5: Add the Contracts using to `GetProductMarginAnalysisHandlerTests.cs`**

If `using Anela.Heblo.Application.Features.Analytics.Contracts;` is missing, add it alphabetically alongside existing `Anela.Heblo.Application.Features.Analytics.*` usings.

Run: `grep -n '^using Anela.Heblo.Application.Features.Analytics.Contracts;' backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs`
Expected: one match.

- [ ] **Step 6: Update the monthly breakdown stub list type**

In `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` find lines 73-84:

```csharp
        var monthlyBreakdown = new List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown>();
        for (int month = 1; month <= 12; month++)
        {
            monthlyBreakdown.Add(new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown
            {
                Month = new DateTime(2024, month, 1),
                UnitsSold = month == 3 ? 15 : month == 6 ? 30 : 0,
                Revenue = month == 3 ? 2250m : month == 6 ? 4500m : 0,
                MarginAmount = month == 3 ? 1500m : month == 6 ? 3000m : 0,
                Cost = month == 3 ? 750m : month == 6 ? 1500m : 0
            });
        }
```

Replace `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` (both occurrences) with `MonthlyMarginBreakdownDto`:

```csharp
        var monthlyBreakdown = new List<MonthlyMarginBreakdownDto>();
        for (int month = 1; month <= 12; month++)
        {
            monthlyBreakdown.Add(new MonthlyMarginBreakdownDto
            {
                Month = new DateTime(2024, month, 1),
                UnitsSold = month == 3 ? 15 : month == 6 ? 30 : 0,
                Revenue = month == 3 ? 2250m : month == 6 ? 4500m : 0,
                MarginAmount = month == 3 ? 1500m : month == 6 ? 3000m : 0,
                Cost = month == 3 ? 750m : month == 6 ? 1500m : 0
            });
        }
```

- [ ] **Step 7: Verify no other MonthlyMarginBreakdown references remain in this test file**

Run: `grep -n 'GetProductMarginAnalysisResponse\.MonthlyMarginBreakdown' backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs`
Expected: no output (empty). If matches appear, replace each with `MonthlyMarginBreakdownDto`.

- [ ] **Step 8: Build the test project**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS with zero warnings.

- [ ] **Step 9: Run the two affected test classes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetMarginReportHandlerTests|FullyQualifiedName~GetProductMarginAnalysisHandlerTests"
```
Expected: all tests in those two classes pass. No new failures, no skipped tests beyond any that were already skipped.

- [ ] **Step 10: Run the entire backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: full green; same pass/fail count as before this refactor (i.e., no regressions in other tests).

- [ ] **Step 11: Commit the full refactor**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs
git commit -m "refactor(analytics): decouple IReportBuilderService from UseCase response types"
```

---

## Task 8: Verify dependency-direction invariant

Make sure the `Services/` layer no longer references any `UseCases/` type.

- [ ] **Step 1: Grep for forbidden using-direction**

Run: `grep -rn 'Anela.Heblo.Application.Features.Analytics.UseCases' backend/src/Anela.Heblo.Application/Features/Analytics/Services/`
Expected: no output (empty). A single match means a `UseCases → Services` import survived; revisit Task 4 Step 1.

- [ ] **Step 2: Grep for forbidden type references**

Run: `grep -rn 'GetMarginReportResponse\|GetProductMarginAnalysisResponse' backend/src/Anela.Heblo.Application/Features/Analytics/Services/`
Expected: no output (empty).

---

## Task 9: Verify OpenAPI surface and generated TypeScript client are unchanged

The HTTP/JSON wire shape and OpenAPI schema must be byte-identical (NFR-2, FR-5). The repo's build regenerates the C# client (`AnelaHebloApiClient.cs`) and the TypeScript client (`frontend/src/api/generated/api-client.ts`) automatically — they must show empty `git diff`.

- [ ] **Step 1: Full backend build (triggers generated-client regeneration)**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: PASS with zero new warnings (NFR-3).

- [ ] **Step 2: Diff the generated C# client**

Run: `git diff --stat -- backend/src/Anela.Heblo.API.Client/Generated/`
Expected: no output (empty diff).

If the diff is non-empty, the OpenAPI schema drifted — most likely cause is a field-name or type mismatch between the new DTO and the nested response type that triggers the OpenAPI generator to surface the DTO. Re-read Task 3 Step 3 and reconcile.

- [ ] **Step 3: Diff the generated TypeScript client**

Run: `git diff --stat -- frontend/src/api/generated/`
Expected: no output (empty diff).

- [ ] **Step 4: Run `dotnet format` to confirm style compliance (NFR-3)**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: exit code 0, no diff.

If `dotnet format` reports changes, run `dotnet format backend/Anela.Heblo.sln` and amend the previous commit (or add a follow-up commit, per CLAUDE.md preference for new commits over amends):

```bash
git add -u
git commit -m "style: dotnet format after analytics refactor"
```

- [ ] **Step 5: Final full test run**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: PASS — every test that passed before still passes, no new failures, no new skips.

---

## Self-Review (already performed by plan author)

**Spec coverage:**
- FR-1 (three DTOs under Contracts) → Tasks 1, 2, 3.
- FR-2 (updated `IReportBuilderService` interface) → Task 4 Step 1.
- FR-3 (updated `ReportBuilderService` implementation, byte-for-byte calc preservation) → Task 4 Step 1.
- FR-4 (handler-local inline projections, no global mapper) → Tasks 5 and 6.
- FR-5 (nested response types preserved, OpenAPI / TS client unchanged) → Task 9 Steps 2-3.
- NFR-1 (performance — O(n) projection) → satisfied by inline `Select` in Tasks 5 and 6.
- NFR-2 (backward compatibility, no schema or wire changes) → Task 9.
- NFR-3 (zero new warnings, `dotnet format` clean) → Task 9 Steps 1 and 4.
- NFR-4 (existing tests pass, no coverage drop) → Task 7 Steps 9-10, Task 9 Step 5.
- Amendment 1 (test file naming) → handled by referencing the actual files `GetMarginReportHandlerTests.cs` and `GetProductMarginAnalysisHandlerTests.cs` throughout.
- Amendment 2 (single projection seam in `GetMarginReportHandler`) → Task 6 explicitly switches `ReportData` to DTO lists and projects once in `BuildSuccessResponse`.
- Amendment 3 (explicit OpenAPI / TS-client diff gate) → Task 9 Steps 2-3.
- Amendment 4 (`ReportData`/`OverallTotals` stay handler-private) → Task 6 keeps both as `internal class` in the same file; `OverallTotals` is not touched at all.

**Placeholder scan:** no TBDs, no "implement later", no "similar to Task N" hand-offs; every code step shows the full target snippet.

**Type consistency:** the three DTO names (`MonthlyMarginBreakdownDto`, `CategoryMarginSummaryDto`, `ProductMarginSummaryDto`), the three interface method signatures, the three projection lambdas, and the test mock return types all use the same exact identifiers across Tasks 1-7.
