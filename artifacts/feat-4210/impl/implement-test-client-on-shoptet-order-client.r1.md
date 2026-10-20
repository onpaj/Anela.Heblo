# Implementation: implement-test-client-on-shoptet-order-client

## What was implemented
Added `IShoptetOrderTestClient` to the list of interfaces implemented by `ShoptetOrderClient`, so the existing class now also satisfies the new test-client contract introduced by the prior `create-shoptet-order-test-client-interface` task, alongside `IEshopOrderClient` and `IShoptetExpeditionOrderSource`.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — class declaration changed from `public class ShoptetOrderClient : IEshopOrderClient, IShoptetExpeditionOrderSource` to `public class ShoptetOrderClient : IEshopOrderClient, IShoptetOrderTestClient, IShoptetExpeditionOrderSource`. No method bodies were touched — `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`, `CreateOrderAsync`, and `DeleteOrderAsync` already had signatures matching `IShoptetOrderTestClient` verbatim (confirmed by diffing against `IShoptetOrderTestClient.cs`).

## Tests
No new tests required by this task — it is a pure interface-declaration change with no new behavior. Existing test suites (build/integration) exercise `ShoptetOrderClient` unchanged.

## How to verify
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Result: `Build succeeded.` 0 Errors (140 pre-existing warnings unrelated to this change, none newly introduced).

## Notes
No deviations from the task context. Signatures for all four relocated methods (`GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`, `CreateOrderAsync`, `DeleteOrderAsync`) were verified character-for-character against `IShoptetOrderTestClient.cs` before editing, confirming no default-parameter or `CancellationToken` mismatches — the build succeeded on the first attempt with no interface-member errors.

## PR Summary
`ShoptetOrderClient` now implements `IShoptetOrderTestClient` in addition to `IEshopOrderClient` and `IShoptetExpeditionOrderSource`. This is the third step of the `IEshopOrderClient` shrink effort: the production Shoptet adapter now exposes the test-only order lifecycle operations (`CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`) through the narrower, integration-test-scoped interface, without any change to its existing behavior or the application-layer `IEshopOrderClient` contract.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — added `IShoptetOrderTestClient` to the implemented-interface list

## Status
DONE
