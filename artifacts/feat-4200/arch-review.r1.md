# Architecture Review: Move stock-analysis business logic out of GetPurchaseStockAnalysisHandler

## Skip Design: true

Pure backend refactor. No new or changed UI components, screens, or visual design decisions — `GetPurchaseStockAnalysisResponse`, its DTOs, and the frontend contract are all explicitly unchanged (NFR-1). The designer phase should focus on the internal service/test design, not any UI/UX artifact.

## Architectural Fit Assessment

This fits an established convention already present in this codebase: `Anela.Heblo.Application/Features/Manufacture/Services/ItemFilterService.cs` (`IItemFilterService`) is the closest analog to what this issue asks for — it is a dedicated Application-layer service, in the same module as its consuming handler, exposing `FilterItems(List<T>, TRequest)`, `SortItems(List<T>, TSortBy, bool)`, and `CalculateSummary(List<T>, DateTime, DateTime, ...)` as three separate public methods, each independently unit-testable. `GetManufacturingStockAnalysisHandler` (Manufacture module) calls these three methods directly instead of implementing the logic inline.

`Anela.Heblo.Application/Features/Purchase/Services/` already has this exact shape for two of four concerns: `IStockAnalysisCalculator` (pure math: efficiency %, recommended order qty) and `IStockSeverityCalculator` (severity classification), both registered via `PurchaseModule.AddScoped<T, TImpl>()`. The issue asks to extend `IStockAnalysisCalculator` specifically — the Purchase module does not yet have a `ShouldIncludeItem`/`SortItems`/`CalculateSummary` service the way Manufacture does. Growing `IStockAnalysisCalculator` to also own per-item analysis, filtering, sorting, and summary calculation is architecturally sound and brings Purchase's structure in line with Manufacture's.

No cross-module boundary is crossed: both today's private methods and the target `IStockAnalysisCalculator` live under `Anela.Heblo.Application/Features/Purchase/`. This is an intra-module extraction, confirmed by reading `PurchaseModule.cs`.

## Proposed Architecture

### Component Overview

```
Before:
  GetPurchaseStockAnalysisHandler
    ├─ Handle()                    [orchestration + pagination]
    ├─ AnalyzeStockItem()          [private, uses _stockAnalysisCalculator + _stockSeverityCalculator]
    ├─ GetLastPurchaseInfo()       [private, mapping helper for AnalyzeStockItem]
    ├─ ShouldIncludeItem()         [private]
    ├─ SortItems()                 [private]
    └─ CalculateSummary()          [private]
  IStockAnalysisCalculator (2 pure-math methods, called by AnalyzeStockItem)
  IStockSeverityCalculator (injected into handler, called by AnalyzeStockItem)

After:
  GetPurchaseStockAnalysisHandler
    └─ Handle()   [date validation, fetch snapshots, category filter, delegate to
                   IStockAnalysisCalculator for analyze/filter/sort/summary,
                   search-term filter, paginate, assemble response]

  IStockAnalysisCalculator                       (grown; same interface, same DI registration)
    ├─ CalculateStockEfficiency()                 [unchanged]
    ├─ CalculateRecommendedOrderQuantity()         [unchanged]
    ├─ AnalyzeItem(MaterialStockSnapshot, from, to) -> StockAnalysisItemDto   [NEW — moved]
    ├─ FilterItems(List<StockAnalysisItemDto>, GetPurchaseStockAnalysisRequest)
    │      -> List<StockAnalysisItemDto>          [NEW — moved, wraps ShouldIncludeItem]
    ├─ SortItems(List<StockAnalysisItemDto>, StockAnalysisSortBy, bool)
    │      -> List<StockAnalysisItemDto>          [NEW — moved]
    └─ CalculateSummary(List<StockAnalysisItemDto>, from, to)
           -> StockAnalysisSummaryDto             [NEW — moved]

  StockAnalysisCalculator : IStockAnalysisCalculator
    (gains IStockSeverityCalculator as a constructor dependency, since AnalyzeItem needs it)
```

### Key Design Decisions

#### Decision 1: Separate public methods on `IStockAnalysisCalculator`, not one composed `Analyze(...)` call

