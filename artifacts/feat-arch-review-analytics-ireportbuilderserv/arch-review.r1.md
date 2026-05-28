# Architecture Review: Decouple IReportBuilderService from UseCase Response Types

## Skip Design: true

Backend-only refactor. No UI components, screens, or visual changes. The OpenAPI surface and generated TypeScript client are explicitly unchanged.

## Architectural Fit Assessment

The proposed refactor aligns directly with the documented architecture in `docs/architecture/filesystem.md` (lines 152–158) and `docs/architecture/development_guidelines.md` (lines 8–25): `Contracts/` is the canonical home for shared DTOs within a feature, and `Services/` should expose those contracts — not import from `UseCases/`. The existing `Analytics/Contracts/` folder already holds analogous types (`AnalysisMarginData`, `TopProductDto`, `MonthlyProductMarginDto`, `ProductMarginSegmentDto`) with the exact style the spec prescribes (public classes, settable auto-properties, `= string.Empty` defaults, one type per file, no XML docs). The new DTOs slot in without inventing a new pattern.

The integration points are surgical and well-bounded:
- One interface (`IReportBuilderService`) and its single implementation.
- Two handlers (`GetMarginReportHandler`, `GetProductMarginAnalysisHandler`).
- Two mock-based test files that wire setup returns.
- One internal helper (`ReportData`) inside `GetMarginReportHandler.cs`.

Nothing else in the codebase consumes these nested types or the service. DI registration in `AnalyticsModule.cs` is unaffected. No domain, persistence, controller, or OpenAPI changes occur.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│  UseCases/GetMarginReport/                                              │
│    GetMarginReportHandler ─── consumes ──┐                              │
│      └── ReportData (internal helper, holds *Dto lists)                 │
│                                          │                              │
│  UseCases/GetProductMarginAnalysis/      │                              │
│    GetProductMarginAnalysisHandler ──────┤                              │
│                                          ▼                              │
│  Services/                       IReportBuilderService                  │
│    ReportBuilderService ─── implements ──┘                              │
│      returns ──┐                                                        │
│                ▼                                                        │
│  Contracts/                                                             │
│    ┌── MonthlyMarginBreakdownDto   (NEW)                                │
│    ├── CategoryMarginSummaryDto    (NEW)                                │
│    ├── ProductMarginSummaryDto     (NEW)                                │
│    ├── AnalysisMarginData          (existing — service input)           │
│    └── …                                                                │
│                ▲                                                        │
│                │                                                        │
│  Domain/Features/Analytics/                                             │
│    AnalyticsProduct, SalesDataPoint  (existing — service input)         │
└─────────────────────────────────────────────────────────────────────────┘

