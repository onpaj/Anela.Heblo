## Module
ShoptetOrders

## Finding
`CompleteDeliveredOrdersJob` (`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`, line 7 and lines 15–16) directly injects `IShipmentClient` from the `ShipmentLabels` module:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
...
private readonly IShipmentClient _shipmentClient;
```

The job uses only one of the six methods on that interface: `HasDeliveredShipmentAsync` (line 99). The full `IShipmentClient` surface includes label fetching, tracking number retrieval, shipment creation and cancellation — none of which `CompleteDeliveredOrdersJob` needs.

The established cross-module pattern in this codebase (documented in `development_guidelines.md`, and demonstrated by `IPackingCarrierCoolingSource` and `IPackingProductSource` in the same module) requires the **consumer to own the contract** and the provider to implement an adapter. Here `ShoptetOrders` is the consumer but it consumes `ShipmentLabels`' interface directly, inverting the correct ownership direction.

## Why it matters
- **Module coupling**: `ShoptetOrders` has a compile-time dependency on `ShipmentLabels`' internal API surface. Any change to `IShipmentClient` made for `ShipmentLabels`' own reasons is forced onto `ShoptetOrders`.
- **ISP violation**: the job depends on the complete six-method `IShipmentClient` when it needs only `HasDeliveredShipmentAsync`. Tests must stub unrelated methods.
- **Pattern inconsistency**: `IPackingCarrierCoolingSource` and `IPackingProductSource` in the same module follow the correct consumer-owned-contract pattern; `IShipmentClient` does not, creating two conflicting conventions in the same feature.

## Suggested fix
1. Add a narrow interface to `ShoptetOrders/Contracts/`:
   ```csharp
   // backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs
   public interface IShipmentDeliveryChecker
   {
       Task HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default);
   }
   ```
2. Add an adapter in `ShipmentLabels` (or in the Shoptet adapter project) that delegates to `IShipmentClient.HasDeliveredShipmentAsync`.
3. Replace the `IShipmentClient` injection in `CompleteDeliveredOrdersJob` with `IShipmentDeliveryChecker`.
4. Register the binding in `ShipmentLabelsModule` or `ShoptetApiAdapterServiceCollectionExtensions`.

No change to the underlying logic is needed.

---
_Filed by daily arch-review routine on 2026-07-20._
