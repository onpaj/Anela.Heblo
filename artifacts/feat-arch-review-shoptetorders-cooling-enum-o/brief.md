## Module
ShoptetOrders (symptom; root is codebase-wide)

## Finding
The `Cooling` enum is defined in the **Catalog** module's domain namespace:

```
backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs
```

It is imported directly by at least four unrelated modules:

| File | Module |
|---|---|
| `Application/Features/ShoptetOrders/IPackingOrderClient.cs:1` | ShoptetOrders |
| `Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs:3` | ShoptetOrders |
| `Domain/Features/Logistics/CarrierCoolingSetting.cs` | Logistics |
| `Domain/Features/Analytics/AnalyticsProduct.cs` | Analytics |
| `Domain/Features/Manufacture/Ingredient.cs` | Manufacture |
| `Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` | ShoptetApi adapter |
| `Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs` | ShoptetApi adapter |

This means ShoptetOrders, Logistics, Analytics, and Manufacture all take a compile-time dependency on the Catalog module's domain layer to reference what is effectively a shared business concept (temperature-chain shipping classification).

## Why it matters
Per the module boundary rules: _"No direct access to another module's entities."_ When `Cooling` is placed in `Catalog`, any module that uses it must reference `Anela.Heblo.Domain.Features.Catalog`. This creates invisible coupling: a rename or removal of `Cooling` in Catalog breaks Logistics, ShoptetOrders, Manufacture, and Analytics simultaneously. It also makes the future goal of deploying modules independently impossible for these modules.

## Suggested fix
Move `Cooling.cs` to the shared Domain location that already exists for cross-module types:

```
backend/src/Anela.Heblo.Domain/Shared/Cooling.cs
```

Namespace: `Anela.Heblo.Domain.Shared` (or a sub-namespace like `Anela.Heblo.Domain.Shared.Logistics`).

Update all `using Anela.Heblo.Domain.Features.Catalog;` references in the affected files above. No logic changes required.

---
_Filed by daily arch-review routine on 2026-05-23._