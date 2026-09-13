# Design: Remove Redundant UpdateAsync Calls in Purchase Order Handlers

## Component Design

No new components. Two existing MediatR command handlers in the Purchase module lose one redundant line each; their public contracts, dependencies, and control flow are otherwise unchanged.

### `UpdatePurchaseOrderStatusHandler`
- File: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusHandler.cs`
- Responsibility: handle `UpdatePurchaseOrderStatusRequest` — load the `PurchaseOrder` via `_repository.GetByIdAsync` (EF-tracked), validate the requested status string, invoke the domain method `purchaseOrder.ChangeStatus(newStatus, updatedBy)`, persist, and map to `UpdatePurchaseOrderStatusResponse`.
- Change: delete `await _repository.UpdateAsync(purchaseOrder, cancellationToken);` (currently line 51, immediately before `await _repository.SaveChangesAsync(cancellationToken);` inside the `try` block). `SaveChangesAsync` remains the sole persistence call; EF's change tracker (entity already attached/`Unchanged` from `GetByIdAsync` → `DbSet.FindAsync`) detects the properties `ChangeStatus` mutated and emits a minimal `UPDATE`.
- Unchanged: constructor/DI (`ILogger`, `IPurchaseOrderRepository`, `ICurrentUserService`), the not-found branch, the `Enum.TryParse` validation branch, the `catch (InvalidOperationException ex)` block, all logging, and the response mapping.

### `UpdatePurchaseOrderInvoiceAcquiredHandler`
- File: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderInvoiceAcquired/UpdatePurchaseOrderInvoiceAcquiredHandler.cs`
- Responsibility: handle `UpdatePurchaseOrderInvoiceAcquiredRequest` — load the `PurchaseOrder` via `_repository.GetByIdAsync` (EF-tracked), invoke the domain method `purchaseOrder.SetInvoiceAcquired(request.InvoiceAcquired, updatedBy)`, persist, and map to `UpdatePurchaseOrderInvoiceAcquiredResponse`.
- Change: delete `await _repository.UpdateAsync(purchaseOrder, cancellationToken);` (currently line 44, immediately before `await _repository.SaveChangesAsync(cancellationToken);` inside the `try` block). `SaveChangesAsync` remains the sole persistence call.
- Unchanged: constructor/DI, the not-found branch, the generic `catch (Exception ex)` block returning `ErrorCodes.PurchaseOrderUpdateFailed`, all logging, and the response mapping.

### Not touched
- `IPurchaseOrderRepository` and `BaseRepository<TEntity, TKey>` (including `UpdateAsync` and `GetByIdAsync`) — signatures and implementations are unchanged; `UpdateAsync` remains valid for other call sites that attach not-yet-tracked entities.
- `UpdatePurchaseOrderHandler` — already follows the target pattern (load via `GetByIdWithDetailsAsync`, mutate, call only `SaveChangesAsync`); used here as the reference implementation, not modified.
- Domain methods `PurchaseOrder.ChangeStatus` and `PurchaseOrder.SetInvoiceAcquired` — unchanged.

### Test updates (mechanical consequence of the handler change, same files/classes)
- `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs`: in `Handle_ShouldCallRepositoryMethods`, remove (or change to `Times.Never`) the `_repositoryMock.Verify(x => x.UpdateAsync(...), Times.Once)` assertion, since the handler no longer calls it.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderInvoiceAcquiredHandlerTests.cs`:
  - `Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist`: remove (or change to `Times.Never`) the `UpdateAsync` verification.
  - `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError`: rewrite so the mocked `SaveChangesAsync` (not `UpdateAsync`) throws, since `UpdateAsync` is no longer invoked; this keeps the handler's `catch (Exception ex)` → `ErrorCodes.PurchaseOrderUpdateFailed` path covered. Rename optional (e.g. `Handle_WhenSaveChangesThrows_ShouldReturnUpdateFailedError`).
  - `Handle_WithNonExistentOrder_ShouldReturnError` already asserts `Times.Never` for `UpdateAsync` — no change.

## Data Schemas

No data schema changes. No database schema, entity, DTO, request/response contract, or event payload is added, removed, or modified by this change:
- `PurchaseOrder` entity/aggregate: unchanged.
- `UpdatePurchaseOrderStatusRequest` / `UpdatePurchaseOrderStatusResponse`: unchanged shape and field values.
- `UpdatePurchaseOrderInvoiceAcquiredRequest` / `UpdatePurchaseOrderInvoiceAcquiredResponse`: unchanged shape and field values.
- Generated OpenAPI/TypeScript client: unaffected (no controller or contract change).

The only observable effect is at the SQL level: the `UPDATE` statement EF Core generates for these two use cases will contain only the columns actually mutated by `ChangeStatus` / `SetInvoiceAcquired` (e.g. `Status`, `UpdatedAt`, `UpdatedBy` — or `InvoiceAcquired`, `UpdatedAt`, `UpdatedBy`) instead of every scalar column on `PurchaseOrder`. This is an internal persistence detail, not a schema or contract change, and requires no migration.
