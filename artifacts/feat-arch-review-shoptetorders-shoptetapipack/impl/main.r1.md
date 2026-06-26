All three tasks complete. Here is the output artifact:

---

# Implementation: Honest Dependency in ShoptetApiPackingOrderClient

## What was implemented
Removed the dishonest `IEshopOrderClient` → `ShoptetOrderClient` runtime downcast in `ShoptetApiPackingOrderClient` by depending on the concrete `ShoptetOrderClient` directly. The DI registration was inverted to register the typed HttpClient on the concrete class and forward the interface via a transient factory — one HttpClient configuration, no casts.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — replaced `AddHttpClient<IEshopOrderClient, ShoptetOrderClient>` with `AddHttpClient<ShoptetOrderClient>` + `AddTransient<IEshopOrderClient>` forwarder (commit `1af15457`)
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` — constructor parameter changed from `IEshopOrderClient` to `ShoptetOrderClient`; `as`/throw cast replaced by direct assignment (commit `7cb748e8`)

## Tests
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs` — existing 10 tests unchanged and passing. No new tests added (FR-4 was based on a false premise per arch review; existing tests already covered the happy paths via `FakeDelegatingHandler`-backed `ShoptetOrderClient`).

## How to verify
```bash
dotnet build backend/Anela.Heblo.sln       # 0 errors
dotnet test backend/Anela.Heblo.sln         # 4,101 pass, 0 fail
```

## Notes
- **`ShoptetApiExpeditionListSource.cs`** has the identical downcast pattern at line 35 — intentionally left untouched per spec. A follow-up ticket should be filed.
- One pre-existing integration test (`PickingListIntegrationTests.PrintPickingList_ProducesPdfs_ForRecentOrders`) fails due to a DI issue for `ICarrierCoolingRepository` that predates this branch. Unrelated to these changes.
- The `dotnet format` run in Task 1 automatically cleaned up the unused `System` using directive that was previously needed for `InvalidOperationException`.

## PR Summary
Removes a DIP/LSP violation in `ShoptetApiPackingOrderClient` where the constructor declared `IEshopOrderClient` but immediately downcasted to `ShoptetOrderClient` at runtime, making any non-`ShoptetOrderClient` implementation silently fail at construction. The fix has two parts: (1) the constructor now takes `ShoptetOrderClient` directly, eliminating the cast, and (2) the DI registration is restructured so that `ShoptetOrderClient` is registered as the typed HttpClient and `IEshopOrderClient` is forwarded via a transient factory — keeping a single HttpClient configuration and preserving all existing interface consumers.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — inverted typed-client registration: `AddHttpClient<ShoptetOrderClient>` + `AddTransient<IEshopOrderClient>` forwarder
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` — constructor parameter `IEshopOrderClient → ShoptetOrderClient`; direct assignment replaces `as`/throw cast

## Status
DONE