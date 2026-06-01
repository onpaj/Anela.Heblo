# Architecture Review: Decouple IReportBuilderService from UseCase Response Types

## Skip Design: true

Backend-only refactor. No UI components, screens, or visual changes. The OpenAPI surface and generated TS client are required to be byte-identical.

## Architectural Fit Assessment

The proposed refactor restores the intended dependency direction defined in `docs/architecture/filesystem.md:31` and `docs/architecture/development_guidelines.md`: **UseCases → Services → Contracts**, with `Contracts/` as the home for shared DTOs (already populated with `AnalysisMarginData`, `TopProductDto`, `MonthlyProductMarginDto`, `ProductMarginSegmentDto`). The current code violates this by having `Services/ReportBuilderService.cs:3-4` import from two `UseCases/` namespaces.

Integration points (verified against the codebase):

- **Interface + implementation**: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` (only one file — interface and class are co-located).
- **Consumers**: exactly two handlers — `GetProductMarginAnalysisHandler.cs:108` (calls `BuildMonthlyBreakdown`) and `GetMarginReportHandler.cs:130,144` (calls `BuildProductSummary`, `BuildCategorySummaries`).
- **Tests**: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` and `GetProductMarginAnalysisHandlerTests.cs`. No standalone `ReportBuilderServiceTests` exists despite the spec referencing one (FR-3, NFR-4) — see Specification Amendments.
- **DI**: `AnalyticsModule.cs:30` (`services.AddScoped<IReportBuilderService, ReportBuilderService>()`). Unchanged by the refactor.
- **No other call sites** for `IReportBuilderService`. The proposal does not over- or under-reach.
- **Nested types stay**: `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` and `GetMarginReportResponse.{Product,Category}MarginSummary` remain in their response classes; the OpenAPI schema and `frontend/src/api/generated/api-client.ts` shape must remain unchanged.

The proposal aligns with the existing Contracts conventions (plain `public class` with auto-properties, no behavior, file-per-type — see `AnalysisMarginData.cs`, `TopProductDto.cs`).

## Proposed Architecture

### Component Overview

```
Features/Analytics/
├── Contracts/                              ◄── shared, framework-free DTOs
│   ├── AnalysisMarginData.cs               (existing, input to service)
│   ├── MonthlyMarginBreakdownDto.cs        (NEW)
│   ├── CategoryMarginSummaryDto.cs         (NEW)
│   ├── ProductMarginSummaryDto.cs          (NEW)
│   └── ... (existing)
│
├── Services/
│   └── ReportBuilderService.cs             ◄── now returns Contracts DTOs only
│       └─ depends on: Contracts, Models, Domain.Features.Analytics
│       └─ MUST NOT depend on: UseCases/*
│
└── UseCases/
    ├── GetProductMarginAnalysis/
    │   ├── GetProductMarginAnalysisResponse.cs  (nested types preserved)
    │   └── GetProductMarginAnalysisHandler.cs   ◄── projects DTO → nested type
    └── GetMarginReport/
        ├── GetMarginReportResponse.cs           (nested types preserved)
        └── GetMarginReportHandler.cs            ◄── projects DTO → nested type
```

Final dependency arrows:

```
UseCases ──────▶ Services ──────▶ Contracts
    │                                ▲
    └─────────────depends on─────────┘
```

### Key Design Decisions

#### Decision 1: Projection location in `GetMarginReportHandler`

**Options considered:**
- (A) Change the handler-internal `ReportData` helper (`GetMarginReportHandler.cs:213-218`) to hold `List<ProductMarginSummaryDto>` / `List<CategoryMarginSummaryDto>`, then project to nested response types once in `BuildSuccessResponse`.
- (B) Project to nested response types immediately at the call sites (line 130 and line 144), keeping `ReportData` typed against the existing nested response types.

**Chosen approach:** (A).

**Rationale:** Internal pipeline state operates in the contracts vocabulary; the projection happens once at the response-assembly seam. This keeps the sort on line 141 (`OrderByDescending(p => p.M2Percentage)`) operating on contract DTOs (M2Percentage is preserved field-for-field, so the sort behavior is identical), and confines the `Select(...)` projection to a single, easily-reviewed location. Option B scatters two projection sites and forces `ReportData` to keep importing nested response types — a partial fix.

