## Module
ExpeditionList (Shoptet adapter)

## Finding
`ShoptetApiExpeditionListSource` constructor (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`, line 42) accepts `IEshopOrderClient` but immediately casts it to the concrete type:

```csharp
_client = (ShoptetOrderClient)client;
```

The three methods this class actually calls — `GetOrdersByStatusAsync`, `GetExpeditionOrderDetailAsync`, and `SetAdditionalFieldAsync` — are not declared on `IEshopOrderClient` (see `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`). The interface injection serves no purpose: the constructor signature accepts an abstraction but the code requires the concrete type.

## Why it matters
- **Interface Segregation (ISP)**: `IEshopOrderClient` does not cover the operations needed here, so injecting it via the interface is misleading.
- **Hidden hard dependency**: the constructor signature lies — the real dependency is `ShoptetOrderClient`. Any other `IEshopOrderClient` implementation will throw `InvalidCastException` at first use.
- **Testability**: test code must supply a real `ShoptetOrderClient` despite the constructor accepting an interface, which is confusing.

## Suggested fix
Inject `ShoptetOrderClient` directly — the class is already in the same adapter assembly, so depending on the concrete type is appropriate here:

```csharp
public ShoptetApiExpeditionListSource(
    ShoptetOrderClient client,   // concrete; already in this assembly
    TimeProvider timeProvider,
    ICatalogRepository catalog,
    ...
```

This removes the silent cast, makes the actual dependency explicit, and eliminates any risk of `InvalidCastException`.

---
_Filed by daily arch-review routine on 2026-06-06._