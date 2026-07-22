# Code Review: fix-cutoff-timeprovider-and-add-tests (r1)

## Review Result: CLEAN

### Blocking
- None

### Advisory
- None

## Notes

**Plan alignment**

- FR-1: `DateTime.UtcNow` is gone from `InventoryCountTileBase.LoadDataAsync`; the cutoff now reads `_timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset)`, matching the spec's exact wording. `_timeProvider` was already a constructor-injected field prior to this change (confirmed in `InventoryCountTileBase.cs`), so no DI wiring changes were needed and none were made. No other line in the method was touched — the two remaining `_timeProvider.GetUtcNow().DateTime` calls (for `data.date` and `metadata.lastUpdated`) were already using the injected provider before this diff and are correctly left alone, consistent with "no other behavior changes."
- FR-2: exactly 4 new tests, each pinning "now" via `FakeTimeProvider(FrozenNow)`:
  - `LoadDataAsync_ItemAtExactCutoff_IsIncluded` — item dated exactly `FrozenNow - 30d` is counted, verifying the `>=` boundary is inclusive (matches the `w.LastStockTaking.Value >= cutoffDate` comparison in production code).
  - `LoadDataAsync_ItemOneSecondBeforeCutoff_IsExcluded` — item dated `FrozenNow - 30d - 1s` is excluded, verifying the boundary is tight rather than day-granular.
  - `LoadDataAsync_ItemWithNullLastStockTaking_IsExcluded` — item with no `StockTakingHistory` entries (so `LastStockTaking` is null via the computed property) is excluded, confirming the `HasValue` guard prevents any null-related fault.
  - `LoadDataAsync_CustomDaysOffset_ShiftsCutoff` — a test-only subclass sets `DaysOffset = 7`, and only the item within that narrower window is counted, confirming the cutoff genuinely depends on `DaysOffset` rather than a hardcoded 30.
  - All four assert through `LoadDataAsync`'s actual JSON-serialized output (`data.count`), i.e. they exercise the real production code path rather than a reimplementation of the filter logic.

**Out-of-scope check**: default `DaysOffset` (30) is untouched; response shape/error handling untouched; no other `DateTime.UtcNow` call sites were touched; no frontend changes. Matches the stated scope.

**Correctness verification performed**:
- Confirmed `LastStockTaking` (`CatalogAggregate.cs:158`) is `StockTakingHistory.OrderByDescending(o => o.Date).FirstOrDefault()?.Date`, backed by `StockTakingHistory` defaulting to `new()` (`CatalogAggregate.cs:85`) — so the null-history test case is a faithful, NRE-safe repro of "no stock taking yet," not a contrived edge case.
- Confirmed `ProductInventoryCountTile`'s constructor signature (`ICatalogRepository`, `TimeProvider`) matches what the tests instantiate, and its `ItemFilter` (`Type == ProductType.Product || Type == ProductType.Goods`) is satisfied by the `ProductType.Product` items used in the tests.
- Confirmed the test-only `CustomOffsetInventoryCountTile` subclass correctly narrows scope by using `ItemFilter => c => true` and a trivial `GenerateDrillDownFilters`, isolating the assertion to the cutoff-shift behavior only.

**Quality observations (non-blocking)**:
- Tests assert via `JsonSerializer.Serialize` + `JsonDocument.Parse` round-trip rather than casting the anonymous return type directly (e.g. via `dynamic` or reflection). This is a reasonable, if slightly indirect, way to inspect an anonymous-typed return value across assembly boundaries in a test project — consistent with how such anonymous-object tile responses are apparently tested elsewhere in this codebase pattern. Not a defect.
- The private helper `CreateItem` and nested `CustomOffsetInventoryCountTile` class are appropriately scoped to the test file and not over-engineered.

No issues found. The diff is a faithful, minimal, well-tested implementation of the stated intent.
