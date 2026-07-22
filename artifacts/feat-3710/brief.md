## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventoryCountTileBase.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
**Date cutoff filter** — `w.LastStockTaking.HasValue && w.LastStockTaking.Value >= cutoffDate` is never exercised. Items with null `LastStockTaking`, items just before and just after the cutoff, and the default `DaysOffset = 30` are all untested.

**`DateTime.UtcNow` instead of `TimeProvider`** — line 38 computes the cutoff as `DateTime.UtcNow.AddDays(-DaysOffset)`, bypassing the injected `TimeProvider` that the class already holds. The tile's date field (line 52) correctly uses `_timeProvider.GetUtcNow()`, but the cutoff used for filtering does not. This makes the filter non-deterministic in tests and means the tile cannot be reliably tested against a fixed point in time.

## Why it matters
Items inventoried just before the 30-day boundary could flip in or out of the count depending on when a test runs. Any bug in the HasValue guard (e.g., dropping it) would cause a null-reference exception at runtime rather than a clean count of zero.

## Suggested approach
Fix the cutoff to use `_timeProvider.GetUtcNow().UtcDateTime`, then add unit tests with a mocked `TimeProvider` and `ICatalogRepository` (≈ low effort):
1. Item with `LastStockTaking` exactly at the cutoff — verify it is included.
2. Item with `LastStockTaking` one second before the cutoff — verify it is excluded.
3. Item with null `LastStockTaking` — verify it is excluded.
4. Subclass with a custom `DaysOffset` — verify the cutoff shifts accordingly.

---
_Filed by weekly coverage-gap routine on 2026-07-20. Based on CI run #29525794843 (bba537b141de1dba71a2c6853c4ff3f7e96153b2)._
