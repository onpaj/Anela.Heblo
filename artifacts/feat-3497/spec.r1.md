# Specification: Unit test coverage for InventorySummaryTileBase age-bucket logic

## Summary
`InventorySummaryTileBase.LoadDataAsync` (`backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventorySummaryTileBase.cs`) currently has 0% line coverage. This task adds unit tests that pin down the existing behavior of the four inventory-age buckets (`recent`, `medium`, `old`, `never`), including the exact boundary conditions at the `ThresholdCritical` (180 days) and `ThresholdWarning` (365 days) constants. No production code changes are required or expected.

## Background
`InventorySummaryTileBase` is an abstract dashboard tile base class used by concrete warehouse tiles to summarize how long it has been since each catalog item was last inventoried. It buckets filtered catalog items into four counts based on `CatalogAggregate.LastStockTaking` (a derived, nullable `DateTime` computed from the item's `StockTakingHistory`) and returns them alongside a `total` and drill-down metadata.

Because the bucket boundaries are business-defined constants (180/365 days) embedded directly in comparison expressions, a future refactor (e.g., changing `<` to `<=`, or inverting a null check) could silently reassign items between buckets or drop never-inventoried items entirely, with no test to catch the regression. This task closes that gap by adding tests against the existing behavior — it does not change bucket definitions, thresholds, or output shape.

## Functional Requirements

### FR-1: Test bucket assignment at and around the critical threshold (180 days)
Verify that an item whose `LastStockTaking` is exactly 180 days before "now" is **not** counted in `recent`, and is counted in `medium`; an item at 179 days is counted in `recent`.

**Acceptance criteria:**
- An item with `LastStockTaking` = now − 179 days is counted in `recent`, not in `medium` or `old`.
- An item with `LastStockTaking` = now − 180 days is counted in `medium`, not in `recent`.

### FR-2: Test bucket assignment at and around the warning threshold (365 days)
Verify that an item whose `LastStockTaking` is exactly 365 days before "now" is counted in `medium` (inclusive upper bound), and an item at 366 days is counted in `old`.

**Acceptance criteria:**
- An item with `LastStockTaking` = now − 365 days is counted in `medium`, not in `old`.
- An item with `LastStockTaking` = now − 366 days is counted in `old`, not in `medium`.

### FR-3: Test that items with a null `LastStockTaking` land in `never`
Verify an item that has no stock-taking history at all (empty `StockTakingHistory`, so `LastStockTaking` is `null`) is counted in `never` and not silently excluded from every bucket.

**Acceptance criteria:**
- An item with an empty `StockTakingHistory` (`LastStockTaking == null`) is counted in `never`.
- That item is not counted in `recent`, `medium`, or `old`.

### FR-4: Test that `total` equals the sum of all four buckets
Verify that for a mixed set of items spanning all four buckets, `total` equals `recent + medium + old + never`, and equals the count of items passing `ItemFilter`.

**Acceptance criteria:**
- Given one item in each of `recent`, `medium`, `old`, and `never`, `total == 4` and `total == recent + medium + old + never`.
- Items excluded by `ItemFilter` are not counted in `total` or any bucket (use a minimal test-only subclass or an existing concrete tile with a known filter, consistent with the pattern in `LowStockAlertTileTests`).

### FR-5: Preserve existing success/error response shape checks
Add coverage confirming the method returns `status: "success"` with the bucket data on the happy path, consistent with existing tile test conventions (e.g. `LowStockAlertTileTests.LoadDataAsync_HandlesExceptions_ReturnsErrorStatus`). This is incidental coverage gained naturally from exercising `LoadDataAsync`, not a new requirement to test exception handling in depth beyond what falls out of the happy-path tests.

**Acceptance criteria:**
- `result` deserializes with `status == "success"` and `data.recent`, `data.medium`, `data.old`, `data.never`, `data.total` present and correct for the arranged input.

## Non-Functional Requirements

### NFR-1: Performance
N/A — this is a unit test addition with no runtime performance impact.

### NFR-2: Security
N/A — no auth, permissions, or sensitive data handling involved.

## Data Model
No schema changes. Tests construct `CatalogAggregate` instances with a `StockTakingHistory` (`List<StockTakingRecord>`) populated so that the derived `LastStockTaking` property (`StockTakingHistory.OrderByDescending(o => o.Date).FirstOrDefault()?.Date`) yields the desired boundary date, or left empty to yield `null`. A minimal `StockTakingRecord` needs at least `Date` set (other fields such as `Type`, `Code`, `AmountNew`, `AmountOld` can take arbitrary/default test values).

Note for implementation: `LoadDataAsync` computes `now` via `DateTime.UtcNow` directly (no injected `TimeProvider`, unlike some sibling tiles such as `LowStockAlertTile`). Tests must compute each item's `LastStockTaking` relative to `DateTime.UtcNow` at test-execution time (e.g., `DateTime.UtcNow.AddDays(-180)`) rather than using a fixed calendar date, to avoid flakiness from the immutable threshold constants combined with a moving "now".

## API / Interface Design
N/A — no interface or contract changes. Tests exercise the existing `ITile.LoadDataAsync(Dictionary<string,string>?, CancellationToken)` method via a concrete subclass (or a minimal test double subclass of `InventorySummaryTileBase` implementing `Title`, `Description`, `ItemFilter`, and `GenerateDrillDownFilters`) with a mocked `ICatalogRepository.GetAllAsync`.

## Dependencies
- `Moq` for mocking `ICatalogRepository` (already used in sibling tests, e.g. `LowStockAlertTileTests`).
- `FluentAssertions` for assertions (existing convention).
- `System.Text.Json` for parsing the anonymous-object result, following the `JsonSerializer.Serialize` / `JsonDocument.Parse` pattern used in `LowStockAlertTileTests`.
- No new NuGet packages or test infrastructure required.

## Out of Scope
- Any change to `InventorySummaryTileBase.cs` production logic, thresholds, or output shape.
- Testing concrete subclasses of `InventorySummaryTileBase` beyond what's needed to exercise the shared bucket logic (e.g., no requirement to add tests for every concrete tile that derives from this base, unless one is reused merely as a vehicle to invoke `LoadDataAsync`).
- Testing the exception-handling branch (`catch (Exception ex)`) beyond incidental coverage; this brief is scoped to bucket/threshold/null-handling correctness, not exhaustive error-path testing.
- Testing `ItemFilter` implementations of concrete tiles — only enough filtering behavior to confirm filtered-out items don't pollute the buckets/total (FR-4).

## Open Questions
None.

## Status: COMPLETE
