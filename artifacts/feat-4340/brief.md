## Module / File
`backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/Sync/ConversionsSyncService.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The existing `SyncOrchestrationTests` use a stub `IGa4EntitySyncService`, so the concrete `ConversionsSyncService.SyncChunkAsync` body is never exercised. Uncovered paths include:

1. **`WithoutOtherBucket` filtering**: GA4 rows whose channel group is the "(other)" bucket are silently dropped; no test verifies that this filtering happens correctly or that valid rows are preserved.
2. **Row-to-entity mapping**: `Ga4ValueParser.ToDate`, `ToDimension`, `ToLong`, and `ToDecimal` are called on specific positional indexes (`DimensionValues[0]`, `MetricValues[0]`, etc.); a column-order regression would silently store revenue figures in the wrong field.
3. **Upsert key and update logic**: `Ga4ChunkUpsert.ReplaceRangeAsync` is called with `(Date, ChannelGroup)` as the composite key and specific field assignments in the update lambda; neither the key selection nor the field update behaviour is asserted.
4. **`ChunkOutcome` construction**: the returned `(report.Rows.Count, upserted)` tuple is never verified.

## Why it matters
`ConversionsSyncService` feeds the `conversions_daily` table, which is the source for e-commerce revenue reporting. A silent regression in channel-group filtering, positional mapping, or upsert semantics would store wrong or duplicate revenue figures without any observable error.

## Suggested approach
Unit or integration test using the `Ga4TestHarness` pattern already present in the test project:
- A report with one valid row and one "(other)" row → only one entity upserted
- A fresh sync on an empty DB window → new entity inserted
- A re-sync over an existing row → entity updated (not duplicated)
- Column-order sanity: verify `Transactions` and `PurchaseRevenue` are not swapped

Effort: ~2–3 hours; uses existing `Ga4TestHarness` and in-memory EF context.

---
_Filed by weekly coverage-gap routine on 2026-09-28. Based on CI run #35977921040 (22bb3b8ff6194bdd055cb438d08b7c6633a85221)._