Dependency arrows after refactor:
  UseCases  →  Services  →  Contracts  →  Domain
  Handlers ALSO retain their own  →  UseCases/*Response  (still own the nested types)
```

The two handlers continue to own their nested response types (`GetMarginReportResponse.ProductMarginSummary`, etc.) for HTTP/OpenAPI stability. They perform a 1:1 projection from the Contract DTO to the nested response type at the moment they assemble the response.

### Key Design Decisions

#### Decision 1: Where mapping from DTO → nested response type lives

**Options considered:**
- **(a)** Map per-item inside the loop in `ProcessProductsForReport`, keeping `ReportData` typed against `GetMarginReportResponse.*` nested types (unchanged shape).
- **(b)** Type `ReportData` against the new `*Dto` lists, project once inside `BuildSuccessResponse` (and project the category list there as well).
- **(c)** Introduce a public mapper class (e.g. `AnalyticsResponseMapper`) in the UseCase folder.

**Chosen approach:** **(b)** — `ReportData` holds DTOs; mapping happens exactly once per call site in `BuildSuccessResponse`, via a small private static helper inside `GetMarginReportHandler.cs` (e.g. `ToResponseSummary(ProductMarginSummaryDto)` and `ToResponseSummary(CategoryMarginSummaryDto)`). The handler for `GetProductMarginAnalysis` does the same in its own file.

**Rationale:**
- One mapping site per type is the easiest to audit during the refactor and the easiest place for future readers to find the "where do these field copies happen?" answer.
- The sort `OrderByDescending(p => p.M2Percentage)` (line 141 of `GetMarginReportHandler.cs`) works identically whether `productSummaries` is `List<ProductMarginSummaryDto>` or `List<GetMarginReportResponse.ProductMarginSummary>` — both expose `M2Percentage` — so option (b) does not perturb the sort.
- `ReportData` is `internal`. Changing its element types is local to one file and not observable elsewhere.
- (c) is overengineered for three simple field-copy projections used by exactly two callers each.

#### Decision 2: DTO style and file layout

**Options considered:**
- Mirror `TopProductDto.cs` (public class, no XML docs, settable auto-properties, `string` defaults `= string.Empty`).
- Add XML documentation comments to the new DTOs.
- Use C# records.

**Chosen approach:** Mirror `TopProductDto.cs` exactly. No XML docs. Public classes (CLAUDE.md hard rule: **DTOs are classes, never records** — OpenAPI client generators mishandle record parameter order). One DTO per file, file name matches class name.

**Rationale:** Matches every existing file in `Analytics/Contracts/` and the project-wide DTO rule. New DTOs would look out of place with XML docs the others lack.

#### Decision 3: Nested response types stay on the response classes

**Options considered:**
- Replace `GetMarginReportResponse.ProductMarginSummary` with the new `ProductMarginSummaryDto` at the HTTP boundary.
- Keep the nested types on `GetMarginReportResponse` / `GetProductMarginAnalysisResponse`.

**Chosen approach:** Keep them. They define the OpenAPI surface; touching them produces a TypeScript client diff and is explicitly out of scope per the spec.

**Rationale:** The refactor's goal is dependency direction, not API surface change. Replacing the nested types would force a generated-client churn and risk frontend regressions for zero architectural benefit beyond what this refactor already secures.

## Implementation Guidance

### Directory / Module Structure

Create three new files under `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/`:

```
Contracts/
├── AnalysisMarginData.cs               (existing)
├── IAnalyticsProductSource.cs          (existing)
├── MonthlyProductMarginDto.cs          (existing)
├── ProductMarginSegmentDto.cs          (existing)
├── TopProductDto.cs                    (existing)
├── MonthlyMarginBreakdownDto.cs        ← NEW
├── CategoryMarginSummaryDto.cs         ← NEW
└── ProductMarginSummaryDto.cs          ← NEW
```

Modify:
- `Features/Analytics/Services/ReportBuilderService.cs` — remove both `using …UseCases.GetMarginReport;` and `using …UseCases.GetProductMarginAnalysis;`; change interface signatures and implementation return types to the new DTOs.
- `Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — change `ReportData` field types to `List<ProductMarginSummaryDto>` / `List<CategoryMarginSummaryDto>`; add one private static `ToResponseSummary` helper per type; project inside `BuildSuccessResponse`.
- `Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — wrap the `BuildMonthlyBreakdown` call with a projection (object initializer or small helper) when assigning `response.MonthlyBreakdown`.

Tests requiring updates (mock setup return types only, not assertions):
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` (lines 82–107)
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` (lines 73–92)

No changes to: `AnalyticsModule.cs`, the controller, the responses themselves, the persistence layer, the frontend, OpenAPI clients, or `appsettings`.

### Interfaces and Contracts

**New contract types** — exact shapes (mirror existing nested types and the spec's FR-1 acceptance criteria):

```csharp
// Contracts/MonthlyMarginBreakdownDto.cs
namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class MonthlyMarginBreakdownDto
{
    public DateTime Month { get; set; }
    public decimal MarginAmount { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public int UnitsSold { get; set; }
}

// Contracts/CategoryMarginSummaryDto.cs
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

// Contracts/ProductMarginSummaryDto.cs
namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class ProductMarginSummaryDto
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal MarginAmount { get; set; }
    public decimal M0Amount { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M2Amount { get; set; }
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal MarginPercentage { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public int UnitsSold { get; set; }
}
```

**Updated service interface (final form):**

```csharp
// Services/ReportBuilderService.cs
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
```

**Handler mapping pattern (illustrative):**

```csharp
// GetMarginReportHandler.cs — inside BuildSuccessResponse
ProductSummaries = reportData.ProductSummaries.Select(ToResponseSummary).ToList(),
CategorySummaries = reportData.CategorySummaries.Select(ToResponseSummary).ToList()

private static GetMarginReportResponse.ProductMarginSummary ToResponseSummary(ProductMarginSummaryDto d)
    => new()
    {
        ProductId = d.ProductId,
        ProductName = d.ProductName,
        Category = d.Category,
        MarginAmount = d.MarginAmount,
        M0Amount = d.M0Amount, M1Amount = d.M1Amount, M2Amount = d.M2Amount,
        M0Percentage = d.M0Percentage, M1Percentage = d.M1Percentage, M2Percentage = d.M2Percentage,
        SellingPrice = d.SellingPrice, PurchasePrice = d.PurchasePrice,
        MarginPercentage = d.MarginPercentage,
        Revenue = d.Revenue, Cost = d.Cost, UnitsSold = d.UnitsSold,
    };

private static GetMarginReportResponse.CategoryMarginSummary ToResponseSummary(CategoryMarginSummaryDto d)
    => new()
    {
        Category = d.Category,
        TotalMargin = d.TotalMargin,
        TotalRevenue = d.TotalRevenue,
        AverageMarginPercentage = d.AverageMarginPercentage,
        ProductCount = d.ProductCount,
        TotalUnitsSold = d.TotalUnitsSold,
    };
```

```csharp
// GetProductMarginAnalysisHandler.cs — inside BuildSuccessResponse
var breakdownDtos = _reportBuilderService.BuildMonthlyBreakdown(
    salesInPeriod, productData, request.StartDate, request.EndDate);

response.MonthlyBreakdown = breakdownDtos.Select(d => new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown
{
    Month = d.Month,
    MarginAmount = d.MarginAmount,
    Revenue = d.Revenue,
    Cost = d.Cost,
    UnitsSold = d.UnitsSold,
}).ToList();
```

**`ReportData` (internal helper) — updated:**

```csharp
internal class ReportData
{
    public List<ProductMarginSummaryDto> ProductSummaries { get; set; } = new();
    public List<CategoryMarginSummaryDto> CategorySummaries { get; set; } = new();
    public OverallTotals OverallTotals { get; set; } = new();
}
```

### Data Flow

**`GetMarginReport` request flow (key path only):**
1. `GetMarginReportHandler.Handle` → `ProcessProductsForReport`.
2. Per product: `_reportBuilderService.BuildProductSummary(product, marginData)` returns `ProductMarginSummaryDto` → added directly to `List<ProductMarginSummaryDto>`.
3. `OrderByDescending(p => p.M2Percentage)` sorts the DTO list in place (no shape change).
4. `_reportBuilderService.BuildCategorySummaries(categoryTotals)` returns `List<CategoryMarginSummaryDto>`.
5. `BuildSuccessResponse` projects each DTO list into the corresponding `GetMarginReportResponse.*` nested type using `ToResponseSummary`.
6. HTTP response shape is identical to today.

**`GetProductMarginAnalysis` request flow:**
1. `GetProductMarginAnalysisHandler.Handle` → `BuildSuccessResponse`.
2. If `IncludeBreakdown`: `_reportBuilderService.BuildMonthlyBreakdown(...)` returns `List<MonthlyMarginBreakdownDto>`.
3. Inline projection assigns `response.MonthlyBreakdown` as `List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown>`.
4. HTTP response shape is identical to today.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Mock setups in `GetMarginReportHandlerTests` / `GetProductMarginAnalysisHandlerTests` return the old nested types and will fail to compile after the interface change | High (build breaks until updated) | Update each `.Returns(...)` setup to produce the new `*Dto` types in the same edit pass as the interface change. Test assertions on response contents remain unchanged because the handler still emits the same nested response shapes. |
| Field drift between DTO and nested type during the manual copy (e.g., a property typo) | Medium | The five DTOs/types each have a single mapping site. Compile fails the moment a property is missing; FluentAssertions in the existing tests catch any value mismatch. The spec freezes the exact 5/6/15 field lists per type — use them as checklists. |
| `OrderByDescending(p => p.M2Percentage)` accidentally sorts a different type than expected after refactor | Low | Both `ProductMarginSummaryDto` and `GetMarginReportResponse.ProductMarginSummary` expose `M2Percentage` with identical type/semantics. Sorting on the DTO produces identical ordering. Existing tests that assert ordering by M2 percentage will catch any regression. |
| OpenAPI-generated TypeScript client diff appears unexpectedly | Low | The nested types on the response classes are untouched, so the OpenAPI spec is unchanged. If the build's PostBuild step still produces a diff, treat it as a regression — investigate before merging. |
| Reviewer confusion that two nearly-identical types now exist (the DTO and the nested response type) | Low | The duplication is intentional and load-bearing for dependency direction. Document the choice succinctly in the PR description (not in code comments — existing code style uses neither). |
| `dotnet format` introduces incidental churn in the touched files | Low | Run `dotnet format` once on the touched files at the end of the change so style and the refactor land in the same commit. |

## Specification Amendments

The spec is complete and accurate. Two small clarifications, neither a change in behavior:

1. **FR-3 acceptance criterion on `ReportData`:** The spec offers implementer's choice between (a) holding DTOs in `ReportData` and mapping once at the end, and (b) holding nested types and mapping per-item. This review **picks (a)** — `ReportData` holds DTOs and `BuildSuccessResponse` performs the projection — for the reasons in Decision 1. Implementer should follow this unless they discover a concrete reason to prefer (b).

2. **FR-4 acceptance criterion on tests:** The mock `.Returns(...)` setups in `GetMarginReportHandlerTests.cs` (lines 82–107) and `GetProductMarginAnalysisHandlerTests.cs` (lines 73–92) **must** change to produce the new DTO types — this is not optional and is the only test change required. Assertions on response contents remain untouched. The spec mentions this in FR-4 but is worth surfacing here because it's the only place the change is non-mechanical for the test author (the constructor call's type name changes).

## Prerequisites

None. The change is purely additive (three new files in `Contracts/`) plus in-place edits to five existing files (one service, two handlers, two test files). No migrations, no DI changes (`AnalyticsModule.cs` is untouched per FR-5), no infrastructure, no config, no feature flags, no Key Vault secrets, no Docker rebuild beyond the normal build cycle. .NET 8 SDK and existing project references are sufficient. Validation gate: `dotnet build`, `dotnet format`, and the two affected handler test classes must pass with assertion bodies unchanged.