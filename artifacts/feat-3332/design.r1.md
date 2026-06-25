# Design: Remove Explicit GC.Collect() from CatalogAnalyticsSourceAdapter

## Component Design

Single file change. No component boundaries, interfaces, or contracts are affected.

**File:** `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs`

Delete line 36 (`GC.Collect();`) from inside the batch loop. The class is `internal sealed`; no callers depend on this behaviour. All existing logic — the `for` loop, batch accumulation, and `yield return` — remains unchanged.

## Data Schemas

No schema changes. No API request/response shapes are affected. No events or contracts change.
