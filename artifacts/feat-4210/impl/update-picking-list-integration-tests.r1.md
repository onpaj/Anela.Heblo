# Implementation: update-picking-list-integration-tests

## What was implemented

Repointed `PickingListIntegrationTests` to use the new `IShoptetOrderTestClient`
for the one relocated method it calls (`GetRecentOrdersAsync`), while keeping
`IEshopOrderClient` (`_orderClient`) for `UpdateStatusAsync`, which stays on the
production interface. This makes the test class a mixed-usage consumer,
matching the pattern already used in `BlockOrderProcessingIntegrationTests` and
`ShoptetTestEnvironmentHydrationTests`.

## Files created/modified

- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`
  - Added `using Anela.Heblo.Adapters.ShoptetApi.Orders;`
  - Added `private readonly IShoptetOrderTestClient _testOrderClient;` field
  - Resolved `_testOrderClient` from DI in the constructor
  - `PrintPickingList_ProducesPdfs_ForRecentOrders`: `GetRecentOrdersAsync` now
    goes through `_testOrderClient`; the two `_orderClient.UpdateStatusAsync(...)`
    calls (lines 80 and 117) are unchanged

## Tests

No new tests added — this task repoints the existing
`PrintPickingList_ProducesPdfs_ForRecentOrders` integration test to the new
test-only client interface.

## How to verify

```bash
cd backend
dotnet build test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~PickingListIntegrationTests"
```

## Notes

- `dotnet build` on the full test project succeeded (0 errors).
- `dotnet test --filter "FullyQualifiedName~PickingListIntegrationTests"` fails
  with `System.InvalidOperationException: Integration test must not run
  against live environment. Set Shoptet:IsTestEnvironment=true in test
  appsettings.json`, thrown from `ShoptetTestGuard.Assert` at line 59, before
  any HTTP call. This is exactly outcome (a) documented as expected in the
  task context: this sandboxed environment has no `Shoptet:IsTestEnvironment`
  configuration/credentials, so the guard trips immediately. Critically, the
  DI container successfully resolved `IShoptetOrderTestClient` in the
  constructor and the rewritten `_testOrderClient.GetRecentOrdersAsync(...)`
  call site compiles — the failure is the pre-existing guard behavior, not a
  regression introduced by this task.

## PR Summary

Repointed `PickingListIntegrationTests` to use `IShoptetOrderTestClient` for
`GetRecentOrdersAsync`, keeping `IEshopOrderClient` for `UpdateStatusAsync`,
consistent with the `IEshopOrderClient` shrink work elsewhere in this feature.
This is the last of the three integration test files that needed repointing.

### Changes
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` — added `_testOrderClient: IShoptetOrderTestClient` field and repointed the `GetRecentOrdersAsync` call site

## Status
DONE
