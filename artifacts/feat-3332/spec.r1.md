# Specification: Remove Explicit GC.Collect() from CatalogAnalyticsSourceAdapter

## Summary

The `CatalogAnalyticsSourceAdapter` calls `GC.Collect()` after every 100-product batch while streaming products to Analytics handlers. This forces unnecessary full-heap blocking collections that slow down every analytics query touching several hundred products. The fix is a one-line deletion with no functional change.

## Background

The Analytics module reads product data through `IAnalyticsProductSource`, implemented by `CatalogAnalyticsSourceAdapter`. This adapter is the single source for all Analytics handlers that call `AnalyticsRepository.StreamProductsWithSalesAsync`. The adapter loads all products into memory, then yields them in 100-product batches. After each batch, it calls `GC.Collect()` — presumably intending to release memory from the previous batch. In .NET, this is unnecessary: once the batch objects leave scope they are already eligible for collection, and the runtime schedules collections based on allocation pressure and heap thresholds. Forcing a collection on a fixed product-count boundary overrides these heuristics with no benefit and measurable cost.

## Functional Requirements

### FR-1: Remove the explicit GC.Collect() call

Remove the `GC.Collect()` call on line 36 of `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs`. No other code changes are required. The batch-streaming loop structure must remain intact.

**Acceptance criteria:**
- Line 36 (`GC.Collect();`) is deleted from `CatalogAnalyticsSourceAdapter`.
- The surrounding `for` loop and `yield return` logic are unchanged.
- `dotnet build` succeeds with no errors or warnings introduced by this change.
- `dotnet format` produces no diff on the modified file.

### FR-2: Verify streaming correctness is unaffected

Confirm that the adapter still streams products correctly in batches after the removal. The batch loop and cancellation-token check must behave identically to before.

**Acceptance criteria:**
- Any existing unit or integration tests covering `CatalogAnalyticsSourceAdapter` or `AnalyticsRepository.StreamProductsWithSalesAsync` pass without modification.
- Manual or automated smoke test of a margin-analysis or product-summary analytics request returns the same results as before the change.

## Non-Functional Requirements

### NFR-1: Performance

Removing the forced collection eliminates stop-the-world Gen-2 / LOH collections on a fixed 100-product cadence. Analytics requests that stream several hundred products are expected to complete faster due to fewer and shorter GC pauses. No regression in throughput is expected; improvement is likely for large product catalogs.

### NFR-2: Memory

Memory behaviour is unchanged from a correctness standpoint. The .NET runtime will still collect batch objects when it determines collection is worthwhile. Peak working-set may vary slightly between GC scheduling decisions, but no sustained memory increase is expected.

### NFR-3: Safety

This is a single-line deletion with no logic change. Risk of regression is minimal. No database migrations, configuration changes, or API contract changes are involved.

## Data Model

No data model changes. The fix is entirely within the streaming infrastructure layer.

## API / Interface Design

No API or interface changes. `IAnalyticsProductSource` and `AnalyticsRepository.StreamProductsWithSalesAsync` signatures and behaviour are unchanged.

## Dependencies

- .NET 8 runtime GC — the fix relies on the standard .NET GC behaving correctly when not interfered with, which is the guaranteed baseline behaviour.
- No new libraries or external services.

## Out of Scope

- Changing the batch size (currently 100 products).
- Replacing `allProducts` list materialisation with a true database-side streaming query (valid future improvement, separate scope).
- Any other GC tuning (e.g., `GCSettings.LatencyMode`, `GC.TryStartNoGCRegion`).
- Changes to other Analytics adapters or handlers.

## Open Questions

None.

## Status: COMPLETE
