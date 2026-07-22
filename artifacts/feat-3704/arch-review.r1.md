# Architecture Review: Extract IStockAnalysisCalculator from GetPurchaseStockAnalysisHandler

## Skip Design: true

## Architectural Fit Assessment
This is a textbook Extract Service refactor with a live template already in the same folder. `IStockSeverityCalculator`/`StockSeverityCalculator` (`backend/src/Anela.Heblo.Application/Features/Purchase/Services/`) is a stateless, constructor-injected, `Scoped`-registered service that encapsulates one pure calculation and is called from `GetPurchaseStockAnalysisHandler.AnalyzeStockItem`. `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity` are structurally identical candidates: pure functions of primitives, called from the same method, with no I/O and no cross-module concerns.

This aligns with `docs/architecture/development_guidelines.md`: "Features/{Feature}/Services/: Domain services, background services" is the documented home for exactly this kind of logic (confirmed in `docs/architecture/filesystem.md:153`), and `PurchaseModule.AddPurchaseModule()` is the sole legitimate place for the DI binding (module owns its own wiring, per ADR-004's binding-locality rule extended here to a Application-layer service, not a repository — but the same "one file per slice" principle applies).

No module boundaries are crossed, no contracts change, no persistence is touched. Risk is low and almost entirely contained to test-wiring correctness (see Risks).

## Proposed Architecture

### Component Overview
```
GetPurchaseStockAnalysisHandler
        │
        ├── IMaterialCatalogService        (existing, unchanged)
        ├── IStockSeverityCalculator ──────► StockSeverityCalculator      (existing, unchanged)
        ├── IStockAnalysisCalculator ──────► StockAnalysisCalculator      (NEW)
        │        │
        │        ├── CalculateStockEfficiency(availableStock, minStock, optimalStock) -> double
        │        └── CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq) -> double?
        │
        └── ILogger<GetPurchaseStockAnalysisHandler> (existing, unchanged)
```
`StockAnalysisCalculator` sits as a sibling to `StockSeverityCalculator` in `Features/Purchase/Services/`, registered in the same `PurchaseModule`, with the same `Scoped` lifetime (lifetime choice is irrelevant for a stateless class but consistency removes a reason to ask "why is this one different").

### Key Design Decisions

#### Decision 1: One combined interface vs. two separate calculators
**Options considered:**
- (a) Single `IStockAnalysisCalculator` with both methods (as specified in brief/spec).
- (b) Two separate interfaces, one per method, mirroring strict SRP-per-method.

**Chosen approach:** (a), exactly as specified.

**Rationale:** `IStockSeverityCalculator` already establishes the precedent of one interface per *cohesive concern* (stock analysis math), not one interface per method. `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity` are both inputs to the same `AnalyzeStockItem` step, operate on the same shape of data (available/min/optimal stock), and were already grouped together in the finding. Splitting further would add ceremony (two DI registrations, two constructor params, two test files) without a corresponding testability or reuse benefit. This matches the brief and spec exactly — no deviation warranted.

#### Decision 2: Stateless class, no constructor dependencies
**Options considered:**
- (a) Plain class with no injected dependencies (matches `StockSeverityCalculator`).
- (b) Inject `ILogger` for diagnostic visibility into edge cases (e.g., unparseable MOQ).

**Chosen approach:** (a).

**Rationale:** `StockSeverityCalculator` takes no dependencies and the two methods being extracted have no existing logging today (the current private methods in the handler don't log either). Adding a logger here would be scope creep beyond "move the code" and would break the "no behavioral edits" constraint the spec explicitly calls out (FR-2, NFR-1). If unparseable-MOQ visibility is ever needed, that's a separate, deliberate feature.

#### Decision 3: DI registration placement and lifetime
**Options considered:**
- (a) `Scoped`, registered adjacent to `IStockSeverityCalculator` in `PurchaseModule`.
- (b) `Singleton`, since the class is stateless and thread-safe.

**Chosen approach:** (a).

**Rationale:** Consistency with the existing sibling service outweighs the marginal performance benefit of `Singleton` for a class this cheap to instantiate. Introducing a second lifetime convention for what is conceptually "the same kind of thing as `IStockSeverityCalculator`" would itself become a future arch-review finding. If `PurchaseModule` is ever audited for lifetime correctness, both should be revisited together, not one now and one later.

## Implementation Guidance

### Directory / Module Structure
No new directories. Two new files in the existing `Services/` folder, one new test file in the existing test folder:

```
backend/src/Anela.Heblo.Application/Features/Purchase/Services/
├── IStockSeverityCalculator.cs         (existing, unchanged)
├── StockSeverityCalculator.cs          (existing, unchanged)
├── IStockAnalysisCalculator.cs         (NEW)
└── StockAnalysisCalculator.cs          (NEW)

backend/test/Anela.Heblo.Tests/Features/Purchase/
├── StockSeverityCalculatorTests.cs               (existing, unchanged)
├── StockAnalysisCalculatorTests.cs               (NEW)
├── GetPurchaseStockAnalysisHandlerTests.cs        (MODIFIED — constructor wiring only)
└── GetPurchaseStockAnalysisHandlerDiacriticsTests.cs (MODIFIED — constructor wiring only)
```

### Interfaces and Contracts
Exactly as specified in `spec.r1.md` FR-1/FR-2 — no deviation:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Services;

public interface IStockAnalysisCalculator
{
    double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock);
    double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq);
}
```

Implementation is a byte-for-byte move of the two current private methods (`GetPurchaseStockAnalysisHandler.cs:137` and `:166`) into `StockAnalysisCalculator`, following `StockSeverityCalculator.cs`'s XML-doc and no-dependency style.

`GetPurchaseStockAnalysisHandler`'s constructor gains one parameter, placed after `stockSeverityCalculator` and before `logger` (matching the spec's stated ordering and the existing field ordering in the class: `_materialCatalog`, `_stockSeverityCalculator`, then the new `_stockAnalysisCalculator`, then `_logger`).

`PurchaseModule.cs:25` gets one new line immediately after the existing `IStockSeverityCalculator` registration:
```csharp
services.AddScoped<IStockAnalysisCalculator, StockAnalysisCalculator>();
```

### Data Flow
Unchanged end-to-end. `AnalyzeStockItem` still computes `optimalStock` and `minStock` itself, then delegates:
- Line 102 (current): `CalculateStockEfficiency(...)` → becomes `_stockAnalysisCalculator.CalculateStockEfficiency(...)`
- Line 107 (current): `CalculateRecommendedOrderQuantity(...)` → becomes `_stockAnalysisCalculator.CalculateRecommendedOrderQuantity(...)`

No new data crosses a module or process boundary; this is purely an in-process call-site redirection.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing handler tests silently weakened by swapping `IStockAnalysisCalculator` for a bare `Mock<IStockAnalysisCalculator>()` without `.Setup()` calls | Medium | `Mock<T>` without setup returns `default` for every call — `0.0` for `CalculateStockEfficiency`, `null` for `CalculateRecommendedOrderQuantity`. `GetPurchaseStockAnalysisHandlerTests.cs:219` (`Handle_SortByStockEfficiency_ReturnsSortedItems`) currently sorts on **real** computed `StockEfficiencyPercentage` values; with an un-stubbed mock every item would collapse to `0`, and the assertion (`OrderByDescending` of an all-zero list) would pass trivially without exercising the real sort behavior. Do **not** mirror the `IStockSeverityCalculator` mocking style verbatim for this dependency. Construct both test classes' `_handler` with a **real** `new StockAnalysisCalculator()` instance (no mock), since the calculator is stateless and has no side effects to isolate — this preserves the exact pre-refactor behavior the spec requires (NFR-1, FR-6) and keeps the sort test meaningful. `IStockSeverityCalculator` stays mocked as-is; it is deliberately stubbed per-test to control severity-based filtering, which is a different testing need than the analysis-calculator's pure arithmetic. |
| Two calculators (`IStockSeverityCalculator`, `IStockAnalysisCalculator`) both computing from the same `availableStock`/`minStock`/`optimalStock` triple invite a future "why not merge them" refactor that would re-couple severity and efficiency logic | Low | Out of scope here — flag as a non-blocking observation only. Keep them separate per the brief; do not preemptively merge. |
| DI registration ordering/comment drift in `PurchaseModule.cs` | Low | Spec FR-4 already gives implementer discretion (single combined comment vs. two adjacent ones). No further guidance needed. |

## Specification Amendments
One addition to FR-6, which the spec left ambiguous ("match whichever style those tests already use for `IStockSeverityCalculator`"):

- **FR-6 clarification:** `IStockSeverityCalculator` is mocked with per-test `.Setup()` calls in both existing test files — that pattern is appropriate for severity because each test wants a controlled severity outcome, independent of the real threshold math. `IStockAnalysisCalculator` must **not** follow the same mocking pattern. Wire both `GetPurchaseStockAnalysisHandlerTests` and `GetPurchaseStockAnalysisHandlerDiacriticsTests` constructors with a real `new StockAnalysisCalculator()` instance (no `Mock<IStockAnalysisCalculator>`, no `.Object`). This is required to keep `Handle_SortByStockEfficiency_ReturnsSortedItems` (`GetPurchaseStockAnalysisHandlerTests.cs:200-221`) actually validating sort-by-real-value behavior rather than trivially passing on an all-default list, and it is the more literal reading of FR-6's "no test assertions are weakened" acceptance criterion.

No other amendments — the spec's file paths, signatures, and body text are otherwise implementation-ready as written.

## Prerequisites
None. No migrations, no config, no new packages. `Moq` and `FluentAssertions` are already in use in the target test project. Implementation can start immediately.
