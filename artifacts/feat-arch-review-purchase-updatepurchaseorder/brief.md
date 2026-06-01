## Module
Purchase

## Finding
The validator for `UpdatePurchaseOrderRequest` is physically located inside the `CreatePurchaseOrder` use case folder and carries the wrong namespace:

- **File (wrong location):** `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`
- **Line 4 (wrong namespace):** `namespace Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;`
- **Line 1 (validated type from another use case):** `using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;`

The file validates `UpdatePurchaseOrderRequest` and `UpdatePurchaseOrderLineRequest` — both of which belong to the `UpdatePurchaseOrder` use case — but is filed under the `CreatePurchaseOrder` folder.

## Why it matters
This breaks the co-location principle of Vertical Slice Architecture (one use case folder contains everything for that use case). A developer searching for the `UpdatePurchaseOrder` validator will not find it in the expected `UseCases/UpdatePurchaseOrder/` directory. The namespace mismatch also signals incorrect ownership — any IDE navigation from `UpdatePurchaseOrderHandler` to its validator will be misleading.

## Suggested fix
Move the file to `UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs` and update the namespace to:
```csharp
namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
```
No logic changes needed — just a rename/move.

---
_Filed by daily arch-review routine on 2026-05-27._