# Specification: Extract margin aggregation and sorting logic from GetProductMarginSummaryHandler

## Summary
`GetProductMarginSummaryHandler` currently embeds two pieces of business logic — weighted-average margin aggregation (`CalculateGroupMarginData`) and product-list sorting (`ApplySorting`) — as private methods. This refactor extracts both into dedicated, independently testable services (`IMarginCalculator.GetGroupAggregatedMarginData` and a new `ITopProductSorter`), following the module's existing pattern of extracting each concern into a named service (as already done for `IMarginCalculator` and `IMonthlyBreakdownGenerator`). No behavior, API contract, or response shape changes.

## Background
An automated architecture-review pass flagged `GetProductMarginSummaryHandler.cs` (242 lines) for holding two substantial private methods that don't belong in a MediatR handler:

- `CalculateGroupMarginData` (lines 122–158): computes weighted-average margin amounts/percentages across a group of products, with a zero-sales branch falling back to simple average. This duplicates the "weighted average" concern that `IMarginCalculator` already owns (it has `CalculateForProduct`, `CalculateAsync`, `GetMarginAmountForLevel`, etc.).
- `ApplySorting` (lines 163–215): a 13-branch `switch` over string sort keys, sorting a `List<TopProductDto>`. Self-contained and deterministic, but currently untestable without instantiating the full handler and its dependencies.

Handlers in this codebase are meant to orchestrate (call a service, map, return); business logic — calculation and multi-branch algorithms — belongs in services. This spec adopts the brief's **Option B (preferred)**: add the aggregation method directly to `IMarginCalculator` (the correct single owner of all margin math), and extract sorting into a new DI-registered `ITopProductSorter` service, consistent with the module's existing service-per-concern pattern (`IMarginCalculator`, `IMonthlyBreakdownGenerator` are both registered as scoped services in `AnalyticsModule.cs`).

## Functional Requirements

### FR-1: Move group margin aggregation into `IMarginCalculator`
Add a new method to `IMarginCalculator` (interface + implementation in `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`):

```csharp
GroupMarginData GetGroupAggregatedMarginData(List<AnalyticsProduct> products);
```

Its body is a verbatim move of the current `GetProductMarginSummaryHandler.CalculateGroupMarginData` logic (lines 122–158): empty-list guard returning a default `GroupMarginData`, zero-total-sales branch using simple `Average`, and the weighted-average-by-sales-volume calculation otherwise. No change to the calculation itself — this is a move, not a rewrite.

The `GroupMarginData` class (currently a private/internal nested-style class declared at the bottom of `GetProductMarginSummaryHandler.cs`, lines 229–242) moves alongside the method, into `MarginCalculator.cs` (or an adjacent file in `Services/`), and its accessibility changes from `internal` to `public` since it now crosses from the service's public interface back into the handler's file.

`GetProductMarginSummaryHandler.GenerateTopProducts` (line 78) is updated to call `_marginCalculator.GetGroupAggregatedMarginData(products)` instead of the removed private method.

**Acceptance criteria:**
- `CalculateGroupMarginData` no longer exists as a method on `GetProductMarginSummaryHandler`.
- `IMarginCalculator` exposes `GetGroupAggregatedMarginData(List<AnalyticsProduct>)` returning `GroupMarginData`.
- `MarginCalculator.GetGroupAggregatedMarginData` produces byte-for-byte identical output to the original `CalculateGroupMarginData` for the same inputs (empty list, zero-sales list, weighted-average case).
- All existing tests in `GetProductMarginSummaryHandlerTests.cs` continue to pass unmodified in behavior (assertions on `TotalMargin`, `TopProducts`, `MonthlyData` still hold).
- New unit tests exist directly against `MarginCalculator` (in `MarginCalculatorTests.cs`, alongside the existing `CalculateForProduct` tests) covering: empty product list, zero-total-sales fallback to simple average, and weighted-average calculation with multiple products of differing sales volumes.

### FR-2: Extract sorting into a dedicated `ITopProductSorter` service
Create a new service in `backend/src/Anela.Heblo.Application/Features/Analytics/Services/`:

```csharp
public interface ITopProductSorter
{
    List<TopProductDto> Sort(List<TopProductDto> products, string? sortBy, bool sortDescending);
}
```

with implementation class `TopProductSorter : ITopProductSorter`, whose `Sort` body is a verbatim move of `GetProductMarginSummaryHandler.ApplySorting` (lines 163–215): the null/whitespace `sortBy` default-to-`TotalMargin` branch, and the full 13-case `switch` over lowercased sort keys (`groupkey`/`productcode`, `displayname`/`productname`, `totalmargin`, `m0amount`, `m1amount`, `m2amount`, `m0percentage`, `m1percentage`, `m2percentage`, `sellingprice`, `purchaseprice`, default). No change to sort keys, casing behavior, or default fallback.

Register the new service in `AnalyticsModule.cs` alongside the existing `IMarginCalculator`/`IMonthlyBreakdownGenerator` registrations:
```csharp
services.AddScoped<ITopProductSorter, TopProductSorter>();
```

`GetProductMarginSummaryHandler` takes a new constructor dependency `ITopProductSorter` and calls it (line 108) instead of the removed private `ApplySorting` method.

