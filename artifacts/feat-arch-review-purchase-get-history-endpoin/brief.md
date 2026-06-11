## Module
Purchase

## Finding
The `GET /api/purchase-orders/{id}/history` endpoint dispatches `GetPurchaseOrderByIdRequest` and then discards all loaded line data:

- File: `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs`
- Lines 131–149: `GetPurchaseOrderHistory` action

`GetPurchaseOrderByIdRequest` causes `PurchaseOrderRepository.GetByIdWithDetailsAsync` to eagerly load both `Lines` and `History` via two `.Include()` calls (file: `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs`, lines 75–81). For any order with many lines, this loads potentially large line collections only to throw them away.

## Why it matters
This violates YAGNI / KISS: every call to the history endpoint also incurs the cost of loading all `PurchaseOrderLine` rows for that order, which is completely unused by the response. As orders grow in line count, this becomes an increasingly expensive no-op. It also breaks the Single Responsibility principle — the `GetPurchaseOrderById` use case is repurposed as a history loader.

## Suggested fix
Add a dedicated lightweight query path for history. The smallest change is a new repository method:
```csharp
// IPurchaseOrderRepository
Task<IReadOnlyList<PurchaseOrderHistory>> GetHistoryAsync(int orderId, CancellationToken cancellationToken = default);
```
With a corresponding repository implementation that queries only the `PurchaseOrderHistory` table. The controller action (or a new `GetPurchaseOrderHistoryHandler`) then calls this directly without loading any `PurchaseOrderLine` data.

---
_Filed by daily arch-review routine on 2026-05-27._