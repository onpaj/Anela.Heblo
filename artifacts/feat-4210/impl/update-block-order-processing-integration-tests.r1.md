# Implementation: update-block-order-processing-integration-tests

## What was implemented
Repointed `BlockOrderProcessingIntegrationTests` to use the new `IShoptetOrderTestClient` for the two test-housekeeping methods it calls (`CreateOrderAsync`, `DeleteOrderAsync`), while leaving the three methods that stay on `IEshopOrderClient` (`UpdateStatusAsync`, `UpdateEshopRemarkAsync`, `GetEshopRemarkAsync`) untouched on the existing `_client` field.

## Files created/modified
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/BlockOrderProcessingIntegrationTests.cs`
  - Added `using Anela.Heblo.Adapters.ShoptetApi.Orders;`
  - Added a new `private readonly IShoptetOrderTestClient _testClient;` field, resolved in the constructor via `fixture.ServiceProvider.GetRequiredService<IShoptetOrderTestClient>()`
  - Repointed the 4 `CreateOrderAsync`/`DeleteOrderAsync` call sites (2 in `BlockOrder_PreservesExistingEshopRemark_AndAppendsOnNewLine`, 2 in `RunTest`) from `_client` to `_testClient`
  - Left the `UpdateStatusAsync`, `UpdateEshopRemarkAsync`, `GetEshopRemarkAsync` call sites on `_client` exactly as they were

## Tests
No new tests — this is a mechanical rewire of an existing test class's dependency on `IShoptetOrderTestClient`. No behavior change to the tests themselves.

## How to verify
```bash
cd backend && dotnet build test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```

At HEAD (before this change) the test project fails with **15** compile errors: 4 in `BlockOrderProcessingIntegrationTests.cs` (this task's file) plus 11 in `ShoptetTestEnvironmentHydrationTests.cs`/`PickingListIntegrationTests.cs` (owned by the still-pending `update-shoptet-test-environment-hydration-tests` and `update-picking-list-integration-tests` tasks). After this change, the same build shows exactly **11** errors — all in the two sibling files, none in `BlockOrderProcessingIntegrationTests.cs`. This confirms the 4 errors this task owned are fixed; I verified this by diffing a build at HEAD (via `git stash`) against a build with this change applied.

The full test project will not build cleanly, and `dotnet test --filter FullyQualifiedName~BlockOrderProcessingIntegrationTests` cannot run, until the two sibling tasks also land — this is the expected intermediate state of this task plan's per-file split (see `full-solution-verification`, the final task, for whole-project verification).

## Notes
Step 3/Step 4 of the task context ("Build the test project" / run the filtered test) can't literally show "Build succeeded" yet because the test **project** (not just this file) also contains `ShoptetTestEnvironmentHydrationTests.cs` and `PickingListIntegrationTests.cs`, which still call the relocated methods directly on `IEshopOrderClient` and are out of scope for this task (they're separate pending tasks in `state.json`). I verified correctness of this task's own change with a before/after error-count diff instead (see above) rather than a literal "Build succeeded" console line, since that line can't appear until the sibling tasks are done.

## PR Summary
Repointed `BlockOrderProcessingIntegrationTests` — the one integration test file that mixes relocated (test-housekeeping) and retained (Application-layer) `IEshopOrderClient` methods — to call `CreateOrderAsync`/`DeleteOrderAsync` through the new `IShoptetOrderTestClient` instead, via a second DI-resolved field. The three methods the class also uses that Application code still depends on (`UpdateStatusAsync`, `UpdateEshopRemarkAsync`, `GetEshopRemarkAsync`) are untouched.

### Changes
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/BlockOrderProcessingIntegrationTests.cs` — added `IShoptetOrderTestClient` field/DI resolution; repointed 4 `CreateOrderAsync`/`DeleteOrderAsync` call sites to it

## Status
DONE
