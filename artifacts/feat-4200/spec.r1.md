# Specification: Move stock-analysis business logic out of GetPurchaseStockAnalysisHandler

## Summary
`GetPurchaseStockAnalysisHandler` currently mixes data retrieval/orchestration with four private business-logic methods (per-item analysis, status filtering, sorting, summary calculation) that belong in `IStockAnalysisCalculator`. This change moves that logic into the calculator service so the handler becomes a thin coordinator, without altering any externally observable behavior (API contract, response shape, or computed values).

## Background
`IStockAnalysisCalculator` already owns two pure-math helpers (`CalculateStockEfficiency`, `CalculateRecommendedOrderQuantity`) used by the handler. The handler additionally implements `AnalyzeStockItem`, `ShouldIncludeItem`, `SortItems`, and `CalculateSummary` as private methods (lines 94–214 of `GetPurchaseStockAnalysisHandler.cs`), giving the handler seven responsibilities: data retrieval, per-item analysis, status filtering, text search, sorting, pagination, and summary calculation. This violates SRP, makes the analysis rules (severity thresholds, filter conditions, sort order, summary math) untestable except through the full MediatR handler, and increases the cost of extending filter/sort behavior. This is a pure internal refactor filed by the arch-review routine — no behavior change, no new user-facing capability.

## Functional Requirements

### FR-1: Extend `IStockAnalysisCalculator` with an `Analyze` method covering per-item analysis
Add a method to `IStockAnalysisCalculator` (and implement it in `StockAnalysisCalculator`) that performs the work currently in the handler's private `AnalyzeStockItem` — given a `MaterialStockSnapshot`, `fromDate`, and `toDate`, compute `dailyConsumption`, `daysUntilStockout`, `optimalStock`, delegate to the existing `CalculateStockEfficiency` / `CalculateRecommendedOrderQuantity` methods, call `IStockSeverityCalculator.DetermineStockSeverity`, and map the result to a `StockAnalysisItemDto` (including `GetLastPurchaseInfo` mapping).

**Acceptance criteria:**
- The new calculator method produces a `StockAnalysisItemDto` byte-for-byte equivalent (all fields) to what the current handler's `AnalyzeStockItem` produces, for the same inputs.
- `IStockSeverityCalculator` remains a handler-injected dependency, passed into the calculator method (as a parameter) or the calculator continues to receive it via constructor injection — either is acceptable as long as the handler still owns wiring `IStockSeverityCalculator` (see NFR-1 on dependency boundaries below; the analyst does not mandate which, leaving it to the architect/designer).
- Existing unit tests that assert on individual `StockAnalysisItemDto` fields (`DailyConsumption`, `DaysUntilStockout`, `OptimalStockLevel`, `StockEfficiencyPercentage`, `Severity`, `RecommendedOrderQuantity`, `LastPurchase`, `IsConfigured`) continue to pass unmodified in behavior (test call sites may need to move, per FR-5).

### FR-2: Move item-inclusion filtering (`ShouldIncludeItem`) into the calculator service
Move the `OnlyConfigured` and `StockStatus` filter logic into `IStockAnalysisCalculator` (or a method it exposes), operating on an already-built `StockAnalysisItemDto` and the request's filter fields.

**Acceptance criteria:**
- Filtering behavior for every `StockStatusFilter` enum value (`Critical`, `Low`, `Optimal`, `Overstocked`, `NotConfigured`, and the default/`All` case) is unchanged.
- `OnlyConfigured = true` continues to exclude items where `IsConfigured == false`, combined with the status filter exactly as today (both conditions must pass).

### FR-3: Move sorting (`SortItems`) into the calculator service
Move the `StockAnalysisSortBy` → LINQ-ordering switch and the `descending` reversal into `IStockAnalysisCalculator`.

