# Specification: Consolidate Margin-Level Resolution in GetProductMarginSummaryHandler

## Summary
Remove the duplicated margin-level switch statement in `GetProductMarginSummaryHandler.CalculateTotalMarginForLevel` and delegate to the already-injected `IMarginCalculator.GetMarginAmountForLevel`. This is a small, behavior-preserving refactor that eliminates a leaked concern and ensures future margin-level changes only need to be made in one place.

## Background
During the daily architecture review on 2026-06-03, a duplication was identified in the Analytics module:

- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` (lines 222–230) contains a private helper `CalculateTotalMarginForLevel` whose core logic is a switch over `marginLevel` (`M0`/`M1`/`M2`, with `M2` as the fallback) that selects the per-unit margin amount from an `AnalyticsProduct`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` (lines 113–119) implements `GetMarginAmountForLevel(product, marginLevel)` with the identical switch.

The handler already has `IMarginCalculator` injected (field `_marginCalculator`, line 15) and uses it in `CalculateAsync`, `GetGroupDisplayName`, and `GetGroupKey`, so the dependency is already wired up. The duplication is purely accidental and not the result of a deliberate design choice.

This matters because:
1. Adding a new margin level (e.g., `M3`) or changing the fallback would require updating both sites; missing one would produce a silent inconsistency that is unlikely to be caught by tests focused on the calculator alone.
2. Margin-level resolution is a domain concern owned by `IMarginCalculator`. Reimplementing it inside a handler couples the use case to private knowledge that belongs to the service.

## Functional Requirements

### FR-1: Delegate margin-level resolution to `IMarginCalculator`
`GetProductMarginSummaryHandler.CalculateTotalMarginForLevel` must obtain the per-unit margin amount for a given product and margin level by calling `_marginCalculator.GetMarginAmountForLevel(product, marginLevel)` instead of evaluating an inline switch expression.

**Acceptance criteria:**
- The inline `switch` over `marginLevel.ToUpperInvariant()` in `CalculateTotalMarginForLevel` (handler lines 222–230) is removed.
- The replacement calls `_marginCalculator.GetMarginAmountForLevel(p, marginLevel)` for each product `p`.
- The method body reduces to a single LINQ `Sum` expression equivalent to the suggested fix in the brief.
- No other call sites or behavior in the handler change.
- A search for `"M0" =>` and `"M1" =>` inside `GetProductMarginSummaryHandler.cs` returns zero matches after the change.

### FR-2: Preserve existing output behavior
The refactor must be behavior-preserving. The numeric value returned by `CalculateTotalMarginForLevel` for any combination of `products` and `marginLevel` must be identical before and after the change, including:
- Case-insensitive matching of `marginLevel` (`m0`, `M0`, `Mo` → existing behavior).
- Fallback to `M2` for any unrecognized `marginLevel` value (including null, empty string, or arbitrary text), matching the current `_ => product.M2Amount` branch.
- Multiplication by total sales (`SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)` cast to `decimal`).

**Acceptance criteria:**
- Unit tests covering `GetProductMarginSummaryHandler` continue to pass without modification.
- A new or existing test asserts that passing an unknown `marginLevel` (e.g., `"M9"`, `""`, `null` if reachable) produces the same total as `M2`.
- A test confirms case-insensitive resolution (`"m1"` equals `"M1"`).

### FR-3: Leave `IMarginCalculator.GetMarginAmountForLevel` unchanged
`MarginCalculator.GetMarginAmountForLevel` and its interface signature are the canonical implementation and must not be modified as part of this refactor.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` lines 113–119 are unchanged.
- The `IMarginCalculator` interface is unchanged.
- No new methods are added to the calculator or its interface.

## Non-Functional Requirements

### NFR-1: Performance
The refactor must not introduce measurable performance regression. `GetMarginAmountForLevel` is an O(1) switch and an interface dispatch; the additional virtual call per product is negligible relative to the existing `SalesHistory.Sum` loop.

**Target:** No more than 1% increase in median execution time of `GetProductMarginSummaryHandler.Handle` on a representative dataset (≥1000 products, each with ≥30 sales history entries).

### NFR-2: Security
No security impact. Refactor is internal to the Analytics module, does not change DTOs, API surface, persistence, or authorization.

### NFR-3: Maintainability
After the change, `grep -r "M0\" =>" backend/src/Anela.Heblo.Application/Features/Analytics/` must return exactly one hit (inside `MarginCalculator.cs`).

### NFR-4: Test coverage
Coverage of `GetProductMarginSummaryHandler` must remain at or above its current level. The refactor is not allowed to reduce coverage by removing tests.

## Data Model
No changes to data model, persistence, or DTOs.

Entities referenced (unchanged):
- `AnalyticsProduct` — exposes `M0Amount`, `M1Amount`, `M2Amount` (decimal) and `SalesHistory` (collection of items with `AmountB2B`, `AmountB2C`).
- `IMarginCalculator` — service interface already registered in DI.

## API / Interface Design
No public API changes.

- `GetProductMarginSummaryHandler` continues to implement the same MediatR `IRequestHandler<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>`.
- The handler's private method `CalculateTotalMarginForLevel` keeps the same signature: `private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, string marginLevel)`.
- No new endpoints, no new events, no UI changes, no OpenAPI client regeneration required.

## Dependencies
- `IMarginCalculator` — already injected into `GetProductMarginSummaryHandler` via constructor (field `_marginCalculator`, line 15). No new DI registration needed.
- No new NuGet packages.
- No frontend impact.
- No database migration.

## Out of Scope
- Modifying `MarginCalculator.GetMarginAmountForLevel` or its interface.
- Introducing a new margin level (`M3` or otherwise). The refactor enables this for the future but does not perform it.
- Changing the fallback semantics (silent fall-through to `M2`). If a stricter behavior (e.g., throwing on unknown level) is desired, it must be addressed in a separate change that updates the canonical method in `MarginCalculator` so both call paths benefit.
- Refactoring other call sites in the handler (`CalculateAsync`, `GetGroupDisplayName`, `GetGroupKey`) — they already use `_marginCalculator` correctly.
- Renaming or removing the private `CalculateTotalMarginForLevel` helper. It remains as a one-line method that wraps the LINQ summation; collapsing it inline at its callers is out of scope.
- Searching the rest of the codebase for other duplications of the `M0/M1/M2` switch. If found incidentally, they should be filed as separate findings, not bundled into this change.

## Open Questions
None.

## Status: COMPLETE