#### Decision 2: Projection mechanism — inline `Select`, no mapper

**Options considered:**
- (A) Inline LINQ `Select(...)` projection — one expression per type.
- (B) Private static `ToResponseType(...)` extension/helper in the handler file.
- (C) AutoMapper profile (the project already uses AutoMapper for some features).

**Chosen approach:** (A), with optional escalation to (B) only if a `Select` lambda exceeds ~8 fields and hurts readability — `ProductMarginSummary` has 16 fields and is a reasonable candidate for a private static method.

**Rationale:** Matches FR-4 ("Do not introduce a global mapper, AutoMapper profile, or shared mapping utility"). AutoMapper is overkill for one-to-one trivial copies and obscures field correspondence — explicit assignment makes the byte-identical wire-shape guarantee (NFR-2) auditable.

#### Decision 3: DTO class shape

**Options considered:** `record` vs `class`.

**Chosen approach:** `public class` with public get/set auto-properties, matching the existing Contracts files (`TopProductDto.cs`, `AnalysisMarginData.cs`).

**Rationale:** Project rule in `CLAUDE.md` ("DTOs are classes, never C# records") because the NSwag OpenAPI generator mishandles record parameter order. Even though these DTOs are not currently in the OpenAPI surface, the rule applies prophylactically — exposing them later would otherwise force a breaking type change.

#### Decision 4: Preserve nested types on responses

**Chosen approach:** Leave `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` and `GetMarginReportResponse.{Product,Category}MarginSummary` exactly as they are.

**Rationale:** They define the public OpenAPI schema; the regenerated TypeScript client (`frontend/src/api/generated/api-client.ts`) must be unchanged (NFR-2). Verification gate: run the API project build and `git diff` the generated swagger and TS client — empty diff is the pass criterion.

## Implementation Guidance

### Directory / Module Structure

Files to create:

```
backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/
├── MonthlyMarginBreakdownDto.cs   (NEW)
├── CategoryMarginSummaryDto.cs    (NEW)
└── ProductMarginSummaryDto.cs     (NEW)
```

Files to modify:

```
backend/src/Anela.Heblo.Application/Features/Analytics/
├── Services/ReportBuilderService.cs
│   - Remove `using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;`
│   - Remove `using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;`
│   - Change interface return types and instantiations to the new DTOs
│
└── UseCases/
    ├── GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs
    │   - Line ~108: add `.Select(dto => new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown { ... }).ToList()`
    │
    └── GetMarginReport/GetMarginReportHandler.cs
        - `ReportData` (line 213): change to `List<ProductMarginSummaryDto>` / `List<CategoryMarginSummaryDto>`
        - `BuildSuccessResponse` (line 176): project both lists to nested types before assigning to `ProductSummaries` / `CategorySummaries`
```

Tests to update (assertions only — no logic changes):

```
backend/test/Anela.Heblo.Tests/Features/Analytics/
├── GetMarginReportHandlerTests.cs
└── GetProductMarginAnalysisHandlerTests.cs
```

### Interfaces and Contracts

DTOs must be transcribed verbatim from the existing nested types (authoritative sources: `GetProductMarginAnalysisResponse.cs:18-25` and `GetMarginReportResponse.cs:18-53`):

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

New `IReportBuilderService` signature:

```csharp
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

### Data Flow

**GetProductMarginAnalysis** (single projection site):

```
Request → Handler.Handle
  → AnalyticsRepository.GetProductAnalysisDataAsync → AnalyticsProduct
  → CalculateProductMargins(...) → AnalysisMarginData
  → ReportBuilderService.BuildMonthlyBreakdown(...) → List<MonthlyMarginBreakdownDto>
  → [PROJECT]  .Select(d => new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown { ... })
  → response.MonthlyBreakdown
