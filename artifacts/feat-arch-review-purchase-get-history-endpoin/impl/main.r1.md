All tasks complete. Let me write the output artifact.

---

# Implementation: Dedicated lightweight query path for `GET /api/purchase-orders/{id}/history`

## What was implemented

Replaced the wasteful aggregate load behind `GET /api/purchase-orders/{id}/history` with a dedicated repository method + MediatR use case that fetches only `PurchaseOrderHistory` rows. The endpoint no longer loads `PurchaseOrderLine` rows, supplier details, or material catalog data.

## Files created/modified

### New files
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs` — MediatR request record `GetPurchaseOrderHistoryRequest(int Id) : IRequest<ListResponse<PurchaseOrderHistoryDto>>`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs` — Thin handler: `ExistsAsync` → `GetHistoryAsync` → project to DTO → return `ListResponse`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistoryTests.cs` — 5 InMemory-DB unit tests for repository methods
- `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs` — Testcontainers PostgreSQL integration test asserting SQL touches only `PurchaseOrderHistory`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs` — 4 unit tests for the handler

### Modified files
- `backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs` — Added `ExistsAsync(int id, CT)` and `GetHistoryAsync(int orderId, CT)`
- `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs` — Implemented both methods (`AsNoTracking`, filter, `OrderByDescending(h => h.ChangedAt)`)
- `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs` — `GetPurchaseOrderHistory` action reduced from ~20 lines to 2 lines; now dispatches `GetPurchaseOrderHistoryRequest` and delegates to `HandleResponse()`

## Tests

| File | Tests |
|---|---|
| `PurchaseOrderRepositoryHistoryTests.cs` | `ExistsAsync_ReturnsTrue`, `ExistsAsync_ReturnsFalse`, `GetHistoryAsync_ReturnsEmpty`, `GetHistoryAsync_OrderedByChangedAtDescending`, `GetHistoryAsync_DoesNotReturnOtherOrders` |
| `PurchaseOrderRepositoryHistorySqlShapeTests.cs` | `GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable` (PostgreSQL + interceptor) |
| `GetPurchaseOrderHistoryHandlerTests.cs` | `Handle_ReturnsPurchaseOrderNotFound`, `Handle_ReturnsEmptyList`, `Handle_ReturnsMappedHistory_InRepositoryOrder`, `Handle_DoesNotCallGetByIdWithDetailsAsync` |

**Full suite result:** 4,321 tests pass, 3 skipped (pre-existing), 0 failures.

## How to verify

```bash
# Run all new tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PurchaseOrderRepositoryHistoryTests|FullyQualifiedName~PurchaseOrderRepositoryHistorySqlShapeTests|FullyQualifiedName~GetPurchaseOrderHistoryHandlerTests"

# Full suite (without Docker-dependent integration tests)
dotnet test backend/Anela.Heblo.sln --filter "Category!=Integration"

# Build
dotnet build backend/Anela.Heblo.sln
```

## Notes

- `GetPurchaseOrderByIdHandler` and `GetByIdWithDetailsAsync` are **untouched** — the detail endpoint still loads lines and history as before (FR-5 preserved).
- The `InMemoryPurchaseOrderRepository` (used in some test factories) was also updated by the implementer to satisfy the new interface contract — `ExistsAsync` checks the in-memory dictionary, `GetHistoryAsync` returns an empty list.
- Two SQL statements on the happy path (existence check + history query) vs. the old single query that materialised all lines and ran a material-catalog round-trip. Net latency is substantially lower.
- The `[Trait("Category", "Integration")]` attribute on `PurchaseOrderRepositoryHistorySqlShapeTests` enables CI to run it only where Docker/Podman is available.

## Commit chain

```
a716bfcf feat(purchase): switch /history endpoint to dedicated GetPurchaseOrderHistory use case
2cf0d301 feat(purchase): add GetPurchaseOrderHistoryHandler thin use case
11d2334f test(purchase): assert GetHistoryAsync SQL touches only PurchaseOrderHistory
601caff5 feat(purchase): add ExistsAsync and GetHistoryAsync to purchase order repository
```

## PR Summary

Eliminated wasteful aggregate loading from `GET /api/purchase-orders/{id}/history`. The endpoint previously dispatched `GetPurchaseOrderByIdRequest`, which loaded all `PurchaseOrderLine` rows, fetched supplier details, and ran a material-catalog batch lookup — then discarded all of it to return only history entries. For purchase orders with many lines this was an unnecessary round-trip that scaled with line count.

The fix introduces a focused repository query (`GetHistoryAsync`) that touches only the `PurchaseOrderHistory` table, a lightweight existence check (`ExistsAsync`) to preserve the not-found 404 semantics, and a thin `GetPurchaseOrderHistoryHandler` that composes the two. The controller action shrinks from ~20 lines to 2. The detail endpoint (`GET /api/purchase-orders/{id}`) is unchanged.

Worst-case two SQL statements (existence + history) replace the old single query that joined `PurchaseOrders`, `PurchaseOrderLines`, and `PurchaseOrderHistory` and triggered a material-catalog service call. A Testcontainers SQL-shape test guards against regression where EF Core might silently re-add a JOIN.

### Changes
- `IPurchaseOrderRepository.cs` — added `ExistsAsync` and `GetHistoryAsync` interface declarations
- `PurchaseOrderRepository.cs` — implemented both methods with `AsNoTracking()`, filter, and descending sort
- `GetPurchaseOrderHistoryRequest.cs` — new MediatR request record
- `GetPurchaseOrderHistoryHandler.cs` — new thin handler (ExistsAsync → GetHistoryAsync → map → return)
- `PurchaseOrdersController.cs` — `GetPurchaseOrderHistory` action reduced to 2 lines
- `PurchaseOrderRepositoryHistoryTests.cs` — 5 InMemory repository unit tests
- `PurchaseOrderRepositoryHistorySqlShapeTests.cs` — Testcontainers SQL-shape integration test
- `GetPurchaseOrderHistoryHandlerTests.cs` — 4 handler unit tests

## Status

DONE