# Specification: Dedicated lightweight query path for `GET /api/purchase-orders/{id}/history`

## Summary
The `GET /api/purchase-orders/{id}/history` endpoint currently dispatches `GetPurchaseOrderByIdRequest`, which eagerly loads the entire `PurchaseOrder` aggregate (including all `PurchaseOrderLine` rows, supplier details, and the per-material catalog batch lookup) only to discard everything except the history collection in the controller. This spec introduces a dedicated repository method and MediatR use case (`GetPurchaseOrderHistory`) that loads only `PurchaseOrderHistory` rows, eliminating the wasted I/O and restoring single-responsibility for the existing `GetPurchaseOrderById` use case.

## Background
The architectural review on 2026-05-27 flagged the endpoint as a YAGNI/KISS and SRP violation:

- `PurchaseOrdersController.GetPurchaseOrderHistory` (lines 130–149) dispatches `GetPurchaseOrderByIdRequest`, then projects only `response.History` into a `ListResponse<PurchaseOrderHistoryDto>`.
- `GetPurchaseOrderByIdHandler` does substantially more work than the history endpoint needs:
  - Calls `_repository.GetByIdWithDetailsAsync` which `Include(x => x.Lines).Include(x => x.History)` (two joins).
  - Loads the supplier via `_supplierRepository.GetByIdAsync` to obtain `SupplierNote`.
  - Performs a batch material-catalog lookup (`_materialCatalog.GetByIdsAsync`) over every distinct `MaterialId` referenced by the lines.
  - Builds a complete `GetPurchaseOrderByIdResponse` (lines, totals, contact info, status, audit fields).
- The history endpoint discards all of the above and returns only history rows.

For purchase orders with many lines this materialises a potentially large `PurchaseOrderLine` set, runs a cartesian-product-style join with the history table, and triggers an unrelated material catalog round-trip on every history request. The cost grows with line count even though the response is independent of lines.

The fix is mechanical and low-risk: add a focused repository read for history only, a thin MediatR handler that calls it, and switch the controller over.

## Functional Requirements

### FR-1: Dedicated repository read for purchase order history
Introduce a method on `IPurchaseOrderRepository` that returns only the history rows for a given purchase order, without touching `PurchaseOrderLine`, `Supplier`, or the material catalog.

Signature (domain interface):
```csharp
Task<IReadOnlyList<PurchaseOrderHistory>> GetHistoryAsync(
    int orderId,
    CancellationToken cancellationToken = default);
```

**Acceptance criteria:**
- `IPurchaseOrderRepository.GetHistoryAsync` exists in `backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs`.
- `PurchaseOrderRepository.GetHistoryAsync` in `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs` queries the `PurchaseOrderHistory` `DbSet` filtered by `PurchaseOrderId == orderId`. It must NOT query `PurchaseOrder`, `PurchaseOrderLine`, supplier, or catalog data.
- Results are ordered by `ChangedAt` descending in the repository (newest first), matching the ordering currently applied in `GetPurchaseOrderByIdHandler` (`OrderByDescending(h => h.ChangedAt)`).
- The method returns an empty list when no history rows exist; it does NOT throw and does NOT signal "not found" — distinguishing "order does not exist" from "order has no history yet" is handled by FR-2.
- The query passes the supplied `CancellationToken` through to EF Core.
- Verified via unit/integration test: SQL emitted by EF Core contains only the `PurchaseOrderHistory` table (no JOINs against `PurchaseOrders` or `PurchaseOrderLines`).

### FR-2: Distinguish "order not found" from "order has no history"
The endpoint must continue to return the same not-found semantics it has today (`ErrorCodes.PurchaseOrderNotFound`) when the order ID does not exist. An order that exists but has zero history rows must return an empty list with HTTP 200, not a not-found error.

**Acceptance criteria:**
- Request for an order ID that does not exist returns `ListResponse<PurchaseOrderHistoryDto>` populated with `ErrorCode = ErrorCodes.PurchaseOrderNotFound` and `Params = { "Id": "<id>" }`, identical to the current behaviour.
- Request for an existing order with no history rows returns HTTP 200 with `Items = []` and `TotalCount = 0`.
- Existence check uses a lightweight query — e.g. `DbSet.AnyAsync(o => o.Id == orderId, ct)` — and does NOT load the aggregate.

### FR-3: Dedicated MediatR use case `GetPurchaseOrderHistory`
Introduce a new use case under `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/` consisting of `GetPurchaseOrderHistoryRequest`, `GetPurchaseOrderHistoryResponse`, and `GetPurchaseOrderHistoryHandler`. The handler depends only on `IPurchaseOrderRepository` and `ILogger`, reuses the existing `PurchaseOrderHistoryDto`, and returns `ErrorCodes.PurchaseOrderNotFound` when the order is missing.

### FR-4: Controller wires the new use case
Update `PurchaseOrdersController.GetPurchaseOrderHistory` to dispatch `GetPurchaseOrderHistoryRequest` instead of `GetPurchaseOrderByIdRequest`. HTTP contract is unchanged.

### FR-5: Preserve `GetPurchaseOrderById` semantics
`GetPurchaseOrderByIdRequest`/`Response`/`Handler` and `GetByIdWithDetailsAsync` are not modified — the detail endpoint still loads both lines and history.

## Non-Functional Requirements

- **NFR-1 Performance:** zero `PurchaseOrderLine` rows fetched; query log shows at most two SQL statements (existence + history).
- **NFR-2 Security:** auth/authz unchanged; no raw `OldValue`/`NewValue` logging.
- **NFR-3 Backwards compatibility:** HTTP contract and OpenAPI shape unchanged; TypeScript client regeneration shows no diff.
- **NFR-4 Observability:** info logs on entry/completion, warning on not-found, matching existing handler conventions.

## Data Model
No schema changes. Reuses `PurchaseOrder`, `PurchaseOrderHistory`, and `PurchaseOrderHistoryDto`.

## API / Interface Design
HTTP route and response shape unchanged. New MediatR request/response/handler. New repository method `GetHistoryAsync` that uses `AsNoTracking()` and orders by `ChangedAt` descending. Controller is reduced to dispatch-and-map.

## Dependencies
Internal only: existing Domain/Application/Persistence layers and MediatR. No new packages, no DB migration. The generated TypeScript client must not see a contract diff.

## Out of Scope
Pagination/filtering on history, contract evolution, caching/ETag, authorization changes, and unrelated Purchase refactors flagged in separate briefs.

## Open Questions
None.

## Status: COMPLETE