```

**GetMarginReport** (project once at assembly):

```
Request → Handler.Handle
  → ProductFilterService.FilterProductsAsync → List<AnalyticsProduct>
  → ProcessProductsForReport(...)
      ├─ ReportBuilderService.BuildProductSummary → ProductMarginSummaryDto         (accumulates in ReportData)
      └─ ReportBuilderService.BuildCategorySummaries → List<CategoryMarginSummaryDto> (assigned in ReportData)
  → Sort productSummaries by M2Percentage (operates on ProductMarginSummaryDto)
  → BuildSuccessResponse
      ├─ [PROJECT] ProductMarginSummaryDto → GetMarginReportResponse.ProductMarginSummary
      └─ [PROJECT] CategoryMarginSummaryDto → GetMarginReportResponse.CategoryMarginSummary
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Field drift between Contract DTO and nested response type during transcription | Medium | Transcribe field lists side-by-side from the authoritative `GetMarginReportResponse.cs:18-53` and `GetProductMarginAnalysisResponse.cs:18-25`; reviewer diff-checks property names, types, and defaults. |
| OpenAPI / TS client unintentionally changes | High (breaks frontend) | Mandatory: run backend build (which regenerates the TS client via PostBuild) and `git diff` `frontend/src/api/generated/api-client.ts` and `backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs`. Empty diff is required to merge. |
| Sort behavior changes when `ReportData.ProductSummaries` switches from response nested type to Contract DTO | Low | The `M2Percentage` field is preserved verbatim; sort on `decimal` is unchanged. Existing handler tests assert sort order — they must keep passing. |
| Name collision: `ProductMarginSummaryDto` reads similarly to the unrelated `GetProductMarginSummary` use case | Low | They live in distinct namespaces (`Contracts` vs `UseCases/GetProductMarginSummary`). Adding a single-line XML doc comment on the new DTO clarifies it belongs to margin **reports**, not the summary use case. |
| Tests assert against nested types and break wholesale | Low | Spec FR-3 explicitly allows test type-reference swaps; no behavioral test changes needed. Reviewer confirms the diff in test files contains only type-name changes. |
| `using` directive cleanup misses one site, leaving a `Services → UseCases` arrow | Medium | Verification: `grep -rn "Anela.Heblo.Application.Features.Analytics.UseCases" backend/src/.../Services/` must return zero results after the refactor. Add this check to the PR description. |
| `dotnet format` introduces unrelated whitespace churn | Low | Run `dotnet format` once at the end and commit separately if it touches files outside the change set. |

## Specification Amendments

1. **FR-3 / NFR-4 — "Existing unit tests for `ReportBuilderService`"**: there is no `ReportBuilderServiceTests.cs` file in `backend/test/Anela.Heblo.Tests/Features/Analytics/`. Treat the spec text as referring to the handler test files (`GetMarginReportHandlerTests.cs`, `GetProductMarginAnalysisHandlerTests.cs`), which do exercise the service indirectly. No new tests are required by this refactor (behavior unchanged), but the spec wording should be updated to reference the actual test files.

2. **FR-4 — projection location in `GetMarginReportHandler`**: clarify that the internal `ReportData` helper class (`GetMarginReportHandler.cs:213-218`) switches to hold `List<ProductMarginSummaryDto>` and `List<CategoryMarginSummaryDto>`, and that projection to nested response types happens exactly once inside `BuildSuccessResponse`. Without this clarification, an implementer might add two projection sites instead of one.

3. **Verification step — explicit OpenAPI diff gate**: add an acceptance step to FR-5: "After the refactor, run `dotnet build` and confirm `git diff` reports zero changes in `backend/src/Anela.Heblo.API.Client/Generated/` and `frontend/src/api/generated/`."

4. **Out of Scope — internal `ReportData` helper**: optionally call out that the `internal class ReportData` and `internal class OverallTotals` declarations at the bottom of `GetMarginReportHandler.cs` are touched only to swap collection element types — they are not promoted to Contracts and remain handler-private.

## Prerequisites

None. This is a pure in-process refactor:

- No new NuGet packages.
- No DI changes (`AnalyticsModule.cs:30` registration is unchanged).
- No migrations, no config, no infrastructure changes.
- No environment, Key Vault, or feature-flag work.

Standard validation gate before merge (per `CLAUDE.md`):

- `dotnet build` — succeeds with no new warnings.
- `dotnet format` — produces no diff.
- All tests in `Anela.Heblo.Tests` pass; specifically the two affected handler test files.
- Generated OpenAPI + TS client files show empty `git diff`.