# Implementation: create-shoptet-order-test-client-interface

## What was implemented
Created a new adapter-layer interface `IShoptetOrderTestClient` in the `Anela.Heblo.Adapters.ShoptetApi` project, declaring the four test-only lifecycle methods (`CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`) that are currently only called from integration tests, matching the exact signature given in the task context.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs` — new interface with the four test-only methods, referencing `CreateEshopOrderRequest`/`EshopOrderSummary` DTOs from `Anela.Heblo.Application.Features.ShoptetOrders`, matching the existing dependency direction already used by `ShoptetOrderClient.cs` in the same project.

## Tests
No new tests required by this task — it only adds an interface definition. The interface is not yet referenced anywhere (implementation and DI registration are separate later tasks in this plan).

## How to verify
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Result: `Build succeeded.` with 0 errors (confirmed).

## Notes
No deviations from the task spec. The file content matches the task context's exact-content requirement verbatim.

## PR Summary
Added `IShoptetOrderTestClient`, a new adapter-layer interface in `Anela.Heblo.Adapters.ShoptetApi`, declaring the four Shoptet order lifecycle methods (`CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`) that exist only to support integration-test setup/teardown. This is the first step of splitting these test-only methods out of the Application-layer `IEshopOrderClient` interface (issue #4210) — later tasks will have `ShoptetOrderClient` implement this interface, shrink `IEshopOrderClient` to the 7 methods Application handlers actually use, and update the integration tests to depend on the new interface directly.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs` — new file, adapter-layer interface for test-only order lifecycle operations

## Status
DONE
