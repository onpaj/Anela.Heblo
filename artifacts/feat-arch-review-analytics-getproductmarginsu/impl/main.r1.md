All tasks complete. Final reviewer says **APPROVED_WITH_NOTES** — no CRITICAL or HIGH issues; medium findings are either pre-existing or intentional per spec. Now writing the output artifact.

---

# Implementation: Refactor GetProductMarginSummary Handler to Depend on Abstractions

## What was implemented

Pure structural refactor aligning the Analytics module with Clean Architecture and the established interface pattern used by sibling services (`IProductFilterService`, `IReportBuilderService`):

1. **Relocated `MarginCalculator`** from `Anela.Heblo.Domain` to `Anela.Heblo.Application.Features.Analytics.Services`
2. **Added `IMarginCalculator` interface** co-located in the same file (matches sibling convention)
3. **Added `IMonthlyBreakdownGenerator` interface** co-located in `MonthlyBreakdownGenerator.cs`; updated its constructor to accept `IMarginCalculator` instead of the concrete type (required for DI correctness)
4. **Updated DI registrations** in `AnalyticsModule.cs` to use interface-based `AddScoped<IInterface, Impl>()`, removed the misleading "Legacy services" comment
5. **Updated `GetProductMarginSummaryHandler`** constructor and fields to inject `IMarginCalculator` and `IMonthlyBreakdownGenerator`
6. **Added mockability test** demonstrating the handler is now testable in full isolation

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — CREATED: contains `IMarginCalculator` interface + relocated `MarginCalculator` implementation
- `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs` — DELETED
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs` — MODIFIED: added `IMonthlyBreakdownGenerator` interface, updated constructor from `MarginCalculator` to `IMarginCalculator`
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — MODIFIED: interface-based DI registrations, misleading comment removed
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — MODIFIED: field and constructor types changed to interfaces
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — MODIFIED: added `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator` test

## Tests

- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — 6 tests total (5 original + 1 new mockability test using `Mock<IMarginCalculator>` and `Mock<IMonthlyBreakdownGenerator>` with `Times.Once` verification)
- Full backend test suite: 4052 passed, 3 skipped, 0 failed

## How to verify

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-analytics-getproductmarginsu

# Build
dotnet build backend/backend.sln

# Format
dotnet format backend/backend.sln --verify-no-changes

# Handler tests (6)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"

# Full suite
dotnet test backend/backend.sln

# Domain folder (should have exactly 3 files)
ls backend/src/Anela.Heblo.Domain/Features/Analytics/

# Confirm no concrete-type injections remain
grep -rn "MarginCalculator " backend/src backend/test \
  | grep -vE "(IMarginCalculator|MarginCalculator\(|MarginCalculator\.cs|SafeMarginCalculator|IMarginCalculationService|MarginCalculationResult)"
```

## Notes

**Intentional design decisions (per arch-review):**
- Single-file co-location of interface + implementation matches the `ProductFilterService.cs` / `ReportBuilderService.cs` pattern in the same folder — this is consistent, not inconsistent.
- `MarginCalculationResult` stays in Domain (not moved to Application) — Application returning Domain-defined types is allowed under Clean Architecture.

**Pre-existing issues flagged by final reviewer (not introduced by this refactor, candidates for follow-up):**
- `GetGroupKey` is duplicated in `AnalyticsRepository` (independent of this refactor)
- `CalculateTotalMarginForLevel` in handler duplicates `GetMarginAmountForLevel` logic (pre-existing)
- Existing test fixture typed as concrete `MarginCalculator` / `MonthlyBreakdownGenerator` rather than interfaces (now serves as integration-style coverage)

**No other callers found:** Solution-wide search confirmed only the 4 expected sites reference `MarginCalculator` / `MonthlyBreakdownGenerator`. FR-7 satisfied.

## PR Summary

Extracts `IMarginCalculator` and `IMonthlyBreakdownGenerator` interfaces, relocates `MarginCalculator` from the Domain layer to Application, and switches `GetProductMarginSummaryHandler` (and `MonthlyBreakdownGenerator`) to depend on those abstractions. Aligns the two outlier services with the established pattern used by `IProductFilterService` and `IReportBuilderService` in the same `Services/` folder.

The `MonthlyBreakdownGenerator` constructor was updated to accept `IMarginCalculator` (required for correctness — once `MarginCalculator` is registered only by interface, the concrete self-registration is gone and the old constructor would fail runtime DI resolution).

No behavior change: same code paths, same DI lifetimes (Scoped), same HTTP API contract, same DTOs, no DB changes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — new file: `IMarginCalculator` interface + relocated implementation
- `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs` — `IMonthlyBreakdownGenerator` interface added; constructor type changed to `IMarginCalculator`
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — interface-based DI registrations; "Legacy services" comment removed
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — field and constructor types changed to interfaces
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — mockability test added (6 tests total, all pass)

## Status
DONE