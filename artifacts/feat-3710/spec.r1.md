# Specification: Deterministic cutoff date in InventoryCountTileBase

## Summary
`InventoryCountTileBase.LoadDataAsync` computes its inventory-count cutoff date with `DateTime.UtcNow` instead of the already-injected `TimeProvider`, making the filter non-deterministic and untestable (0% coverage). Fix the cutoff to use `_timeProvider.GetUtcNow().UtcDateTime` and add unit tests that pin time via a mocked `TimeProvider`.

## Background
`InventoryCountTileBase` is the abstract base for dashboard tiles that count catalog items inventoried within the last `DaysOffset` (default 30) days. The class already takes a `TimeProvider` dependency and uses it correctly for the display fields (`date`, `lastUpdated`), but line 38 independently calls `DateTime.UtcNow.AddDays(-DaysOffset)` for the actual filter cutoff. This inconsistency means the "current time" used to decide whether an item counts differs, in principle, from the "current time" shown to the user, and — more importantly — cannot be controlled in tests, leaving the core filtering logic (including the null-guard on `LastStockTaking`) completely uncovered.

## Functional Requirements

### FR-1: Use injected TimeProvider for cutoff calculation
Replace `DateTime.UtcNow.AddDays(-DaysOffset)` on line 38 with `_timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset)`, so the cutoff is derived from the same clock abstraction already used elsewhere in the method.

**Acceptance criteria:**
- Line 38 no longer references `DateTime.UtcNow`.
- The computed `cutoffDate` is `_timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset)`.
- No other behavior of `LoadDataAsync` changes (return shape, error handling, `date`/`lastUpdated` fields remain as-is).

### FR-2: Unit test coverage for the cutoff filter
Add unit tests for `InventoryCountTileBase.LoadDataAsync` (via a minimal concrete test subclass or existing subclass, plus a mocked `ICatalogRepository` and a fixed `TimeProvider`, e.g. `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` or an equivalent test double already used in the codebase) covering:

**Acceptance criteria:**
- Test 1: item with `LastStockTaking` exactly equal to the cutoff date/time is **included** in `count` (boundary is inclusive, per existing `>=` comparison).
- Test 2: item with `LastStockTaking` one second before the cutoff is **excluded** from `count`.
- Test 3: item with `LastStockTaking == null` is **excluded** from `count` (no `NullReferenceException`).
- Test 4: a subclass (or test double) overriding `DaysOffset` to a non-default value shifts the cutoff accordingly — an item that would be excluded under the default 30-day window but included under the custom window is counted correctly.
- All four tests pin "now" via the fake `TimeProvider` (no reliance on real wall-clock time) and pass deterministically regardless of when they run.
- Existing tests for this class/subclasses (if any) continue to pass.

## Non-Functional Requirements

### NFR-1: Performance
N/A — no change to algorithmic complexity or I/O; same single repository call and in-memory filter.

### NFR-2: Security
N/A — internal dashboard-tile logic, no new inputs, auth, or data exposure.

## Data Model
N/A — no schema or entity changes. Uses existing `CatalogAggregate.LastStockTaking` (`DateTime?`).

## API / Interface Design
N/A — no public API or contract change. `LoadDataAsync`'s return shape and signature are unchanged; only the internal cutoff computation and its test coverage change.

## Dependencies
- `TimeProvider` (already injected via constructor).
- Test-side fake/mock for `TimeProvider` (e.g. `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider`, or the project's existing convention for faking time — check `docs/architecture/development_guidelines.md` / existing test suite for the established pattern before introducing a new package).
- Mocked `ICatalogRepository` for test data setup.

## Out of Scope
- Changing `DaysOffset` default value or making it configurable at runtime.
- Changing the tile's response shape, drill-down filters, or error-handling behavior.
- Auditing or fixing other tiles/classes for similar `DateTime.UtcNow` usage (this fix is scoped to `InventoryCountTileBase` only).
- Frontend changes.

## Open Questions
None.

## Status: COMPLETE
