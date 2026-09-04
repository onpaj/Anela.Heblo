## Module
ShoptetOrders

## Finding
`ShoptetApiPackingOrderClient.GetPackingOrderAsync` calls the static `ShoptetApiExpeditionListSource.ApplyEnrichment` with two always-empty dictionaries:

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs, lines 76–80
ShoptetApiExpeditionListSource.ApplyEnrichment(
    order.Items,
    new Dictionary<string, decimal>(),   // stockByCode — always empty
    new Dictionary<string, string>(),    // locationByCode — always empty
    coolingByCode);
```

The method signature is:
```csharp
internal static void ApplyEnrichment(
    IEnumerable<ExpeditionOrderItem> items,
    Dictionary<string, decimal> stockByCode,
    Dictionary<string, string> locationByCode,
    Dictionary<string, Cooling> coolingByCode,
    Dictionary<string, decimal>? priceByCode = null)
```

The sole purpose of this call is to apply `coolingByCode` to the items — the stock and location parameters serve no function here.

## Why it matters
Three concrete costs:

1. **Intent is obscured**: two empty dictionaries look like incomplete implementation or a bug to any reader. There is no comment explaining why they are empty.
2. **Unnecessary allocation**: two `Dictionary` objects are allocated on every call to `GetPackingOrderAsync`, which runs on every packing-screen load.
3. **Coupling to unrelated logic**: `ShoptetApiPackingOrderClient` now depends statically on `ShoptetApiExpeditionListSource`'s enrichment method (which was built for the expedition-list path). If `ApplyEnrichment`'s signature or semantics change to serve expedition-list needs, the packing path is silently affected.

KISS principle: the method does three things (stock, location, cooling); this call site only needs one.

## Suggested fix
Replace the `ApplyEnrichment` call with a direct in-line application of just the cooling enrichment (the only thing that is actually needed):

```csharp
foreach (var item in order.Items)
{
    if (coolingByCode.TryGetValue(item.ProductCode, out var cooling))
        item.Cooling = cooling;
}
```

This is the smallest change that removes the confusion, eliminates the empty allocations, and severs the speculative static dependency on the expedition-list source.

---
_Filed by daily arch-review routine on 2026-09-02._
