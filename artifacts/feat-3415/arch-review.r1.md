# Architecture Review: Test Coverage – GetPurchaseStockAnalysis Dual-Bucket Invariant and Summary Assertions

## Skip Design: true

## Architectural Fit Assessment

This is a pure test addition with no production code changes. It fits cleanly into the existing test suite: `GetPurchaseStockAnalysisHandlerTests.cs` already owns xUnit + Moq + FluentAssertions, uses a shared `MakeSnapshot` factory, and isolates each test with its own mock setup. The two new tests follow exactly that pattern. No new files, no new types, no cross-module impact.

The handler's dual-bucket design is explicit and stable (lines 42–72 of the handler): `allAnalysisItems` feeds `CalculateSummary`; `analysisItems` feeds paging and display. `IStockSeverityCalculator` is already mocked per-test, which is the correct approach because the calculator's real logic is not under test here — the handler's wiring of the two buckets is.

## Proposed Architecture

### Component Overview

Two new `[Fact]` methods appended to the existing `GetPurchaseStockAnalysisHandlerTests` class. Each method:
1. Builds a fully deterministic `List<MaterialStockSnapshot>` using the existing `MakeSnapshot` factory.
2. Stubs `_materialCatalogMock` to return that list.
3. Stubs `_stockSeverityCalculatorMock` with per-item `SetupSequence` so severity is predictable per snapshot call.
4. Calls `_handler.Handle(request, CancellationToken.None)`.
5. Asserts with FluentAssertions.

No new helper classes, no new factory methods, no changes to production code.

### Key Design Decisions

#### Decision 1: Severity stub strategy — sequence vs. blanket
**Options considered:**
- Blanket `It.IsAny<>()` → `.Returns(severity)`: all items get the same severity. Used by the existing generic tests.
- `SetupSequence`: returns different severities per successive call. Correct for multi-severity snapshots.
- Separate `Setup` calls with specific argument matchers for `availableStock`: fragile, depends on internal calculation path.

**Chosen approach:** `SetupSequence` on `DetermineStockSeverity` ordered to match the snapshot list order, since the handler calls `AnalyzeStockItem` via `snapshots.Select(...)` which preserves order.

**Rationale:** Sequence-based stubbing is the only pattern that works without coupling to internal arithmetic. The `Select` over `snapshots` is deterministic and in insertion order, so the sequence fires in the same order the snapshots are declared. This is already the idiomatic Moq approach for ordered return values with an `IEnumerable.Select`.

#### Decision 2: `TotalInventoryValue` formula source of truth
**Options considered:**
- Derive expected value at runtime inside the test using LINQ over the snapshot list.
- Hard-code a pre-computed `decimal` literal with an inline comment showing the arithmetic.

**Chosen approach:** Hard-coded literal with an inline comment (e.g. `// 10 * 5.00m + 20 * 3.00m + 0 * 0 = 110.00m`).

**Rationale:** The spec requires NFR-2 (readability with explicit arithmetic). A computed expected value would make the test circular — it would never catch a sign error in `CalculateSummary` because the same formula would produce the same wrong answer. A literal forces the developer to reason about the numbers independently of the code path.

#### Decision 3: `EffectiveStock` in `MakeSnapshot` — `available + ordered`
**Chosen approach:** Keep relying on the existing `MakeSnapshot` helper which sets `EffectiveStock = available + ordered`. For FR-2, set `ordered = 0` for all items so that `EffectiveStock == available`, keeping the arithmetic trivial and the comment concise.

**Rationale:** The handler uses `item.Stock.EffectiveStock` (not `Available`) in `TotalInventoryValue`. Using `ordered = 0` makes `effective = available`, eliminating a subtlety that would add noise to the comment without testing anything new.

## Implementation Guidance

### Directory / Module Structure

Single file to modify:
`backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`

Append two methods to the existing `GetPurchaseStockAnalysisHandlerTests` class. No new files.

### Interfaces and Contracts

All types are already in scope via the existing `using` directives at the top of the test file. No new imports needed.

