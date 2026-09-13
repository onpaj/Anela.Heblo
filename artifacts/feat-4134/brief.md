## Module
Purchase

## Finding
`UpdatePurchaseOrderStatusHandler` and `UpdatePurchaseOrderInvoiceAcquiredHandler` each call `_repository.UpdateAsync(purchaseOrder, cancellationToken)` immediately before `SaveChangesAsync`, even though the entity was loaded via `GetByIdAsync` (which internally calls `DbSet.FindAsync`) and is therefore **already tracked** by the EF Core change tracker.

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusHandler.cs:51`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderInvoiceAcquired/UpdatePurchaseOrderInvoiceAcquiredHandler.cs:44`

`BaseRepository.UpdateAsync` calls `DbSet.Update(entity)`, which transitions the entry to `EntityState.Modified` and marks **every scalar property** as modified — not just the ones that changed. The result is an `UPDATE` statement that writes all columns on every invocation, regardless of what actually changed.

For contrast, `UpdatePurchaseOrderHandler` loads the entity with `GetByIdWithDetailsAsync` (also tracking) and correctly calls only `SaveChangesAsync`, relying on EF's change detection. This shows the correct pattern is known but was not applied consistently.

## Why it matters
- **Over-writes unchanged data**: status transitions write columns like `OrderNumber`, `OrderDate`, `Notes`, etc. even when they didn't change, which wastes I/O and can interfere with optimistic-concurrency or audit triggers if added later.
- **Inconsistency**: three handlers that perform in-place mutations take three subtly different paths (`UpdateAsync + Save`, `UpdateAsync + Save`, `Save` only), making maintenance harder and tests less reliable across all three.
- **Shadow properties / interceptors**: marking all properties modified resets any shadow-property tracking (e.g. `row_version`) and can interfere with EF interceptors that inspect which properties were actually changed.

## Suggested fix
Remove the redundant `UpdateAsync` call in both handlers. The mutation methods on `PurchaseOrder` already update the entity's state in memory; EF's change tracker detects the diff automatically on `SaveChangesAsync`.

```diff
- await _repository.UpdateAsync(purchaseOrder, cancellationToken);
  await _repository.SaveChangesAsync(cancellationToken);
```

No other changes needed.

---
_Filed by daily arch-review routine on 2026-09-11._
