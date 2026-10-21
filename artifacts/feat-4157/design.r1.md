# Design: Eliminate transient 404 on PUT /api/purchase-orders/{id}

## Component Design

### FlexiSupplierRepository (adapter, singleton)
- Fields added: `SemaphoreSlim _refreshLock = new(1,1)`, `DateTime _lastForcedRefreshUtc`, const `MinRefreshInterval = 30s`.
- `GetByIdAsync(id)`:
  1. `list = GetAllSuppliersFromCacheAsync()`; if found, return it.
  2. Not found: log Warning (`list count`, `id`). Enter `_refreshLock`; if `now - _lastForcedRefreshUtc >= MinRefreshInterval`, remove the cache entry, reload via `GetAllSuppliersFromCacheAsync()`, set `_lastForcedRefreshUtc`. Re-check the list. Return match or null.
  3. Empty fetch result: log Warning "Flexi returned zero suppliers" (not cached, as today).
- Flexi exceptions are not caught.
- `SearchSuppliersAsync` unchanged.

### UpdatePurchaseOrderHandler
Order of operations: load order (404 `PurchaseOrderNotFound` with Warning log including Id) -> load supplier (404 `SupplierNotFound`, Warning log with order Id and SupplierId) -> inside the try: current user, `UpdateOrderNumber`, `Update`, line diff, `SaveChangesAsync`, map. The remainder of the logic is unchanged.

## Data Schemas
No DB, API request or response changes. Error responses keep `ErrorCode` 1101 `{Id}` and 1102 `{SupplierId}`.
