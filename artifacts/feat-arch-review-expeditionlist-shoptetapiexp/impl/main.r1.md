---

# Implementation: Replace Misleading Interface Injection in ShoptetApiExpeditionListSource

## What was implemented

Constructor signature refactor in `ShoptetApiExpeditionListSource`: replaced the misleading `IEshopOrderClient` parameter (immediately downcast to `ShoptetOrderClient`) with the concrete `ShoptetOrderClient` directly. Removed the now-obsolete downcast and the stale comment explaining why the cast was "safe". `dotnet format` ran cleanly with no further changes — the `using Anela.Heblo.Application.Features.ShoptetOrders;` import was already unused and was removed by the formatter.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — constructor parameter type changed (`IEshopOrderClient` → `ShoptetOrderClient`), downcast removed, stale comment removed, unused using removed by formatter

## Tests
- Targeted: `dotnet test --filter "FullyQualifiedName~ShoptetApiExpeditionListSource"` — all 4 tests passed
- Full suite: `dotnet test backend/Anela.Heblo.sln` — 4592 tests passed (38 pre-existing Docker-related failures unrelated to this change)

## How to verify
```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ShoptetApiExpeditionListSource"
```

## Notes

No DI changes required — `ShoptetOrderClient` was already registered as a concrete type via `AddHttpClient<ShoptetOrderClient>` and the existing tests already supplied a real `ShoptetOrderClient`. The change is byte-identical at runtime (the DI factory always returned `ShoptetOrderClient`), so production behavior is unchanged.

## PR Summary

Replaces the misleading `IEshopOrderClient` constructor parameter in `ShoptetApiExpeditionListSource` with the concrete `ShoptetOrderClient` it has always required. The class calls `GetOrdersByStatusAsync`, `GetExpeditionOrderDetailAsync`, and `SetAdditionalFieldAsync` — three methods that exist only on the concrete client — so the interface parameter provided no decoupling benefit and introduced a silent `InvalidCastException` risk. Both types live in the same adapter assembly, making concrete injection the appropriate Clean Architecture choice, consistent with the `ShoptetApiPackingOrderClient` refactor shipped in May 2026.

DI registration and all existing tests already supply a concrete `ShoptetOrderClient`; no edits to either were required. The stale comment explaining the cast has been removed.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — constructor parameter `IEshopOrderClient` → `ShoptetOrderClient`, downcast removed, stale comment removed, unused `using` cleaned up by `dotnet format`

## Status
DONE