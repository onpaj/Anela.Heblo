## Module
ShoptetOrders

## Finding
`ShoptetApiPackingOrderClient.GetPackingOrderAsync` (lines 99–118 of `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`) explicitly derives and populates three shipping-address fields on the returned `PackingOrder`:

```csharp
ShippingStreet = shippingStreet,
ShippingCity   = NormalizeAddressField(deliveryAddress?.City),
ShippingZip    = NormalizeAddressField(deliveryAddress?.Zip),
```

`GetPackingOrderHandler.Handle` (lines 41–57 of `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs`) maps all other `PackingOrder` fields into `GetPackingOrderResponse` but never maps these three. `GetPackingOrderResponse` has no `ShippingStreet`, `ShippingCity`, or `ShippingZip` properties, confirmed by the generated TypeScript client (`GetPackingOrderResponse` in `frontend/src/api/generated/api-client.ts` lines 35061–35120).

The two unit tests in `GetPackingOrderHandlerTests` do not set or assert shipping address fields, so the gap is not caught by the test suite.

## Why it matters
The adapter performs an address-normalization pass (`CombineStreetAndHouseNumber`, `NormalizeAddressField`) and that computed data is silently discarded at the handler boundary. Any consumer of `GET /api/shoptet-orders/{code}/packing` cannot display the delivery address even though the backend already has it. If a future UI or integration adds a shipping-address display to this screen it will need to re-derive something the adapter already provides.

## Suggested fix
Add three optional properties to `GetPackingOrderResponse`:

```csharp
public string? ShippingStreet { get; set; }
public string? ShippingCity   { get; set; }
public string? ShippingZip    { get; set; }
```

Map them in `GetPackingOrderHandler.Handle`:

```csharp
ShippingStreet = order.ShippingStreet,
ShippingCity   = order.ShippingCity,
ShippingZip    = order.ShippingZip,
```

Add assertions for these fields in at least one `GetPackingOrderHandlerTests` case.

---
_Filed by daily arch-review routine on 2026-06-05._