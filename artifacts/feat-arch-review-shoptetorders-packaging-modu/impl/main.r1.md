---

# Implementation: Decouple Packaging from ShoptetOrdersSettings

## What was implemented

Removed the direct dependency from `Application.Features.Packaging` on `ShoptetOrdersSettings` by moving packing-eligibility computation and the "mark as packed" status transition behind the existing `IPackingOrderClient` and `IEshopOrderClient` contracts. An architecture enforcement test was added to pin the boundary in place.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs` — added `bool IsEligibleForPacking { get; set; }` to `PackingOrder`, tightened XML doc on `StatusId`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — added `Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default)`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — injected `IOptions<ShoptetOrdersSettings>`, implemented `MarkAsPackedAsync` as delegate to `UpdateStatusAsync`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` — injected `IOptions<ShoptetOrdersSettings>`, sets `IsEligibleForPacking = statusId == _orderSettings.PackingStateId`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs` — dropped `IOptions<ShoptetOrdersSettings>`, reads `order.IsEligibleForPacking`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — dropped `ShoptetOrdersSettings`, reads `order.IsEligibleForPacking`, calls `MarkAsPackedAsync`
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs` — removed `IOptions<ShoptetOrdersSettings>` setup, sets `IsEligibleForPacking` explicitly
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` — removed `ShoptetOrdersSettings` setup, sets `IsEligibleForPacking` explicitly, asserts `MarkAsPackedAsync`
- `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs` — updated `MakeOrder` and constructor to remove `ShoptetOrdersSettings`
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added `PackagingShoptetOrdersAllowlist` and `Packaging -> ShoptetOrders` rule
- 5 adapter test files — updated `new ShoptetOrderClient(http)` → `new ShoptetOrderClient(http, Options.Create(new ShoptetOrdersSettings()))` to fix broken constructor

## Tests

- `GetPackingOrderHandlerTests` — 5 tests pass (uses `IsEligibleForPacking` directly, no `IOptions<ShoptetOrdersSettings>`)
- `ScanPackingOrderHandlerTests` — 13 tests pass (verifies `MarkAsPackedAsync`, no `ShoptetOrdersSettings`)
- `ScanPackingOrderHandlerPackagePersistenceTests` — 3 tests pass
- `ModuleBoundariesTests` — 20 theory rows pass including the new `Packaging -> ShoptetOrders` rule
- Full suite: 4,863 tests pass, 0 failures

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --no-build
git grep -n -e "PackingStateId" -e "PackedStateId" -e "ShoptetOrdersSettings" -- backend/src
# Last command must show no matches under backend/src/Anela.Heblo.Application/Features/Packaging/
```

## Notes

- One deviation from the plan: the `ShoptetApiAdapterServiceCollectionExtensions.cs` DI registration did **not** need changes since `AddHttpClient<ShoptetOrderClient>` resolves parameters from DI automatically. However, 5 test files that manually `new`-ed `ShoptetOrderClient` needed the extra `IOptions<ShoptetOrdersSettings>` parameter — these were updated.
- The `PackagingShoptetOrdersAllowlist` in the architecture test was extended beyond the plan's 4 entries to cover `ScanOrderData -> PackingOrderItem` and `ResetOrderShipmentHandler -> IPackingOrderClient/PackingOrder/PackingOrderItem` (both pre-existing legitimate uses of the contract surface that were not covered by the plan's initial allowlist).
- Unicode typographic quotation marks in Czech string literals (`„Balí se"`) required Python-based file writing/fixing since the Write tool substitutes ASCII `"` for the typographic `"` (U+201D), causing C# parse errors.

## PR Summary

Removes the direct coupling from `Application.Features.Packaging` onto `ShoptetOrdersSettings` — a configuration class owned by the `ShoptetOrders` module. Two concrete changes land: (1) `PackingOrder` now carries a precomputed `IsEligibleForPacking` flag set by `ShoptetApiPackingOrderClient` using `PackingStateId`, so neither handler needs to know that constant; (2) `IEshopOrderClient` gets a `MarkAsPackedAsync` method implemented by `ShoptetOrderClient` via the existing `UpdateStatusAsync` path, hiding `PackedStateId` from `ScanPackingOrderHandler`. Both handlers are simplified accordingly. An architecture enforcement test (`ModuleBoundariesTests`, `Packaging -> ShoptetOrders` rule) pins the boundary so it cannot silently re-erode.

### Changes
- `IPackingOrderClient.cs` — `PackingOrder` gains `IsEligibleForPacking`; `StatusId` XML doc steers readers away from re-deriving eligibility
- `IEshopOrderClient.cs` — new `MarkAsPackedAsync` method on the contract
- `ShoptetOrderClient.cs` — injects `IOptions<ShoptetOrdersSettings>`, implements `MarkAsPackedAsync`
- `ShoptetApiPackingOrderClient.cs` — injects `IOptions<ShoptetOrdersSettings>`, sets `IsEligibleForPacking` on returned DTO
- `GetPackingOrderHandler.cs` — drops `IOptions<ShoptetOrdersSettings>`, reads `order.IsEligibleForPacking`
- `ScanPackingOrderHandler.cs` — drops `ShoptetOrdersSettings`, reads `order.IsEligibleForPacking`, calls `MarkAsPackedAsync`
- Test files (4 handler tests, 1 architecture) — updated to match new contracts; 5 adapter tests fixed for `ShoptetOrderClient` constructor change

## Status
DONE