Key types for the two tests:
- `StockSeverity` — enum: `Critical`, `Low`, `Optimal`, `Overstocked`, `NotConfigured`
- `StockStatusFilter` — enum: `Critical` (for FR-1 filter request)
- `StockAnalysisSummaryDto` — fields: `TotalProducts`, `CriticalCount`, `LowStockCount`, `OptimalCount`, `OverstockedCount`, `NotConfiguredCount`, `TotalInventoryValue` (decimal), `AnalysisPeriodStart`, `AnalysisPeriodEnd`
- `LastPurchaseInfoDto.UnitPrice` is `decimal` — the `TotalInventoryValue` formula is `Sum((decimal)EffectiveStock * UnitPrice)` where both sides are decimal after the cast
- `MaterialPurchaseSnapshot.UnitPrice` is `decimal`

### Data Flow

**FR-1 (Dual-Bucket Invariant)**
1. Snapshot list: 2 Critical items + 2 Optimal items (total = 4).
2. `SetupSequence` returns `Critical, Critical, Optimal, Optimal` in that order.
3. Request: `StockStatus = StockStatusFilter.Critical`, `PageNumber = 1`, `PageSize = 10`.
4. Handler builds `allAnalysisItems` (4 items), then `analysisItems` (2 Critical items).
5. `Summary` is computed from `allAnalysisItems`:
   - `TotalProducts = 4`
   - `CriticalCount = 2`
   - `OptimalCount = 2`
6. Assertions:
   - `response.Items` has count 2 and all items have `Severity == StockSeverity.Critical`
   - `response.Summary.TotalProducts == 4`
   - `response.Summary.OptimalCount == 2` (equality, not just > 0)

**FR-2 (Summary Field Value Assertions)**
1. Snapshot list: 1 Critical, 1 Low, 1 Optimal, 1 Overstocked, 1 NotConfigured (total = 5).
2. `SetupSequence` returns exactly those five severities in declaration order.
3. Each item has a controlled `available` and a `LastPurchase.UnitPrice`; `ordered = 0` so `EffectiveStock = available`.
4. Request: no `StockStatus` filter (`StockStatusFilter.All`), explicit pinned UTC `FromDate` and `ToDate` — never `DateTime.UtcNow`.
5. Assertions cover every `StockAnalysisSummaryDto` field with hard-coded expected values plus inline arithmetic comment.
6. `AnalysisPeriodStart` and `AnalysisPeriodEnd` asserted equal to the pinned `fromDate`/`toDate` values.
7. `NotConfigured` item with `lastPurchase = null` contributes `0` to `TotalInventoryValue`; document in comment.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `SetupSequence` call order differs from handler's `Select` order | Medium | Document that sequence order must match snapshot declaration order; the handler uses `snapshots.Select(...)` which preserves list insertion order. |
| `TotalInventoryValue` cast — `EffectiveStock` is `double`, `UnitPrice` is `decimal`; C# does not allow implicit `double * decimal` | High | The handler already casts explicitly: `(decimal)i.EffectiveStock * (i.LastPurchase?.UnitPrice ?? 0)`. Use integer-friendly values to avoid floating-point noise in the hand-computed expected value. |
| Pinned `FromDate`/`ToDate` affect `daysDiff` and `DailyConsumption` | Low | FR-2 does not assert any consumption-derived values. Severity comes from the mock. Safe to ignore. |
| `NotConfigured` item with `lastPurchase = null` contributes `0` to `TotalInventoryValue` | Low | Document this in the inline comment for the expected value. |

## Specification Amendments

**Amendment 1:** FR-1 acceptance criterion says `response.Summary.OptimalCount` is "greater than zero". Strengthen to an equality assertion (`== 2`) for a stricter, self-documenting test. An equality check makes the test fail-fast when the dual-bucket invariant breaks.

**Amendment 2:** The spec does not mention `AnalysisPeriodStart`/`AnalysisPeriodEnd` in FR-1. Correct — those fields are not relevant to the dual-bucket invariant and should not be asserted in FR-1.

**Amendment 3:** For FR-2, always supply explicit non-null `FromDate` and `ToDate` in the request using pinned UTC `DateTime` literals, never `DateTime.UtcNow`, to keep the assertion deterministic.

## Prerequisites

None. All required types, infrastructure, and test utilities already exist in the codebase. The two test methods can be written and run immediately against the current production handler without any migration, configuration, or scaffolding work.
