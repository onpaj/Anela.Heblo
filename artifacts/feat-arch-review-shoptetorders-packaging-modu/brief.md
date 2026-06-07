## Module
ShoptetOrders / Packaging

## Finding
`ScanPackingOrderHandler` (in the `Packaging` module) directly injects and reads `ShoptetOrdersSettings` — a concrete settings class owned by the `ShoptetOrders` module:

**`backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`**
- Line 18: `private readonly ShoptetOrdersSettings _orderSettings;`
- Line 28: `IOptions<ShoptetOrdersSettings> orderSettings,`
- Line 49: `var isEligible = order.StatusId == _orderSettings.PackingStateId;`
- Line 186: `await _eshopOrderClient.UpdateStatusAsync(orderCode, _orderSettings.PackedStateId, ct);`

The `development_guidelines.md` rule is: "No direct references between feature modules" and "Communication only through contracts/interfaces." `ShoptetOrdersSettings` is a concrete class in the `ShoptetOrders` namespace, not a contract interface.

## Why it matters
The `Packaging` module is tightly coupled to a Shoptet-specific configuration detail it does not own. Two Shoptet status IDs (`PackingStateId`, `PackedStateId`) leak into business logic that is otherwise Shoptet-agnostic. If `ShoptetOrdersSettings` is renamed, moved, or split, `ScanPackingOrderHandler` breaks silently. Additionally, packing eligibility is computed identically in both `GetPackingOrderHandler` (`ShoptetOrders`) and `ScanPackingOrderHandler` (`Packaging`), duplicating the rule in two modules.

## Suggested fix
Two minimal changes:

1. **Encapsulate eligibility in the contract.** Add `bool IsEligibleForPacking` to `PackingOrder` (in `IPackingOrderClient.cs`) and set it in `ShoptetApiPackingOrderClient` (which already knows `PackingStateId` via its own settings). Remove the `PackingStateId` read from `ScanPackingOrderHandler`.

2. **Add `MarkAsPackedAsync` to `IEshopOrderClient`.** Add a single-purpose method to the interface:
   ```csharp
   Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
   ```
   Implement it in `ShoptetOrderClient` using `PackedStateId` from its own settings. Replace the `UpdateStatusAsync(orderCode, _orderSettings.PackedStateId, ct)` call in `ScanPackingOrderHandler` with `MarkAsPackedAsync`. This removes the last reason for `Packaging` to reference `ShoptetOrdersSettings` at all.

---
_Filed by daily arch-review routine on 2026-06-05._