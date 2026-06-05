# Consolidate Duplicate Analytics DTOs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete three response-nested wrapper classes in the Analytics module and collapse the verbatim handler `Select(...new...)` re-projections to direct assignments, so each margin-related concept has exactly one type (the `Contracts/` DTO) instead of two.

**Architecture:** Pure backend refactor — the four files in `Application/Features/Analytics/UseCases/GetMarginReport/` and `.../GetProductMarginAnalysis/` lose nested types and the matching `.Select(...)` mapping blocks. `IReportBuilderService` already returns the `Contracts/` DTOs, so no service interface changes. The TypeScript client regenerates automatically on `dotnet build` (NSwag prebuild), renaming three generated classes (`ProductMarginSummary` → `ProductMarginSummaryDto`, etc.). The HTTP/JSON wire shape is byte-equivalent before and after.

**Tech Stack:** C# / .NET 8, MediatR (handler boundary), NSwag (OpenAPI/TS client), xUnit + Moq + FluentAssertions (BE tests), React + TypeScript (FE consumer of generated client).

---

## File Structure

**Files modified (4):**
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportResponse.cs` — delete nested classes, change collection element types.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — collapse two `Select(...)` blocks (lines 159–190) to direct assignments.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisResponse.cs` — delete nested class, change collection element type.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — collapse one `Select(...)` block (lines 93–103) to a direct assignment; preserve `?? []` null-guard.

**Files regenerated (do not hand-edit):**
- `frontend/src/api/generated/api-client.ts` — three TS classes + interfaces renamed automatically on `dotnet build` and on FE `prebuild`.

