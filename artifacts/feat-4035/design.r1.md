# Design: Replace ApplyEnrichment call in ShoptetApiPackingOrderClient with direct cooling enrichment

## Component Design

### `ShoptetApiPackingOrderClient.GetPackingOrderAsync` (modified)
- Location: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`
- Responsibility: unchanged — loads a Shoptet order and produces the `PackingOrder` view for the packing screen.
- Change: the block that currently calls `ShoptetApiExpeditionListSource.ApplyEnrichment(order.Items, new Dictionary<string, decimal>(), new Dictionary<string, string>(), coolingByCode)` is replaced with a private-scope `foreach` loop over `order.Items` that sets `item.Cooling` from `coolingByCode` when the item's `ProductCode` is present, and otherwise leaves it unchanged:

```csharp
foreach (var item in order.Items)
{
    if (coolingByCode.TryGetValue(item.ProductCode, out var cooling))
        item.Cooling = cooling;
}
```

- Interface: no change to `IPackingOrderClient` / `IPackingOrderCountSource`, no change to method signature, no change to the constructor or injected dependencies (`IShoptetExpeditionOrderSource`, `IPackingProductSource`, `IPackingCarrierCoolingSource`, `ILogger<ShoptetApiPackingOrderClient>`, `ShoptetApiSettings`, `ShoptetOrdersSettings`).

### `ShoptetApiExpeditionListSource.ApplyEnrichment` (unchanged)
- Location: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
- Responsibility: unchanged — still applies stock, location, cooling, and (optional) price enrichment for the picking-list path.
- Still called from `PickingListBatchProcessor.cs:89` with real, non-empty `stockByCode`, `locationByCode`, `coolingByCode`, and `priceByCode`. This call site is out of scope and untouched.

## Data Schemas

No schema changes — no DTOs, database entities, or event payloads are added, removed, or modified. For reference, the two types involved in the change keep their existing shapes:

- `ExpeditionOrderItem` (`Expedition/ExpeditionProtocolData.cs`) — internal mapping type; only its pre-existing `Cooling` property is written by the new loop, exactly as it was written by `ApplyEnrichment` before.
- `PackingOrder` / `PackingOrderItem` (`Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs`) — internal contract returned by `GetPackingOrderAsync`; unchanged shape. `PackingOrder.IsCooled` continues to be derived from `ExpeditionOrder.IsCooled`, which continues to read each item's `Cooling` value — now set by the in-line loop instead of by `ApplyEnrichment`.
