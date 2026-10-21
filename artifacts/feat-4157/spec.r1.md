# Specification: Eliminate transient 404 on PUT /api/purchase-orders/{id}

## Summary
About 50% of `PUT /api/purchase-orders/{id}` calls in the 2026-09-05..09-12 window returned 404 and succeeded about 3 seconds later on retry with the same payload. The 404 is attributed in telemetry to a missing purchase order, but code analysis shows the same HTTP 404 is also produced by `ErrorCodes.SupplierNotFound`, which depends on a cached Flexi contact list. This feature makes the failure attributable, removes the false 404 for a transient upstream/cache failure, and prevents partial mutation before validation.

## Background
- `UpdatePurchaseOrderHandler.Handle` has two 404-mapped exits: `PurchaseOrderNotFound` (1101, `GetByIdWithDetailsAsync` returns null) and `SupplierNotFound` (1102, `ISupplierRepository.GetByIdAsync` returns null).
- The order read is a plain EF `FirstOrDefaultAsync` by primary key with no `AsNoTracking`, no query filter and no cache. A stale null for an existing row is implausible. The issue's hypothesis (read-after-write visibility) is weak.
- `FlexiSupplierRepository` (singleton) loads all suppliers through `IContactListClient.GetAsync(...)` and caches them for 2h only `if (suppliers.Any())`. A transient Flexi failure or empty or partial result yields a list without the supplier. `GetByIdAsync` then returns null, giving `SupplierNotFound` and HTTP 404. The next call (about 3s later, UI retry) refetches and succeeds. This matches the observed "no exception logged, retry in 3s succeeds" pattern. Both outcomes only log at Warning, and the telemetry key `req-404:PUT PurchaseOrders/UpdatePurchaseOrder` cannot tell them apart.
- The handler calls `purchaseOrder.UpdateOrderNumber(...)` before the supplier check, so a supplier failure leaves the tracked entity mutated (not saved, but the order of operations is fragile).
- The 404 payload carries `ErrorCode` and `Params` (`Id` vs `SupplierId`), so the causes are distinguishable in the response body, but not in telemetry.

## Functional Requirements

### FR-1: Confirm root cause via logging
Warning logs in the handler for both not-found exits must include enough context to attribute the failure. `Supplier with ID {SupplierId} not found` must also include the purchase order id. The supplier repository must log (Warning) when Flexi returns zero suppliers or when the requested id is absent from a non-empty list, including the list size and cache hit or miss.
**Acceptance criteria:**
- A log line on SupplierNotFound includes order Id and SupplierId.
- `FlexiSupplierRepository` logs when the fetched supplier list is empty and when `GetByIdAsync` misses, with the count.

### FR-2: Do not turn a transient supplier lookup failure into a 404
`FlexiSupplierRepository.GetAllSuppliersFromCacheAsync` must not treat a failed or empty Flexi result as authoritative. Behaviour:
- Exceptions from the Flexi client propagate (no swallowing into an empty list).
- An empty result is not cached (already true) and is treated as a failure for `GetByIdAsync`: retry once with a short delay, then throw `InvalidOperationException`-free dedicated exception or return a distinguishable failure (see Open Questions) so the API answers 5xx/503 instead of 404.
- A cache hit that lacks the requested id triggers one forced refresh (cache bypass) before concluding "not found".
**Acceptance criteria:**
- Unit test: first Flexi call returns empty, second returns the supplier, so `GetByIdAsync` returns the supplier.
- Unit test: id missing from the cached list triggers one refresh, and a genuinely unknown id still returns null.
- Unit test: Flexi client exception is not converted to null.

### FR-3: Validate before mutating
In `UpdatePurchaseOrderHandler`, resolve the supplier before any mutation of the order (move the supplier lookup above `UpdateOrderNumber`).
**Acceptance criteria:**
- Unit test: when the supplier is not found, the order entity is unchanged and `SaveChangesAsync` is not called.

### FR-4: Genuine not-found behaviour is preserved
A truly unknown order id still returns 404 `PurchaseOrderNotFound`, and a truly unknown supplier id still returns 404 `SupplierNotFound`.
**Acceptance criteria:** existing `UpdatePurchaseOrderHandlerTests` and controller tests pass unchanged.

## Non-Functional Requirements
### NFR-1: Performance
At most one additional Flexi fetch on a cache miss for a supplier id. No extra DB queries. The cache hit path stays unchanged. Refresh-on-miss must be guarded against a stampede (single-flight) and rate limited, for example no more than one forced refresh per 30s, so that unknown ids cannot hammer Flexi.
### NFR-2: Security
No change to authorization (`Feature.Purchase_PurchaseOrders`, Write).

## Data Model
No schema or migration changes.

## API / Interface Design
No contract change for the happy path. `ISupplierRepository` keeps its signatures. A transient upstream failure surfaces as a non-404 error (500 via existing exception handling, or a mapped error code). No OpenAPI change expected unless a new ErrorCode is added (Open Questions).

## Dependencies
Rem.FlexiBeeSDK `IContactListClient`, `IMemoryCache`. `docs/processes/flow-purchase-order.md` (owns `FlexiSupplierRepository.cs` and the UpdatePurchaseOrder handler), so the process doc must be updated in the same PR (Known quirks: supplier list cached 2h, refresh-on-miss) and `python3 scripts/process-docs/check.py index` run.

## Out of Scope
- Frontend retry behaviour changes.
- Changing the order read path (`GetByIdWithDetailsAsync`), unless new evidence appears.
- Caching strategy redesign beyond refresh-on-miss and not trusting empty results.
- Other handlers using `ISupplierRepository` beyond benefiting from the repository fix.

## Open Questions
None. Assumptions made: (1) the supplier lookup is the dominant cause (the telemetry cannot prove it, so FR-1 logging is included to confirm); (2) a transient Flexi failure should surface as HTTP 5xx using the existing exception pipeline rather than a new ErrorCode.

## Status: COMPLETE
