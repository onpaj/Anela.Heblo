# add-boundary-tests — impl r1

## What was added

Two new `ModuleBoundaryRule` entries were added to `ModuleBoundariesTests.Rules()` in
`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`:

- **ShoptetApi Adapters -> Catalog** — inspects `Anela.Heblo.Adapters.ShoptetApi`, forbids direct
  references to `Anela.Heblo.Domain.Features.Catalog`, `Anela.Heblo.Application.Features.Catalog`,
  and `Anela.Heblo.Persistence.Catalog`.
- **ShoptetApi Adapters -> Logistics** — inspects `Anela.Heblo.Adapters.ShoptetApi`, forbids direct
  references to `Anela.Heblo.Domain.Features.Logistics`, `Anela.Heblo.Application.Features.Logistics`,
  and `Anela.Heblo.Persistence.Logistics`.

Both rules use `InspectedAssembly: "Anela.Heblo.Adapters.ShoptetApi"`.

## Allowlist entries beyond the initial set

The initial allowlists provided in the task specification were insufficient; the test run revealed
additional violations from types not anticipated in the spec. The following entries were added:

### ShoptetApiAdaptersCatalogAllowlist (additions beyond spec)

- `PickingListBatchProcessor -> ICatalogRepository` — retains `ICatalogRepository` injection;
  out of scope.
- `PickingListBatchProcessor -> CatalogAggregate` — needed to cover the compiler-generated async
  state machine `<EnrichBatchAsync>d__8`, which captures `CatalogAggregate` in its fields. The
  declaring-type check resolves nested types against this parent entry.
- `ShoptetStockClient -> EshopStock` — `ShoptetStockClient` implements `IEshopStockClient` and
  its `ListAsync` method returns `IAsyncEnumerable<EshopStock>` directly; the adapter is the
  mapping boundary.
- `ShoptetStockClient -> EshopStockSupply` — same pattern for `GetSupplyAsync`.
- `HeurekaProductFeedClient -> ProductEshopUrl` — implements `IProductEshopUrlSource`; `GetAllAsync`
  returns `IReadOnlyList<ProductEshopUrl>` directly.

### ShoptetApiAdaptersLogisticsAllowlist (additions beyond spec)

- `ShippingMethodCatalog -> Carriers` — `ShippingMethodCatalog` exposes `Carriers` directly in
  method return types and parameters (`GetAvailableDeliveryOptions`, `GetShippingCodesForCarrier`,
  `ResolveCarrier`). Covers compiler-generated `<>c` and `<>c__DisplayClass1_0` nested types via
  the declaring-type check.
- `ShippingMethodCatalog -> DeliveryHandling` — same class, same methods.
- `ShoptetApiExpeditionListSource -> IGiftSettingRepository` — retains `IGiftSettingRepository`
  constructor injection; out of scope.
- `ShoptetApiExpeditionListSource -> GiftSetting` — `GiftSetting` flows through `BatchAndFlushAsync`
  and `ResolveGiftBadge`; compiler-generated state machines covered by declaring-type check.
- `ShoptetApiExpeditionListSource -> PrintPickingListResult` — implements `IPickingListSource`;
  `CreatePickingList` return type.
- `ShoptetApiExpeditionListSource -> PrintPickingListRequest` — implements `IPickingListSource`;
  `CreatePickingList` parameter type.

## Test result

All 26 ModuleBoundariesTests pass (0 failures).

```
Passed! - Failed: 0, Passed: 26, Skipped: 0, Total: 26, Duration: 406 ms
```

## Commit

`262267d feat(feat-3260): add-boundary-tests @claude`