**Options considered:**
- (a) One composed `Analyze(IEnumerable<MaterialStockSnapshot>, GetPurchaseStockAnalysisRequest)` method that internally does analysis + filter + sort + summary and returns everything the handler needs (the shape sketched in the issue's own "Suggested fix" code sample).
- (b) Four separate public methods (`AnalyzeItem`, `FilterItems`, `SortItems`, `CalculateSummary`) mirroring `ItemFilterService`'s existing three-method shape in the Manufacture module.

**Chosen approach:** (b), four separate methods.

**Rationale:** `ItemFilterService` is the established, working precedent for exactly this problem in this codebase (same layer, same kind of handler, same pipeline shape: analyze → filter → sort → summarize). Matching it keeps Purchase and Manufacture consistent for the next developer who reads either. It also directly serves the issue's own stated motivation — "Testability: … enables focused, fast unit tests" — a composed `Analyze()` would still require assembling a full snapshot list and request object to test summary math in isolation; four separate methods let a test call `CalculateSummary(items, from, to)` with a hand-built `List<StockAnalysisItemDto>` directly, with no dependency on `AnalyzeItem` or filtering at all. Four separate methods are also individually simpler to keep byte-for-byte equivalent to today's four private methods (Decision constraint in spec FR-1..FR-4) than reasoning about one larger method's internal composition.

#### Decision 2: `IStockSeverityCalculator` moves from handler-injected to `StockAnalysisCalculator`-injected

**Options considered:**
- (a) Keep `IStockSeverityCalculator` injected into the handler; pass it as a parameter into the new `AnalyzeItem(snapshot, from, to, severityCalculator)` method.
- (b) Inject `IStockSeverityCalculator` into `StockAnalysisCalculator`'s constructor; the handler stops depending on it entirely.

**Chosen approach:** (b).

**Rationale:** `AnalyzeItem` is the only caller of `DetermineStockSeverity` (confirmed: `GetPurchaseStockAnalysisHandlerTests.cs`/`...DiacriticsTests.cs` exercise severity only indirectly through the handler; `IStockSeverityCalculator` has no other caller in the handler). Once `AnalyzeStockItem` moves, threading `IStockSeverityCalculator` through every `AnalyzeItem` call as a parameter is unnecessary ceremony — standard constructor injection is simpler, is exactly how `IStockAnalysisCalculator` and `IStockSeverityCalculator` are already wired (`services.AddScoped<T, TImpl>()` in `PurchaseModule.cs`, both scoped so lifetime compatibility is not a concern), and shrinks the handler's constructor from 5 dependencies to 4, reinforcing the "thin coordinator" goal in spec FR-5.

#### Decision 3: `GetLastPurchaseInfo` moves with `AnalyzeItem` as a private helper on `StockAnalysisCalculator`

**Options considered:**
- (a) Leave `GetLastPurchaseInfo` in the handler and have it call back in some way.
- (b) Move it as a private method on `StockAnalysisCalculator`, since `AnalyzeItem` is its only caller.

**Chosen approach:** (b) — already identified as the right call in the spec; confirmed by reading the handler: `GetLastPurchaseInfo` (lines 147–164) has exactly one call site, inside `AnalyzeStockItem`. No reason for it to stay behind.

#### Decision 4: Free-text search-term filter (lines 59–69) stays in the handler

**Options considered:**
- (a) Move search-term filtering into `IStockAnalysisCalculator.FilterItems` too (folding it into the same method as the status/`OnlyConfigured` filter), matching how `ItemFilterService.FilterItems` in Manufacture combines both concerns in one method.
- (b) Leave it in the handler, as scoped by the spec (Out of Scope) and the issue's own method table (which does not list it).

**Chosen approach:** (b) — leave it in the handler.

**Rationale:** The issue is explicit that this is "the minimal change: no new abstractions, just moving existing private methods" and lists exactly four methods by name and line range; search-term filtering is not one of them. Even though `ItemFilterService` demonstrates a codebase precedent for combining both filters in one service method, expanding scope to match that precedent is a separate improvement the issue did not ask for and the spec correctly fenced off. Flagging as a **natural follow-up** (not blocking this change): once this refactor lands, a future issue could fold search-term filtering into `IStockAnalysisCalculator.FilterItems` to fully match the `ItemFilterService` shape — but doing it now would inflate this PR's diff beyond what the arch-review issue asked for.

## Implementation Guidance

### Directory / Module Structure

No new files. Two existing files change:
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — add four method signatures.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — add four method implementations (moved verbatim from the handler, adjusted for `this`/field access), add `IStockSeverityCalculator` constructor dependency, add `GetLastPurchaseInfo` as a new private method.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — remove the four private methods, `GetLastPurchaseInfo`, and the `IStockSeverityCalculator` field/constructor parameter; replace direct calls with calls into `_stockAnalysisCalculator`.

No `PurchaseModule.cs` change needed — `IStockAnalysisCalculator` is already registered.

### Interfaces and Contracts

`IStockAnalysisCalculator` (new shape):

```csharp
public interface IStockAnalysisCalculator
{
    double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock);
    double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq);

    // NEW
    StockAnalysisItemDto AnalyzeItem(MaterialStockSnapshot item, DateTime fromDate, DateTime toDate);
    List<StockAnalysisItemDto> FilterItems(List<StockAnalysisItemDto> items, GetPurchaseStockAnalysisRequest request);
    List<StockAnalysisItemDto> SortItems(List<StockAnalysisItemDto> items, StockAnalysisSortBy sortBy, bool descending);
    StockAnalysisSummaryDto CalculateSummary(List<StockAnalysisItemDto> items, DateTime fromDate, DateTime toDate);
}
```

`StockAnalysisCalculator` constructor gains `IStockSeverityCalculator stockSeverityCalculator` (stored as a field, used only inside `AnalyzeItem`).

Method bodies are moved as-is from the handler (same logic, same field references rebased onto the calculator's own fields) — this review does not propose any algorithmic change, per spec NFR-1.

### Data Flow

`Handle()` after the change:

```csharp
var snapshots = await _materialCatalog.GetStockAnalysisSnapshotsAsync(fromDate, toDate, cancellationToken);

var allAnalysisItems = snapshots
    .Where(s => MaterialCategoryResolver.Matches(s.ProductCode, request.MaterialCategory))
    .Select(s => _stockAnalysisCalculator.AnalyzeItem(s, fromDate, toDate))
    .ToList();

var analysisItems = _stockAnalysisCalculator.FilterItems(allAnalysisItems, request);

if (!string.IsNullOrWhiteSpace(request.SearchTerm))
{
    // unchanged: search-term filter stays inline in the handler (Decision 4)
}

analysisItems = _stockAnalysisCalculator.SortItems(analysisItems, request.SortBy, request.SortDescending);

var totalCount = analysisItems.Count;
var pagedItems = /* unchanged pagination */;

var summary = _stockAnalysisCalculator.CalculateSummary(allAnalysisItems, fromDate, toDate);
```

This preserves the critical, easy-to-miss invariant already present today and called out in spec FR-4: **`CalculateSummary` is computed from `allAnalysisItems` (post category-filter, pre status/search/sort filter), not from the paginated or filtered `analysisItems`.** The developer must not accidentally pass `analysisItems` (filtered) into `CalculateSummary` when performing the move — this is the single highest-risk regression in this refactor (see Risks table).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Summary accidentally computed from filtered `analysisItems` instead of unfiltered `allAnalysisItems`, silently changing `Summary` counts/`TotalInventoryValue` in the API response | High | Keep the exact `allAnalysisItems` vs `analysisItems` variable split in the handler as shown in Data Flow above; add/keep a test asserting `Summary.TotalProducts` differs from `Items.Count`/`TotalCount` when a status filter narrows the result set (this scenario is the one that would catch the regression) |
| `IStockSeverityCalculator` moving from handler DI to `StockAnalysisCalculator` DI breaks a test that mocks the handler's constructor directly | Medium | `GetPurchaseStockAnalysisHandlerTests.cs`/`...DiacriticsTests.cs` construct the handler with explicit mocks per dependency (confirmed by reading both files' line counts and the handler's 5-parameter constructor); developer must update the handler's test setup to drop the `IStockSeverityCalculator` mock from the handler constructor and add it to wherever `StockAnalysisCalculator` (or its mock) is constructed/stubbed in those tests |
| Floating-point/order-of-operations drift when moving arithmetic between classes (e.g. `dailyConsumption`, `optimalStock` computed inline in `AnalyzeStockItem`) | Low | Move method bodies verbatim, no reformatting of the arithmetic expressions themselves; run the full existing test suite (585 + 104 lines of assertions) unmodified against the new code path |
| `StockAnalysisCalculator` becoming a "God service" for Purchase stock analysis, re-creating the SRP problem one layer up | Low | Out of scope for this issue — the issue explicitly asks for exactly this consolidation ("Move … into `IStockAnalysisCalculator` … possibly as a new overload or a dedicated method"); if `StockAnalysisCalculator` grows further after this, a future arch-review can reassess, consistent with how `ItemFilterService` already carries an equivalent three-concern load in Manufacture without issue |

## Specification Amendments

- **FR-1 acceptance criteria, dependency wiring bullet:** the spec left `IStockSeverityCalculator` wiring open ("either is acceptable... left to the architect/designer"). This review resolves it: `IStockSeverityCalculator` is injected into `StockAnalysisCalculator`'s constructor (Decision 2); the handler no longer takes `IStockSeverityCalculator` as a constructor parameter at all.
- **API / Interface Design section of the spec:** resolves the "single composed `Analyze` vs. separate methods" open choice — four separate methods (Decision 1), named `AnalyzeItem`, `FilterItems`, `SortItems`, `CalculateSummary`, matching `IItemFilterService`'s naming convention in the Manufacture module as closely as the existing `IStockAnalysisCalculator` method-naming style (`CalculateX`, `Calculate...`) allows.
- **Out of Scope section:** confirms search-term filtering stays in the handler (Decision 4) — no amendment to spec scope, just confirmation.

## Prerequisites

None. No migrations, config, or infrastructure changes — this is a same-module, same-assembly code move with no new package references, no `PurchaseModule.cs` DI changes (the interface being extended is already registered), and no schema or API contract changes.
