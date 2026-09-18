# Implementation: packaging-handlers

## What was implemented

Replaced the direct `IEshopOrderClient` dependency (owned by the `ShoptetOrders` module) with the
consumer-owned `IPackedOrderStatusUpdater` contract (added to `Packaging.Contracts` by the
`packaging-contract` task, with `ShoptetOrdersPackedOrderStatusUpdaterAdapter` registered by the
`packaging-adapter` task) in both Packaging handlers that call `MarkAsPackedAsync`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — field/ctor param/usage renamed from `IEshopOrderClient _eshopOrderClient` to `IPackedOrderStatusUpdater _packedOrderStatusUpdater`; added `using ...Packaging.Contracts`. Kept `using ...ShoptetOrders` because `IPackingOrderClient`/`PackingOrder` (a separate, still-valid dependency covered by issue #3721, not this task) also live in that namespace — removing it entirely as the task-context literally specified would have broken the build.
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs` — same swap; `using ...ShoptetOrders` fully removed here since nothing else in the file needed it.
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` — mock field and all `Verify`/`Setup` calls retargeted to `Mock<IPackedOrderStatusUpdater>`; kept the `ShoptetOrders` using for `IPackingOrderClient`/`PackingOrder`.
- `backend/test/Anela.Heblo.Tests/Application/Packaging/CompletePackingOrderHandlerTests.cs` — same swap; `ShoptetOrders` using removed (no longer needed).
- `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs` — **not listed in the task-context** but also constructed `ScanPackingOrderHandler` with a raw `Mock<IEshopOrderClient>`; updated the same way to keep the build green.

## Tests

- `ScanPackingOrderHandlerTests` (22 cases incl. `Handle_LabelsExist_MarksOrderAsPacked`, `Handle_MarkAsPackedFails_StillReturnsSuccessfulScanResponse`) — all pass against the new mock type.
- `CompletePackingOrderHandlerTests` (incl. `Handle_WhenMarkAsPackedThrows_ReturnsPackingCompletionFailed`) — pass.
- `ScanPackingOrderHandlerPackagePersistenceTests` — pass (unaffected behaviourally; only its constructor mock type changed).

## How to verify

```bash
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj   # 0 errors
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ScanPackingOrderHandlerTests|FullyQualifiedName~CompletePackingOrderHandlerTests|FullyQualifiedName~ScanPackingOrderHandlerPackagePersistenceTests"
# 22 passed (ScanPackingOrderHandlerTests) + CompletePackingOrderHandlerTests + persistence tests, 0 failed
cd .. && dotnet build Anela.Heblo.sln   # full solution, 0 errors
```

## Notes

- The task-context's step 2 instruction ("Remove `using Anela.Heblo.Application.Features.ShoptetOrders;`") assumed that was the only type Packaging pulled from that namespace. It is not — `IPackingOrderClient`/`PackingOrder` (the separate #3721-era dependency) live there too and are still consumed by `ScanPackingOrderHandler`. Deviated from the literal instruction by keeping that using in `ScanPackingOrderHandler.cs` and its test; removing it fully in `CompletePackingOrderHandler.cs`/its test where nothing else needed it.
- Found and fixed one additional call site (`ScanPackingOrderHandlerPackagePersistenceTests.cs`) that the task-context did not enumerate; without it the build would not compile.
- Remaining `IEshopOrderClient` usages elsewhere in the repo (ExpeditionList, ShoptetOrders' own module registration/tests, `ModuleBoundariesTests`) are in scope for the still-pending `expeditionlist-*` and `module-boundary-enforcement` tasks, not this one — left untouched.

## PR Summary
Packaging's `ScanPackingOrderHandler` and `CompletePackingOrderHandler` now depend on `IPackedOrderStatusUpdater` (owned by `Packaging.Contracts`) instead of importing `ShoptetOrders.IEshopOrderClient` directly, following the consumer-owns-contract pattern already established by `IShipmentDeliveryChecker`/`ILeafletKnowledgeSource`. `ShoptetOrders` provides the implementation via the already-registered `ShoptetOrdersPackedOrderStatusUpdaterAdapter`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — consumes `IPackedOrderStatusUpdater`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs` — consumes `IPackedOrderStatusUpdater`
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`, `CompletePackingOrderHandlerTests.cs`, `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs` — updated mocks to match

## Status
DONE
