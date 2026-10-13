## Module / File
`backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/TransportBoxBaseTile.cs`

## Coverage
Line coverage: 0.0% (filter threshold: 60%)

## What's not tested
`GenerateDrillDownFilters` branches on three cases: `FilterStates.Length == 1` (single-state tile), `FilterStates.Length > 1` with an active filter already matching (sub-check on whether the active filter contains the ACTIVE sentinel), and the fallback empty object. No tests verify that the `"ACTIVE"` sentinel string is only returned when all states in the array are non-Closed, or that the fallback fires when none of the state conditions match. The `LoadDataAsync` error path (returns an error-shape object on exception) is also unexercised.

## Why it matters
Dashboard tiles silently return incorrect filter payloads when the drill-down URL is constructed with the wrong state. A regression in the ACTIVE sentinel logic would cause the wrong transport box subset to appear when clicking the tile, which is a data-visibility bug.

## Suggested approach
Unit tests for a concrete subclass (or a test double): one test per branch in `GenerateDrillDownFilters` (single state, multi-state with active match, fallback); one test for the exception path in `LoadDataAsync`. Estimated effort: ~2h.

---
_Filed by weekly coverage-gap routine on 2026-09-14. Based on CI run #34699120372 (722ec6efc4c1f7e5db235606c763a2f2b4a9a374)._
