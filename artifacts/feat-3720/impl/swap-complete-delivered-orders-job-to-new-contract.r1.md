# Implementation: swap-complete-delivered-orders-job-to-new-contract

## What was implemented
Retyped `CompleteDeliveredOrdersJob`'s dependency from the six-method `IShipmentClient` (owned by `ShipmentLabels`) to the narrow, consumer-owned `IShipmentDeliveryChecker` (owned by `ShoptetOrders`). No change to `ExecuteAsync` control flow, logging, or job metadata — only the `using`, field type, and constructor parameter type changed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs` — `using Anela.Heblo.Application.Features.ShipmentLabels;` replaced with `using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;`; `_shipmentClient` field and constructor parameter retyped from `IShipmentClient` to `IShipmentDeliveryChecker`. The call site `_shipmentClient.HasDeliveredShipmentAsync(order.Code, cancellationToken)` is untouched — same signature on both interfaces.
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs` — `using` for `ShipmentLabels` replaced with `ShoptetOrders.Contracts`; `MakeSut`'s return-tuple type and local mock retyped from `Mock<IShipmentClient>` to `Mock<IShipmentDeliveryChecker>`. No other test code changed — every `shipments.Setup(...)`/`.Verify(...)` call already only referenced `HasDeliveredShipmentAsync`, whose signature is identical on the new interface.

## Tests
`CompleteDeliveredOrdersJobTests` — all 9 existing tests exercise `ExecuteAsync` behavior end to end (job-disabled skip, delivered/not-delivered branching, dry-run mode, test-source-state flag, note appending, per-order exception isolation) and continue to pass unmodified in behavior, only against the new mock type.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CompleteDeliveredOrdersJobTests"
dotnet build Anela.Heblo.sln
```
Results: 9/9 tests passed; build succeeded with 0 errors (13 pre-existing unrelated warnings + the known unrelated `AccessMatrixGen` MSB3073 post-build warning).

Also ran the full `Anela.Heblo.Tests` suite (`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`): 5898 passed, 76 failed, 4 skipped. All 76 failures are pre-existing Docker/testcontainers integration tests (`System.ArgumentException: Docker is either not running or misconfigured...`) in unrelated areas (Smartsupp, GridLayouts, KnowledgeBase, Article persistence, etc.) — this sandbox has no Docker daemon. None of the failures touch `ShoptetOrders`, `ShipmentLabels`, or `CompleteDeliveredOrdersJob`.

## Notes
As with the previous task, the developer subagent originally assigned to this task got stuck in a loop backgrounding `dotnet test`/`dotnet build` calls and never returned control. The orchestrator applied the exact task-context diff directly, killed the stray dotnet/MSBuild processes, and ran the verification commands synchronously to completion.

## PR Summary
Completes the interface-ownership inversion fix: `CompleteDeliveredOrdersJob` now depends on the narrow, consumer-owned `IShipmentDeliveryChecker` instead of `ShipmentLabels`' full six-method `IShipmentClient`, closing the compile-time coupling the arch-review flagged. Only the dependency type changed — behavior, logging, and job metadata are identical, and all 9 existing unit tests pass unmodified.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs`

## Status
DONE