**Acceptance criteria:**
- All six sort paths (`ProductCode`, `ProductName`, `AvailableStock`, `Consumption`, `StockEfficiency`, `LastPurchaseDate`, and the default fallback to `StockEfficiency`) produce the same ordering as today, including the `descending` reversal behavior (list-order reversal, not a change to the comparer).

### FR-4: Move summary calculation (`CalculateSummary`) into the calculator service
Move the severity-count and `TotalInventoryValue` aggregation into `IStockAnalysisCalculator`.

**Acceptance criteria:**
- `StockAnalysisSummaryDto` output (all six counts, `TotalInventoryValue`, `AnalysisPeriodStart`, `AnalysisPeriodEnd`) is computed from the **unfiltered** `allAnalysisItems` collection, exactly as today (summary must not be affected by `ShouldIncludeItem` filtering or the search-term filter — this ordering constraint must be preserved by whichever new method/overload composes analysis, filtering, and summary).

### FR-5: Handler becomes a thin coordinator
After the move, `GetPurchaseStockAnalysisHandler.Handle` retains only: date-range defaulting/validation, calling `IMaterialCatalogService.GetStockAnalysisSnapshotsAsync`, calling into `IStockAnalysisCalculator` for analysis/filter/sort/summary, the free-text search-term filter (see Open Questions — scope decision needed here), and pagination/response assembly.

**Acceptance criteria:**
- `GetPurchaseStockAnalysisHandler.cs` no longer contains `AnalyzeStockItem`, `ShouldIncludeItem`, `SortItems`, or `CalculateSummary` as private methods.
- The handler's `Handle` method still produces `GetPurchaseStockAnalysisResponse` with identical `Items`, `TotalCount`, `PageNumber`, `PageSize`, and `Summary` values as before, for identical inputs (verified via existing test suite, see FR-6).
- `MaterialCategoryResolver.Matches` category filtering (line 50 of the current handler) stays in the handler unless the architect/designer decide otherwise — it is not named in the issue's list of methods to move.

### FR-6: Test suite continues to pass, relocated as appropriate
`GetPurchaseStockAnalysisHandlerTests.cs` (585 lines) and `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` (104 lines) currently exercise the moved logic indirectly through the handler.

**Acceptance criteria:**
- All existing tests in both files continue to pass without modification to their assertions or expected values (call-site/setup changes are allowed if the architect/designer choose to relocate coverage).
- New focused unit tests are added directly against `StockAnalysisCalculator` for the newly-added analysis/filter/sort/summary logic, per the designer's test plan — enabling the fast, isolated testing called out as the motivation in the issue ("Testability" in Why it matters).

## Non-Functional Requirements

### NFR-1: No behavioral change (pure refactor)
This is explicitly framed by the issue as "the minimal change: no new abstractions, just moving existing private methods." No change to computed values, filter semantics, sort semantics, summary semantics, API request/response DTO shapes, or the `GetPurchaseStockAnalysisRequest`/`Response` public contract is in scope.

### NFR-2: Module boundary / dependency direction preserved
`IStockAnalysisCalculator` lives in `Anela.Heblo.Application/Features/Purchase/Services/`, same module as the handler (`Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/`) — this is an intra-module move, not a cross-module boundary change. No new project references are required. Per `docs/architecture/development_guidelines.md`, DTOs referenced across the moved boundary (`StockAnalysisItemDto`, `StockAnalysisSummaryDto`, `GetPurchaseStockAnalysisRequest`) must remain classes, not records — they already are and this must not change.

### NFR-3: No performance regression
The moved logic is pure in-memory LINQ/computation over an already-fetched snapshot collection; moving it to another class in the same process must not change algorithmic complexity or introduce additional I/O, allocations at a different order of magnitude, or additional enumeration passes beyond what exists today.

