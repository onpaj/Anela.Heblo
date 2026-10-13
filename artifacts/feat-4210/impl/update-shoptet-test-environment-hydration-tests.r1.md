# Implementation: update-shoptet-test-environment-hydration-tests

## What was implemented

Repointed `ShoptetTestEnvironmentHydrationTests` to use the new
`IShoptetOrderTestClient` for the order-lifecycle calls that moved off
`IEshopOrderClient` (`CreateOrderAsync`, `DeleteOrderAsync`,
`ListByExternalCodePrefixAsync`), while keeping `IEshopOrderClient` for
`UpdateStatusAsync`, which stays on the production interface. This makes
the test class a mixed-usage consumer, matching the pattern already used
in `BlockOrderProcessingIntegrationTests`.

## Files created/modified

- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs`
  - Added `using Anela.Heblo.Adapters.ShoptetApi.Orders;`
  - Added `private readonly IShoptetOrderTestClient _testClient;` field
  - Resolved `_testClient` from DI in the constructor
  - `HydrateTestEnvironment`: `ListByExternalCodePrefixAsync` and
    `CreateOrderAsync` now go through `_testClient`; `UpdateStatusAsync`
    calls unchanged (still `_client`)
  - `PurgeTestOrders`: `ListByExternalCodePrefixAsync` and
    `DeleteOrderAsync` now go through `_testClient`; `UpdateStatusAsync`
    call unchanged (still `_client`)

## Tests

No new tests added — this task repoints existing integration tests
(`Guard_*`, `HydrateTestEnvironment`, `PurgeTestOrders`) in
`ShoptetTestEnvironmentHydrationTests.cs` to the new test-only client
interface.

## How to verify

```bash
cd backend
dotnet build test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetTestEnvironmentHydrationTests"
```

## Notes

The full test project currently fails to build with 6 errors, all in
`Integration/PickingListIntegrationTests.cs` (`GetRecentOrdersAsync` no
longer exists on `IEshopOrderClient`, removed by the earlier
`shrink-ieshoporderclient-interface` task). None of these errors
reference `ShoptetTestEnvironmentHydrationTests.cs` — confirmed by
grepping the full build output for the class name, which returns no
matches. `PickingListIntegrationTests.cs` is explicitly out of scope for
this task; it has its own task context file
(`task-context/update-picking-list-integration-tests.md`) and is the
next pending task in `state.json`. Once that task lands, the whole test
project should build and this class's tests can run end-to-end
(`Guard_*` run for real today; `HydrateTestEnvironment`/`PurgeTestOrders`
early-return because `SHOPTET_HYDRATE` is unset in this environment,
which is the expected/documented behavior per the task's own acceptance
criteria).

## PR Summary

Repointed `ShoptetTestEnvironmentHydrationTests` to use
`IShoptetOrderTestClient` for `CreateOrderAsync`, `DeleteOrderAsync`, and
`ListByExternalCodePrefixAsync`, keeping `IEshopOrderClient` for
`UpdateStatusAsync`, consistent with the `IEshopOrderClient` shrink work
elsewhere in this feature.

### Changes
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs` — added `_testClient: IShoptetOrderTestClient` field and repointed the relocated call sites

## Status
DONE
