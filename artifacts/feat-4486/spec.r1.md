# Specification: Lighter material-name lookup in GetDailyConsumptionBreakdownHandler

## Summary
Replace the full entity-graph load (`GetAllWithAllocationsAsync`) in the daily consumption breakdown handler with `GetMaterialNamesByIdsAsync`, and build the name lookup once.

## Functional Requirements
### FR-1: Use name-only lookup
Handler fetches names via `GetMaterialNamesByIdsAsync` with the distinct `PackingMaterialId`s of the day's consumptions. `GetAllWithAllocationsAsync` is no longer called.
**Acceptance:** existing handler tests pass; unknown material id still labelled "Unknown".
### FR-2: Dedupe
The three `BuildGroupBy*` methods receive `IReadOnlyDictionary<int,string> materialNameById` instead of the materials list; no per-method dictionary builds.
### FR-3: Unchanged behaviour
Early return for empty consumptions, response shape, ordering, and exception propagation are unchanged.

## Non-Functional Requirements
Performance: no allocations loaded. No API/contract/DB change.

## Out of scope
Other handlers, repository changes.