## Data Model
No data model changes. Existing types remain as-is:
- `MaterialStockSnapshot` (input, from `IMaterialCatalogService`)
- `StockAnalysisItemDto`, `StockAnalysisSummaryDto`, `LastPurchaseInfoDto` (output DTOs, unchanged shape)
- `GetPurchaseStockAnalysisRequest` (`FromDate`, `ToDate`, `MaterialCategory`, `StockStatus`, `OnlyConfigured`, `SearchTerm`, `SortBy`, `SortDescending`, `PageNumber`, `PageSize`, `IsExport`)
- `StockStatusFilter`, `StockAnalysisSortBy`, `StockSeverity` enums (unchanged)

## API / Interface Design
No HTTP-facing API change. The internal interface changes are:
- `IStockAnalysisCalculator` gains one or more new method(s)/overload(s) covering: per-item analysis (`AnalyzeStockItem` equivalent), item filtering (`ShouldIncludeItem` equivalent), sorting (`SortItems` equivalent), and summary calculation (`CalculateSummary` equivalent). The issue suggests a single composed `Analyze(IEnumerable<MaterialStockSnapshot>, GetPurchaseStockAnalysisRequest)` method as one option, but also allows separate methods/overloads — this is left to the architect and designer to decide, since it affects how the FR-4 ordering constraint (summary computed from unfiltered items) is expressed and how testable each piece is in isolation.
- `GetLastPurchaseInfo` (currently a private handler helper, lines 147–164) is a mapping helper used only by `AnalyzeStockItem`; it should move together with `AnalyzeStockItem` into the calculator, since it has no reason to stay behind once its only caller moves.

## Dependencies
- `IStockSeverityCalculator.DetermineStockSeverity` — currently injected into the handler and called from `AnalyzeStockItem`. Once `AnalyzeStockItem` moves, this dependency's wiring must move with it (either inject `IStockSeverityCalculator` into `StockAnalysisCalculator`, or keep it handler-owned and pass it into the calculator call — architect/designer decision).
- `MaterialCategoryResolver.Matches` — stays in the handler per FR-5, not a dependency of the moved code.
- `NormalizeForSearch()` extension (used by the search-term filter) — stays in the handler per the Open Questions scope decision below, unless the designer decides otherwise.
- No new external libraries or services.

## Out of Scope
- Any change to the free-text search-term filtering logic (lines 59–69 of the current handler) — the issue's table of methods to move does not include this block; it is left in the handler. (Flagged as an open question below since the issue's summary of handler responsibilities lists "text search" among the seven SRP-violating concerns, but the "Suggested fix" code sample does not show it moving.)
- Any change to pagination logic (`Skip`/`Take`, `IsExport` handling).
- Any change to `MaterialCategoryResolver` or `IMaterialCatalogService`.
- Any new caching, async batching, or performance optimization beyond preserving current behavior.
- Any change to the two existing `IStockAnalysisCalculator` methods (`CalculateStockEfficiency`, `CalculateRecommendedOrderQuantity`) beyond them now being called from within the new `Analyze`-family method(s) instead of directly from the handler.
- Any change to controller/API layer, frontend, or OpenAPI contract.

## Open Questions

None.

**Assumptions made** (each is a reasonable default per the issue's own scope, not left open for architect/designer sign-off, but noted for traceability):
- The free-text search-term filter (lines 59–69) stays in the handler, since it is not in the issue's table of four methods to move and the issue's own "Suggested fix" code sample keeps only `Analyze(...)` + pagination in the handler-level snippet, implying search-term filtering is either folded into that call or intentionally left out of scope; the analyst treats it as out of scope (see Out of Scope) and leaves the exact composition (call it before or after `Analyze`) to the architect/designer, since it does not change behavior either way as long as ordering relative to summary calculation (FR-4) is preserved.
- `GetLastPurchaseInfo` moves together with `AnalyzeStockItem` since it is a private helper with no other caller.
- The exact shape of the new `IStockAnalysisCalculator` method(s) (one composed `Analyze` vs. four separate methods) is an architecture/design decision, not a product decision — the acceptance criteria above are written to hold under either shape.

## Status: COMPLETE
