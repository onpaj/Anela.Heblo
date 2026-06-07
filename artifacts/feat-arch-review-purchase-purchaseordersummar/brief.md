## Module
Purchase

## Finding
`GetPurchaseOrdersHandler` always sets `SupplierId = 0` in the summary DTO, with an inline comment acknowledging this:

- File: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/GetPurchaseOrdersHandler.cs`
- Line 44: `SupplierId = 0, // No longer using SupplierId`

The `PurchaseOrderSummaryDto` still declares `public int SupplierId { get; set; }` (file: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderSummaryDto.cs`, line 9), and this field is serialised and exposed through the API and generated TypeScript client.

Note also a type inconsistency: the domain entity `PurchaseOrder.SupplierId` is `long`, but the DTO field is `int`.

## Why it matters
A public API field that is always `0` is a misleading contract: consumers (frontend, API clients) may rely on the field assuming it carries a real value. The comment confirms the field is intentionally not populated, making it dead data in the API response. It also inflates the serialised payload and the generated TypeScript type unnecessarily.

## Suggested fix
Two options (pick one):
1. **Remove** `SupplierId` from `PurchaseOrderSummaryDto` if it is truly no longer needed. Update any frontend code that references it.
2. **Populate it** from the entity (`SupplierId = (int)order.SupplierId`) if the value is actually wanted by consumers.

If removing: also remove the type mismatch (`long` vs `int`) concern. Either way, delete the `// No longer using SupplierId` comment — it signals the field should have been cleaned up earlier.

---
_Filed by daily arch-review routine on 2026-05-27._