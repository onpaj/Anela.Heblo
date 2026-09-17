# Implementation: register-test-client-in-di

## What was implemented
Registered `IShoptetOrderTestClient` in DI so `ShoptetOrderClient` is resolvable through that interface, alongside its existing `IEshopOrderClient` and `IShoptetExpeditionOrderSource` registrations.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — added `services.AddTransient<IShoptetOrderTestClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());` immediately after the existing `IEshopOrderClient` registration (line 52), before the `IShoptetExpeditionOrderSource` line. No `using` addition needed — `IShoptetOrderTestClient` lives in `Anela.Heblo.Adapters.ShoptetApi.Orders`, already imported by this file.

## Tests
No new tests required by this task; it is a pure DI wiring change with no new logic to unit test.

## How to verify
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Build succeeded (0 Errors, 140 pre-existing warnings unrelated to this change).

## Notes
Verified `IShoptetOrderTestClient` interface exists at `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs` before wiring it up. Change matches the task context exactly.

## PR Summary
Registered the new `IShoptetOrderTestClient` interface (introduced by an earlier task in this feature) in the adapter's DI container, resolving it from the same `ShoptetOrderClient` instance used for `IEshopOrderClient` and `IShoptetExpeditionOrderSource`. This makes the test-only lifecycle interface consumable via DI ahead of the tasks that update integration tests to use it.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — added `IShoptetOrderTestClient` DI registration

## Status
DONE
