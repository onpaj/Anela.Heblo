## Module
ShoptetOrders

## Finding
`PackingOrderItem` is defined in `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs` (lines 48–60) as part of the `IPackingOrderClient` contract — an Application-layer service interface. The class carries an explicit comment:

> "A single line on the packing screen. **Also serialized in the API response.**"

`GetPackingOrderResponse` (line 25 of `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs`) declares:

```csharp
public List<PackingOrderItem> Items { get; set; } = new();
```

This means the same type serves two distinct roles:

1. Internal contract between `IPackingOrderClient` implementations and the application handler.
2. External API DTO serialized to JSON and exposed to clients (confirmed in generated TypeScript client `PackingOrderItem` at `frontend/src/api/generated/api-client.ts` line 30113, including the `weightGrams` field).

The `WeightGrams` property is an example of leakage: it was added to support backend packing-weight calculations but is now part of the public API surface. The frontend `PackingOrderItem` interface in `useScanPackingOrder.ts` (line 12) does **not** include a weight field — it has its own separately maintained type — which means the two clients of packing-order items are already diverging.

## Why it matters
Per `development_guidelines.md`: DTOs live in `contracts/` of the specific module and must not be shared. The double role violates this rule: any field added to the internal contract automatically becomes part of the public API, and any change to the public API forces changes to the internal contract. The types cannot evolve independently.

## Suggested fix
Introduce a dedicated `PackingOrderItemDto` in `UseCases/GetPackingOrder/` (or a `Contracts/` folder if the module grows):

```csharp
public class PackingOrderItemDto
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? ImageUrl { get; set; }
    public string? SetName { get; set; }
    // WeightGrams is intentionally omitted — internal detail only
}
```

Change `GetPackingOrderResponse.Items` to `List<PackingOrderItemDto>` and map in the handler. Remove the "Also serialized in the API response" comment from `PackingOrderItem`.

---
_Filed by daily arch-review routine on 2026-06-05._