**Acceptance criteria:**
- `ApplySorting` no longer exists as a method on `GetProductMarginSummaryHandler`.
- `ITopProductSorter`/`TopProductSorter` exist in `Services/`, registered as `Scoped` in `AnalyticsModule.cs`.
- `GetProductMarginSummaryHandler` constructor takes `ITopProductSorter` as a new parameter; all call sites and test fixtures that construct the handler directly are updated (at minimum `GetProductMarginSummaryHandlerTests.cs`, and any other test file constructing this handler — confirm via a repo-wide search before merging).
- `TopProductSorter.Sort` produces identical ordering to the original `ApplySorting` for every sort key (all 13 named keys, an unrecognized key, a null/empty key), in both ascending and descending order.
- New unit tests exist directly against `TopProductSorter` covering: default (no `sortBy`) sort by `TotalMargin`, each of the 13 named sort keys in both directions, and an unrecognized `sortBy` value falling back to `TotalMargin`.
- `GetProductMarginSummaryHandlerTests.cs` continues to pass with the handler wired to a real (non-mocked) `TopProductSorter`, matching the existing pattern where `MarginCalculator`/`MonthlyBreakdownGenerator` are used as real instances rather than mocks in most tests.

### FR-3: Handler reduced to orchestration only
After FR-1 and FR-2, `GetProductMarginSummaryHandler` retains only: `Handle` (orchestration), `GenerateTopProducts` (mapping calculation results to `TopProductDto`, delegating aggregation and sorting to the two new services), and `CalculateTotalMarginForLevel` (a short one-line sum using `IMarginCalculator.GetMarginAmountForLevel`, out of scope for this refactor — see Out of Scope).

**Acceptance criteria:**
- `GetProductMarginSummaryHandler.cs` no longer contains the `GroupMarginData` class declaration.
- The handler file's line count is materially reduced (roughly from 242 lines to under 150, reflecting the ~90 lines moved out).
- No public API surface changes: `GetProductMarginSummaryRequest`, `GetProductMarginSummaryResponse`, and all DTOs are unchanged; the generated OpenAPI/TypeScript client is unaffected.

## Non-Functional Requirements

### NFR-1: Performance
N/A — internal refactor. This is a pure code-move with no algorithmic change; no performance impact is expected or targeted. The existing streaming architecture (`IAsyncEnumerable<AnalyticsProduct>`, noted in the handler's header comment as a prior "PERFORMANCE FIX") is untouched.

### NFR-2: Security
N/A — internal refactor. No auth, data sensitivity, or attack-surface changes; this is an internal Application-layer service reorganization with no new external inputs.

## Data Model
No new domain entities. `GroupMarginData` (existing DTO-like class holding `M0Amount`, `M1Amount`, `M2Amount`, `M0Percentage`, `M1Percentage`, `M2Percentage`, `SellingPrice`, `PurchasePrice`) relocates from `GetProductMarginSummaryHandler.cs` into the `Services/` folder and changes from `internal` to `public` accessibility. No property or shape changes.

## API / Interface Design
No changes to any HTTP endpoint, request/response contract, or MediatR request/response types (`GetProductMarginSummaryRequest`/`GetProductMarginSummaryResponse`). This is purely an internal class/interface reorganization within the Application layer:

- `IMarginCalculator` gains one new method: `GetGroupAggregatedMarginData(List<AnalyticsProduct> products) : GroupMarginData`.
- New interface `ITopProductSorter` with one method: `Sort(List<TopProductDto> products, string? sortBy, bool sortDescending) : List<TopProductDto>`.
- `GetProductMarginSummaryHandler` constructor signature changes (new `ITopProductSorter` dependency added; existing `IAnalyticsRepository`, `IMarginCalculator`, `IMonthlyBreakdownGenerator`, `TimeWindowParser` dependencies unchanged).

## Dependencies
- Existing `IMarginCalculator` / `MarginCalculator` (`backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`) — extended, not replaced.
- Existing DI registration pattern in `AnalyticsModule.cs` (`services.AddScoped<...>`) — new line added for `ITopProductSorter`.
- Test fixtures: `GetProductMarginSummaryHandlerTests.cs`, `MarginCalculatorTests.cs`, and any other test that directly constructs `GetProductMarginSummaryHandler` (must be located and updated for the new constructor parameter — confirm no other call sites exist via `grep -r "new GetProductMarginSummaryHandler"` before considering this complete).
- No new external libraries or services required.

## Out of Scope
- `CalculateTotalMarginForLevel` (lines 220–225) — a short one-line sum delegating to `IMarginCalculator.GetMarginAmountForLevel` — is not flagged by the arch-review finding and is left in the handler.
- Any change to `GetProductMarginAnalysisHandler` or `GetMarginReportHandler` (other Analytics handlers that also depend on `IMarginCalculator`) — this refactor only touches `GetProductMarginSummaryHandler` and the shared `IMarginCalculator`/new `ITopProductSorter` services.
- Any behavioral change to sort ordering, tie-breaking, or margin calculation formulas — this is a structural move only.
- Option A (static helper `TopProductSorter.Sort(...)` without DI/interface) is explicitly not adopted; Option B (DI-registered `ITopProductSorter`) is used per the brief's stated preference and consistency with the module's existing service pattern.

## Open Questions
None.

## Status: COMPLETE