**Files unchanged (verified by reading):**
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSummaryDto.cs` — canonical shape, already correct.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/CategoryMarginSummaryDto.cs` — canonical shape, already correct.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/MonthlyMarginBreakdownDto.cs` — canonical shape, already correct.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` (and `IReportBuilderService` interface inside it) — already returns `ProductMarginSummaryDto`, `List<CategoryMarginSummaryDto>`, `List<MonthlyMarginBreakdownDto>`. No interface or implementation change.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` and `GetProductMarginAnalysisHandlerTests.cs` — tests already use `Contracts/` DTO names only; no test edits required. They serve as the regression net.
- `frontend/src/**` (hand-written) — `ProductMarginSummary.tsx` page is for the unrelated `GetProductMarginSummary` use case; no hand-written code references the nested-class generated types being renamed.

---

## Field-Identity Verification (do this once, before editing)

Each of the three pairs is field-identical in the current code. Confirmed by reading both sides during planning, but re-verify in Step 1 of Tasks 2 and 4 before deleting. Reference diff (for confirmation, not for change):

| Pair | Nested fields | Contracts/ DTO fields | Identical? |
|---|---|---|---|
| `GetMarginReportResponse.ProductMarginSummary` ↔ `ProductMarginSummaryDto` | `ProductId:string`, `ProductName:string`, `Category:string`, `MarginAmount:decimal`, `M0Amount`, `M1Amount`, `M2Amount`, `M0Percentage`, `M1Percentage`, `M2Percentage`, `SellingPrice`, `PurchasePrice`, `MarginPercentage`, `Revenue`, `Cost`, `UnitsSold:int` | same 16 fields | yes |
| `GetMarginReportResponse.CategoryMarginSummary` ↔ `CategoryMarginSummaryDto` | `Category:string`, `TotalMargin`, `TotalRevenue`, `AverageMarginPercentage`, `ProductCount:int`, `TotalUnitsSold:int` | same 6 fields | yes |
| `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` ↔ `MonthlyMarginBreakdownDto` | `Month:DateTime`, `MarginAmount`, `Revenue`, `Cost`, `UnitsSold:int` | same 5 fields | yes |

If the verification step in any task surfaces an unexpected divergence: STOP, report the difference in the PR description, and add the missing field to the `Contracts/` DTO before deleting the nested class. Do not silently drop fields.

---

## Task 1: Baseline verification

Confirm the starting state is green so any failure after edits is attributable to the refactor.

**Files:**
- Read only: none modified.

- [ ] **Step 1: Confirm working directory is clean and current branch**

Run: `git status && git rev-parse --abbrev-ref HEAD`

Expected: working tree clean (or only the plan file modified), branch is the feature branch.

- [ ] **Step 2: Run a full backend build**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: build succeeds with 0 errors. Note the warning count for comparison after edits.

- [ ] **Step 3: Run the Analytics test subset to establish baseline green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Analytics" --no-build`

Expected: all Analytics tests pass.

- [ ] **Step 4: Confirm the current generated TS client uses the old type names**

Run: `grep -n "^export class \\(ProductMarginSummary\\|CategoryMarginSummary\\|MonthlyMarginBreakdown\\) " frontend/src/api/generated/api-client.ts`

Expected: three matches — `MonthlyMarginBreakdown`, `ProductMarginSummary`, `CategoryMarginSummary`. (These three are exactly what will rename after regeneration.)

- [ ] **Step 5: No commit yet — this task is read-only**

Proceed to Task 2.

---

## Task 2: Refactor `GetMarginReport` (Response + Handler together)

The response file and the handler must be edited in one commit because the handler references the nested types. Splitting would leave the build red between commits.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs:159-190`
- Regression net (no edit): `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`

- [ ] **Step 1: Re-verify field identity for both pairs**

Read both files and confirm fields match the table in "Field-Identity Verification". If anything has diverged since the plan was written, stop and report.

Run:
```bash
grep -nE "^\s+public\s" backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportResponse.cs
grep -nE "^\s+public\s" backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSummaryDto.cs
grep -nE "^\s+public\s" backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/CategoryMarginSummaryDto.cs
```

Expected: nested class property lines match Contracts/ DTO property lines field-for-field for both pairs.

- [ ] **Step 2: Update `GetMarginReportResponse.cs` — replace entire file content**

Replace the full file contents with:

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;

public class GetMarginReportResponse : BaseResponse
{
    public DateTime ReportPeriodStart { get; set; }
    public DateTime ReportPeriodEnd { get; set; }
    public decimal TotalMargin { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal AverageMarginPercentage { get; set; }
    public int TotalProductsAnalyzed { get; set; }
    public int TotalUnitsSold { get; set; }
    public List<ProductMarginSummaryDto> ProductSummaries { get; set; } = new();
    public List<CategoryMarginSummaryDto> CategorySummaries { get; set; } = new();
}
```

What changed:
- Added `using Anela.Heblo.Application.Features.Analytics.Contracts;`.
- `ProductSummaries` element type: `ProductMarginSummary` → `ProductMarginSummaryDto`.
- `CategorySummaries` element type: `CategoryMarginSummary` → `CategoryMarginSummaryDto`.
- Deleted nested classes `ProductMarginSummary` (lines 18–43) and `CategoryMarginSummary` (lines 45–52).

- [ ] **Step 3: Update `GetMarginReportHandler.cs` — replace the `Select(...)` blocks with direct assignments**

In `GetMarginReportHandler.cs`, find the `BuildSuccessResponse` block (lines 142–192). Replace the `ProductSummaries = ...` and `CategorySummaries = ...` initializer lines.

Use `Edit` with `old_string`:

```csharp
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
```

And `new_string`:

```csharp
            ProductSummaries = reportData.ProductSummaries,
            CategorySummaries = reportData.CategorySummaries.ToList()
```

Notes:
- `reportData.ProductSummaries` is already `List<ProductMarginSummaryDto>` (see `ReportData` at the bottom of the same file, line 211). Direct assignment is type-compatible.
- `reportData.CategorySummaries` is `List<CategoryMarginSummaryDto>` (line 212), so `.ToList()` is redundant but safe. Use it to keep symmetry with the original `.ToList()` call and to avoid sharing the list reference with internal state (defensive copy — matches the pre-refactor behaviour, which materialised a new list per call).

- [ ] **Step 4: Build the backend**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

Expected: 0 errors, warning count unchanged from baseline.

If you get `CS0246: The type or namespace name 'ProductMarginSummary' could not be found`, you missed a reference — search:

Run: `grep -rn "GetMarginReportResponse\\.ProductMarginSummary\\|GetMarginReportResponse\\.CategoryMarginSummary" backend/`

Expected: zero matches. (Pre-edit there were two, both in `GetMarginReportHandler.cs`.)

- [ ] **Step 5: Run `GetMarginReportHandler` tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMarginReportHandlerTests" --no-build`

Expected: all tests pass. Tests already construct `ProductMarginSummaryDto` and `CategoryMarginSummaryDto` directly (verified during planning at `GetMarginReportHandlerTests.cs:84` and `:99`), so they exercise the new direct-assignment path immediately.

- [ ] **Step 6: Run `dotnet format` on the modified files**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportResponse.cs backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs
```

Expected: succeeds; may rewrite trailing whitespace or import order. Re-run `dotnet build` after if format changed anything.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportResponse.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs
git commit -m "refactor(analytics): collapse GetMarginReport nested DTOs into Contracts/ types

Delete GetMarginReportResponse.ProductMarginSummary and .CategoryMarginSummary
nested classes; reference ProductMarginSummaryDto and CategoryMarginSummaryDto
from Contracts/ directly. Collapses the verbatim Select(...new...) mapping in
GetMarginReportHandler.BuildSuccessResponse to direct assignments. Wire shape
unchanged."
```

---

## Task 3: Refactor `GetProductMarginAnalysis` (Response + Handler together)

Same pattern as Task 2, smaller surface — one nested class, one `Select(...)` block.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs:93-103`
- Regression net (no edit): `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs`

- [ ] **Step 1: Re-verify field identity**

Run:
```bash
grep -nE "^\s+public\s" backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisResponse.cs
grep -nE "^\s+public\s" backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/MonthlyMarginBreakdownDto.cs
```

Expected: both files declare the same 5 fields with the same types: `Month:DateTime`, `MarginAmount:decimal`, `Revenue:decimal`, `Cost:decimal`, `UnitsSold:int`.

- [ ] **Step 2: Replace `GetProductMarginAnalysisResponse.cs` contents**

Replace the full file contents with:

```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;

public class GetProductMarginAnalysisResponse : BaseResponse
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal TotalMargin { get; set; }
    public decimal MarginPercentage { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public int TotalUnitsSold { get; set; }
    public DateTime AnalysisPeriodStart { get; set; }
    public DateTime AnalysisPeriodEnd { get; set; }
    public List<MonthlyMarginBreakdownDto> MonthlyBreakdown { get; set; } = new();
}
```

What changed:
- Added `using Anela.Heblo.Application.Features.Analytics.Contracts;`.
- `MonthlyBreakdown` element type: `MonthlyMarginBreakdown` → `MonthlyMarginBreakdownDto`.
- Deleted nested class `MonthlyMarginBreakdown` (lines 18–25).

The collection name (`MonthlyBreakdown`, singular) stays as-is. The arch-review flagged it as mildly odd for a `List<>` but explicitly out of scope (renaming would be a wire-shape change).

- [ ] **Step 3: Update `GetProductMarginAnalysisHandler.cs` — collapse the `Select(...)` block**

Use `Edit` with `old_string`:

```csharp
            response.MonthlyBreakdown = (_reportBuilderService
                .BuildMonthlyBreakdown(productData.SalesHistory, productData, request.StartDate, request.EndDate) ?? [])
                .Select(dto => new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown
                {
                    Month = dto.Month,
                    MarginAmount = dto.MarginAmount,
                    Revenue = dto.Revenue,
                    Cost = dto.Cost,
                    UnitsSold = dto.UnitsSold
                })
                .ToList();
```

And `new_string`:

```csharp
            response.MonthlyBreakdown = _reportBuilderService
                .BuildMonthlyBreakdown(productData.SalesHistory, productData, request.StartDate, request.EndDate) ?? new List<MonthlyMarginBreakdownDto>();
```

Notes:
- The `?? []` collection-expression null-guard is preserved as `?? new List<MonthlyMarginBreakdownDto>()` — same semantics, explicit type for an assignment whose target type is `List<MonthlyMarginBreakdownDto>`. (The original `?? []` worked because of the surrounding `.Select(...).ToList()`; without it we need an explicit list expression.)
- `BuildMonthlyBreakdown` is declared as `List<MonthlyMarginBreakdownDto>` (non-null) in `IReportBuilderService`, so the null-guard is defensive. Keep it for behavioural parity with the pre-refactor code.

- [ ] **Step 4: Build the backend**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

Expected: 0 errors.

If you see `CS0246: The type or namespace name 'MonthlyMarginBreakdown' could not be found`, confirm no stale references remain:

Run: `grep -rn "GetProductMarginAnalysisResponse\\.MonthlyMarginBreakdown" backend/`

Expected: zero matches. (Pre-edit there was one, in `GetProductMarginAnalysisHandler.cs:95`.)

- [ ] **Step 5: Run `GetProductMarginAnalysisHandler` tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginAnalysisHandlerTests" --no-build`

Expected: all tests pass. Tests already construct `MonthlyMarginBreakdownDto` directly (verified during planning at `GetProductMarginAnalysisHandlerTests.cs:73,76`).

- [ ] **Step 6: Run `dotnet format` on the modified files**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisResponse.cs backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs
```

Expected: succeeds.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisResponse.cs \
        backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs
git commit -m "refactor(analytics): collapse GetProductMarginAnalysis nested DTO into Contracts/

Delete GetProductMarginAnalysisResponse.MonthlyMarginBreakdown nested class;
reference MonthlyMarginBreakdownDto from Contracts/ directly. Collapses the
verbatim Select(...new...) mapping in GetProductMarginAnalysisHandler to a
direct assignment with the null-guard preserved. Wire shape unchanged."
```

---

## Task 4: Backend full-build + Analytics test gate

Confirms the BE side as a whole is green before touching the frontend.

- [ ] **Step 1: Full solution build**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: 0 errors. Warning count matches baseline from Task 1 Step 2.

- [ ] **Step 2: Run the full Analytics test surface (handlers + service + repository + pipeline)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Analytics" --no-build`

Expected: all Analytics tests pass.

- [ ] **Step 3: Confirm no stale references anywhere in the backend**

Run: `grep -rn "GetMarginReportResponse\\.ProductMarginSummary\\|GetMarginReportResponse\\.CategoryMarginSummary\\|GetProductMarginAnalysisResponse\\.MonthlyMarginBreakdown" backend/`

Expected: zero matches.

- [ ] **Step 4: No commit (verification only)**

If everything passes, proceed to Task 5.

If something fails, stop and investigate before regenerating the frontend client.

---

## Task 5: Regenerate the TypeScript client and verify the rename

The NSwag-driven TS client regenerates as part of building `Anela.Heblo.API` in Debug mode (per `docs/development/api-client-generation.md:381`). Touch that to ensure the generated file picks up the renames, then verify.

**Files:**
- Regenerated (do not hand-edit): `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Build the API project in Debug mode to trigger client regeneration**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -c Debug`

Expected: builds successfully; the prebuild NSwag step rewrites `frontend/src/api/generated/api-client.ts`.

- [ ] **Step 2: Confirm the three renames landed in the generated client**

Run:
```bash
grep -nE "^export class (ProductMarginSummary|CategoryMarginSummary|MonthlyMarginBreakdown)( |Dto) " frontend/src/api/generated/api-client.ts
```

Expected: three matches, each ending with `Dto` (i.e. `ProductMarginSummaryDto`, `CategoryMarginSummaryDto`, `MonthlyMarginBreakdownDto`). The pre-refactor names without `Dto` must be gone.

- [ ] **Step 3: Confirm the JSON property names are unchanged**

Run: `grep -nE "productSummaries|categorySummaries|monthlyBreakdown" frontend/src/api/generated/api-client.ts | head -20`

Expected: the wire property names `productSummaries`, `categorySummaries`, `monthlyBreakdown` are still present (only the TS class names changed, not the JSON keys). This is the wire-equivalence sanity check.

- [ ] **Step 4: Confirm no hand-written FE code referenced the old generated type names**

Run:
```bash
grep -rn "\\b\\(ProductMarginSummary\\|CategoryMarginSummary\\|MonthlyMarginBreakdown\\)\\b" frontend/src --include="*.ts" --include="*.tsx" | grep -v "api/generated/" | grep -v "GetProductMarginSummary" | grep -v "ProductMarginSummary\\(\\.tsx\\|/\\|\\.test\\.tsx\\| from\\| as \\|>:\\|/>\\|<\\)"
```

Expected: zero matches. (The `ProductMarginSummary.tsx` page and its test reference the unrelated `useProductMarginSummary` hook — coincidental name collision, not a consumer of the affected types. The pipe-greps above filter those out.)

If the grep returns anything that isn't `App.tsx`'s page import / route / the page component file / its test, investigate.

- [ ] **Step 5: Frontend build and lint**

Run:
```bash
cd frontend && npm run build && npm run lint
```

Expected: both succeed without source edits. The prebuild script will regenerate the client again (idempotent — same output as Step 1).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(api): regenerate TS client after Analytics DTO consolidation

Three nested-class–derived generated types renamed to their Contracts/ DTO
equivalents: ProductMarginSummary → ProductMarginSummaryDto, CategoryMarginSummary
→ CategoryMarginSummaryDto, MonthlyMarginBreakdown → MonthlyMarginBreakdownDto.
JSON wire shape unchanged."
```

---

## Task 6: Final verification

End-to-end sanity check that the refactor is complete and the gates from the spec pass.

- [ ] **Step 1: Full solution build (post-format check)**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: 0 errors, warning count matches baseline.

- [ ] **Step 2: `dotnet format` no-op check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`

Expected: exit code 0. If non-zero, run `dotnet format backend/Anela.Heblo.sln` and amend the relevant commit.

- [ ] **Step 3: Full Analytics test pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Analytics" --no-build`

Expected: all green.

- [ ] **Step 4: Confirm zero matches for any deleted nested-class reference**

Run:
```bash
grep -rn "GetMarginReportResponse\\.ProductMarginSummary\\|GetMarginReportResponse\\.CategoryMarginSummary\\|GetProductMarginAnalysisResponse\\.MonthlyMarginBreakdown" backend/ frontend/src/
```

Expected: zero matches across both backend and frontend source.

- [ ] **Step 5: Confirm the affected response files are minimal (nested classes gone)**

Run:
```bash
wc -l backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportResponse.cs \
      backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisResponse.cs
```

Expected: `GetMarginReportResponse.cs` ≤ 20 lines (was 54). `GetProductMarginAnalysisResponse.cs` ≤ 20 lines (was 26).

- [ ] **Step 6: Frontend build + lint (final)**

Run:
```bash
cd frontend && npm run build && npm run lint
```

Expected: both succeed.

- [ ] **Step 7: Optional — JSON wire-shape diff (FR-5 evidence)**

If a representative `GetMarginReport` payload is available (from logs, dev environment, or a recorded fixture), compare against the new output. The arch-review classifies this as a single-pass sanity check rather than an automated gate; do it if you have a quick way to capture both responses (`curl` against staging is the canonical method).

If no payload is readily available, skip — the test suite plus the unchanged JSON property names in the generated client (Task 5 Step 3) is the same guarantee.

- [ ] **Step 8: No new commit (verification only)**

The three commits from Tasks 2, 3, and 5 are the deliverables. Done.

---

## Out-of-scope reminders (do not do these even if tempting)

- **Do not add `MarginPercentage` to `MonthlyMarginBreakdownDto`.** Adding a field is a wire-shape change requiring a separate product decision (spec FR-3, arch-review Decision 2).
- **Do not rename `MonthlyBreakdown` to `MonthlyBreakdowns` on the response** — renaming a JSON property is also a wire-shape change (arch-review Decision 3).
- **Do not touch `IReportBuilderService` or `ReportBuilderService`.** They already return the `Contracts/` DTOs; no change is required (arch-review §"Interfaces and Contracts").
- **Do not remove the `Dto` suffix or move the `Contracts/` DTOs.** Layout stays as-is (spec "Out of Scope").
- **Do not extend the refactor to other Analytics duplications.** If you spot more parallel-type pairs, file a follow-up — don't grow the PR (spec "Out of Scope").
