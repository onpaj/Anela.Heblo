# update-packing-order-client — impl artifact

## Task

Decouple `ShoptetApiPackingOrderClient` from `ICatalogRepository` and
`ICarrierCoolingRepository`. Replace with consumer-owned contracts
`IPackingProductSource` and `IPackingCarrierCoolingSource` (defined in task 1).

## Changes

### ShoptetApiExpeditionListSource.cs

Added a new overload of `ResolveCarrierCooling` alongside the existing one:

```
internal static Cooling ResolveCarrierCooling(
    string shippingGuid,
    IReadOnlyDictionary<(string CarrierName, string DeliveryHandlingName), Cooling> matrix)
```

The new overload takes string-keyed tuples matching `PackingCarrierCoolingSetting.CarrierName`
and `.DeliveryHandlingName`. It converts the `Carriers` and `DeliveryHandling` enum values to
their string representations via `.ToString()` before looking up in the matrix. The original
enum-keyed overload is preserved (still used by the expedition list picking flow).

### ShoptetApiPackingOrderClient.cs

Full replacement:
- Constructor now takes `IPackingProductSource` and `IPackingCarrierCoolingSource` instead of
  `ICatalogRepository` and `ICarrierCoolingRepository`.
- `GetPackingOrderAsync`: calls `_carrierCoolingSource.GetAllAsync()` and builds a
  `(CarrierName, DeliveryHandlingName) -> Cooling` dictionary for the new overload.
- Calls `_productSource.GetByCodesAsync()` to get `PackingProductInfo` per product code.
  Weight now comes directly from `info.WeightGrams` (int?); no GrossWeight/NetWeight
  conversion logic needed — that concern now lives in the catalog adapter (task 2).
- Removed usings: `Anela.Heblo.Domain.Features.Catalog`, `Anela.Heblo.Domain.Features.Logistics`.
- Added using: `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`.

### ShoptetApiPackingOrderClientTests.cs

- Removed all references to `ICatalogRepository`, `ICarrierCoolingRepository`,
  `CatalogAggregate`, `CarrierCoolingSetting`, domain namespaces.
- Added `using Anela.Heblo.Application.Features.ShoptetOrders.Contracts`.
- Replaced `CatalogWith`/`CoolingWith`/`BuildSut` helpers with `ProductSourceWith`,
  `CoolingSourceWith`, and updated `BuildSut`.
- Weight tests: the two separate GrossWeight/NetWeight fallback tests were replaced with:
  - `GetPackingOrderAsync_PopulatesWeightGramsFromCatalog` — verifies direct `WeightGrams` pass-through.
  - `GetPackingOrderAsync_FallsBackToDefaultWeightWhenCatalogHasNoWeight` — null `WeightGrams`.
  - `GetPackingOrderAsync_FallsBackToDefaultWeightWhenProductNotInCatalog` — product absent from source.
  Total test count remains 12 (one count test replaced two old weight tests with three new ones,
  net +1; combined with the removal of one now-redundant test).

## Test results

```
Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12, Duration: 66 ms
```

All 12 tests in `ShoptetApiPackingOrderClientTests` pass. Build: 0 errors, 147 warnings
(pre-existing; none introduced by this task).
